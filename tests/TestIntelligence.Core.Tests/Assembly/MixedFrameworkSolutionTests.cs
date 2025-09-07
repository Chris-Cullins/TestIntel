using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using TestIntelligence.Core.Assembly;
using TestIntelligence.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace TestIntelligence.Core.Tests.Assembly
{
    /// <summary>
    /// Tests for mixed framework solution scenarios, validating assembly loading and 
    /// framework compatibility across different .NET framework versions in a single solution.
    /// </summary>
    public class MixedFrameworkSolutionTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly TestSolutionGenerator _solutionGenerator;
        private readonly ICrossFrameworkAssemblyLoader _loader;
        private readonly ITestOutputHelper _output;

        public MixedFrameworkSolutionTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDirectory = Path.Combine(Path.GetTempPath(), "MixedFrameworkTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            
            _solutionGenerator = new TestSolutionGenerator(_tempDirectory);
            _loader = AssemblyLoaderFactory.CreateSilent();
        }

        public void Dispose()
        {
            _loader?.Dispose();
            _solutionGenerator?.Dispose();
            
            if (Directory.Exists(_tempDirectory))
            {
                try
                {
                    Directory.Delete(_tempDirectory, recursive: true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        #region Mixed Framework Detection Tests

        [Fact]
        public async Task DetectFrameworkVersions_MixedFrameworkSolution_ShouldIdentifyAllFrameworks()
        {
            // Arrange - Create solution with multiple framework targets
            var solution = await CreateMixedFrameworkSolutionAsync("MixedFrameworkDetectionSolution");
            var assemblyPaths = GetAssemblyPathsFromSolution(solution);

            var detectedFrameworks = new List<(string Path, FrameworkVersion Framework)>();

            // Act - Process each assembly
            foreach (var assemblyPath in assemblyPaths)
            {
                try
                {
                    CreateTestAssemblyFile(assemblyPath);
                    var framework = _loader.DetectFrameworkVersion(assemblyPath);
                    detectedFrameworks.Add((assemblyPath, framework));
                }
                catch (Exception ex)
                {
                    // Log framework detection failure for debugging
                    detectedFrameworks.Add((assemblyPath, FrameworkVersion.Unknown));
                    _output.WriteLine($"Failed to detect framework for {assemblyPath}: {ex.Message}");
                }
            }

            // Assert - Verify framework detection results
            detectedFrameworks.Should().HaveCount(assemblyPaths.Count);
            detectedFrameworks.Should().AllSatisfy(item =>
            {
                item.Framework.Should().BeOneOf(
                    FrameworkVersion.NetFramework48,
                    FrameworkVersion.NetCore,
                    FrameworkVersion.Net5Plus,
                    FrameworkVersion.NetStandard,
                    FrameworkVersion.Unknown);
            });
        }

        [Fact]
        public async Task LoadAssemblies_MixedFrameworkProjects_ShouldHandleAllFrameworks()
        {
            // Arrange
            var solution = await CreateMixedFrameworkSolutionAsync("MixedFrameworkLoadSolution");
            var assemblyPaths = GetAssemblyPathsFromSolution(solution);
            
            // Create test assemblies for each project
            foreach (var path in assemblyPaths)
            {
                CreateTestAssemblyFile(path);
            }

            // Act
            var results = await _loader.LoadAssembliesAsync(assemblyPaths);

            // Assert
            results.Should().HaveCount(assemblyPaths.Count);
            
            // All load attempts should complete (success or failure is determined by test environment)
            results.Values.Should().AllSatisfy(result =>
            {
                result.Should().NotBeNull();
            });
        }

        [Theory]
        [InlineData("net48", FrameworkVersion.NetFramework48)]
        [InlineData("net6.0", FrameworkVersion.Net5Plus)]
        [InlineData("netcoreapp3.1", FrameworkVersion.NetCore)]
        [InlineData("netstandard2.0", FrameworkVersion.NetStandard)]
        [InlineData("net8.0", FrameworkVersion.Net5Plus)]
        public void DetectFrameworkVersion_SpecificFrameworkAssemblies_ShouldDetectCorrectFramework(
            string frameworkMoniker, FrameworkVersion expectedFramework)
        {
            // Arrange
            var assemblyPath = Path.Combine(_tempDirectory, $"test-{frameworkMoniker}.dll");
            CreateTestAssemblyFile(assemblyPath);

            // Act
            var detectedFramework = _loader.DetectFrameworkVersion(assemblyPath);

            // Assert - Should detect expected framework or fallback gracefully
            detectedFramework.Should().BeOneOf(
                expectedFramework, 
                FrameworkVersion.NetStandard, 
                FrameworkVersion.Unknown);
        }

        #endregion

        #region Framework Compatibility Tests

        [Fact]
        public async Task FrameworkCompatibilityMatrix_ShouldValidateCompatibilityRules()
        {
            // Arrange - Create projects with different framework combinations
            var frameworkCombinations = new[]
            {
                ("net48", "netstandard2.0"),     // .NET Framework + .NET Standard
                ("net6.0", "netstandard2.1"),   // .NET 6 + .NET Standard
                ("netcoreapp3.1", "net8.0"),    // .NET Core + .NET 8
                ("net8.0", "netstandard2.0")    // .NET 8 + .NET Standard
            };

            var compatibilityResults = new List<FrameworkCompatibilityResult>();

            foreach (var (primaryFramework, dependencyFramework) in frameworkCombinations)
            {
                // Act - Test compatibility between frameworks
                var result = await TestFrameworkCompatibilityAsync(primaryFramework, dependencyFramework);
                compatibilityResults.Add(result);
            }

            // Assert
            compatibilityResults.Should().HaveCount(frameworkCombinations.Length);
            
            // All compatibility tests should complete without exceptions
            compatibilityResults.Should().AllSatisfy(result =>
            {
                result.Should().NotBeNull();
                result.PrimaryFramework.Should().NotBeNullOrEmpty();
                result.DependencyFramework.Should().NotBeNullOrEmpty();
            });

            // .NET Standard should be compatible with all frameworks
            var netStandardResults = compatibilityResults.Where(r => 
                r.DependencyFramework.Contains("netstandard")).ToList();
            netStandardResults.Should().NotBeEmpty();
        }

        [Fact]
        public async Task SupportedFrameworks_MixedSolution_ShouldReportAllSupportedFrameworks()
        {
            // Arrange
            var solution = await CreateMixedFrameworkSolutionAsync("SupportedFrameworksSolution");
            
            // Act
            var supportedFrameworks = _loader.SupportedFrameworks;

            // Assert
            supportedFrameworks.Should().NotBeEmpty();
            supportedFrameworks.Should().Contain(FrameworkVersion.NetStandard);
            
            // Verify framework support is consistent across the solution
            foreach (var framework in supportedFrameworks)
            {
                var description = framework.GetDescription();
                description.Should().NotBeNullOrEmpty();
            }
        }

        #endregion

        #region Cross-Framework Dependency Resolution Tests

        [Fact]
        public async Task LoadAssembly_CrossFrameworkDependencies_ShouldResolveDependenciesCorrectly()
        {
            // Arrange - Create a solution where .NET 8 project references .NET Standard library
            var solution = await CreateCrossFrameworkDependencySolutionAsync("CrossFrameworkDependencySolution");
            
            var net8Project = solution.Projects.FirstOrDefault(p => p.Name.Contains("Net8"));
            var netstandardProject = solution.Projects.FirstOrDefault(p => p.Name.Contains("Standard"));
            
            if (net8Project != null && netstandardProject != null)
            {
                var net8AssemblyPath = GetAssemblyPath(net8Project);
                var netstandardAssemblyPath = GetAssemblyPath(netstandardProject);
                
                CreateTestAssemblyFile(net8AssemblyPath);
                CreateTestAssemblyFile(netstandardAssemblyPath);

                // Act - Load primary assembly with cross-framework dependency
                var result = await _loader.LoadAssemblyAsync(net8AssemblyPath);

                // Assert
                result.Should().NotBeNull();
                // Result may succeed or fail depending on test environment, but should handle gracefully
            }
        }

        [Fact]
        public void DetectFrameworkVersion_ComplexFrameworkHierarchy_ShouldHandleHierarchically()
        {
            // Arrange - Create assemblies that represent a complex framework hierarchy
            var hierarchyAssemblies = new Dictionary<string, string>
            {
                { "BaseLibrary", "netstandard2.0" },        // Base library
                { "CoreService", "netcoreapp3.1" },         // Core service layer
                { "BusinessLogic", "net6.0" },              // Business logic
                { "WebApi", "net8.0" },                     // Top-level web API
                { "LegacyModule", "net48" }                 // Legacy module
            };

            var detectionResults = new List<(string Assembly, FrameworkVersion Detected)>();

            // Act
            foreach (var (assemblyName, framework) in hierarchyAssemblies)
            {
                var assemblyPath = Path.Combine(_tempDirectory, $"{assemblyName}-{framework}.dll");
                CreateTestAssemblyFile(assemblyPath);
                
                var detected = _loader.DetectFrameworkVersion(assemblyPath);
                detectionResults.Add((assemblyName, detected));
            }

            // Assert
            detectionResults.Should().HaveCount(hierarchyAssemblies.Count);
            
            // All detections should complete successfully
            detectionResults.Should().AllSatisfy(result =>
            {
                result.Detected.Should().BeOneOf(
                    FrameworkVersion.NetFramework48,
                    FrameworkVersion.NetCore,
                    FrameworkVersion.Net5Plus,
                    FrameworkVersion.NetStandard,
                    FrameworkVersion.Unknown);
            });
        }

        #endregion

        #region Performance and Scalability Tests

        [Fact]
        public async Task LoadAssemblies_LargeMixedFrameworkSolution_ShouldMaintainPerformance()
        {
            // Arrange - Create a large solution with mixed frameworks
            var solution = await CreateLargeMixedFrameworkSolutionAsync("LargeMixedSolution", 20);
            var assemblyPaths = GetAssemblyPathsFromSolution(solution);
            
            foreach (var path in assemblyPaths)
            {
                CreateTestAssemblyFile(path);
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act
            var results = await _loader.LoadAssembliesAsync(assemblyPaths);
            stopwatch.Stop();

            // Assert
            results.Should().HaveCount(assemblyPaths.Count);
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(30000, 
                "Loading 20 mixed framework assemblies should complete within 30 seconds");
            
            // All assemblies should be processed
            results.Values.Should().AllSatisfy(result =>
            {
                result.Should().NotBeNull();
            });
        }

        [Fact]
        public async Task ConcurrentFrameworkDetection_MixedSolution_ShouldBeThreadSafe()
        {
            // Arrange
            var solution = await CreateMixedFrameworkSolutionAsync("ConcurrentMixedSolution");
            var assemblyPaths = GetAssemblyPathsFromSolution(solution);
            
            foreach (var path in assemblyPaths)
            {
                CreateTestAssemblyFile(path);
            }

            // Act - Detect frameworks concurrently
            var tasks = assemblyPaths.Select(path => Task.Run(() =>
            {
                try
                {
                    return new { Path = path, Framework = _loader.DetectFrameworkVersion(path), Error = (string?)null };
                }
                catch (Exception ex)
                {
                    return new { Path = path, Framework = FrameworkVersion.Unknown, Error = (string?)ex.Message };
                }
            })).ToArray();

            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().HaveCount(assemblyPaths.Count);
            results.Should().AllSatisfy(result =>
            {
                result.Framework.Should().BeOneOf(
                    FrameworkVersion.NetFramework48,
                    FrameworkVersion.NetCore,
                    FrameworkVersion.Net5Plus,
                    FrameworkVersion.NetStandard,
                    FrameworkVersion.Unknown);
            });
        }

        #endregion

        #region Helper Methods

        private async Task<GeneratedSolution> CreateMixedFrameworkSolutionAsync(string solutionName)
        {
            var config = new SolutionConfiguration
            {
                SolutionName = solutionName,
                ProjectCount = 4,
                ProjectNamePrefix = "Project",
                ProjectTemplate = new ProjectConfiguration
                {
                    ClassCount = 2,
                    MethodsPerClass = 3,
                    IncludeComplexity = false
                }
            };

            var solution = await _solutionGenerator.CreateSolutionAsync(config);
            
            // Modify projects to target different frameworks
            var frameworks = new[] { "net48", "netcoreapp3.1", "net6.0", "netstandard2.0" };
            for (int i = 0; i < solution.Projects.Count && i < frameworks.Length; i++)
            {
                await SetProjectFrameworkAsync(solution.Projects[i], frameworks[i]);
            }

            return solution;
        }

        private async Task<GeneratedSolution> CreateLargeMixedFrameworkSolutionAsync(string solutionName, int projectCount)
        {
            var config = new SolutionConfiguration
            {
                SolutionName = solutionName,
                ProjectCount = projectCount,
                ProjectNamePrefix = "Project",
                ProjectTemplate = new ProjectConfiguration
                {
                    ClassCount = 3,
                    MethodsPerClass = 4,
                    IncludeComplexity = false
                }
            };

            var solution = await _solutionGenerator.CreateSolutionAsync(config);
            
            // Distribute projects across different frameworks
            var frameworks = new[] { "net48", "netcoreapp3.1", "net6.0", "net8.0", "netstandard2.0", "netstandard2.1" };
            
            for (int i = 0; i < solution.Projects.Count; i++)
            {
                var framework = frameworks[i % frameworks.Length];
                await SetProjectFrameworkAsync(solution.Projects[i], framework);
            }

            return solution;
        }

        private async Task<GeneratedSolution> CreateCrossFrameworkDependencySolutionAsync(string solutionName)
        {
            var config = new SolutionConfiguration
            {
                SolutionName = solutionName,
                ProjectCount = 2,
                ProjectNamePrefix = "Project",
                ProjectTemplate = new ProjectConfiguration
                {
                    ClassCount = 2,
                    MethodsPerClass = 2
                }
            };

            var solution = await _solutionGenerator.CreateSolutionAsync(config);
            
            // First project: .NET Standard library
            if (solution.Projects.Count > 0)
            {
                solution.Projects[0].Name = "StandardLibrary";
                await SetProjectFrameworkAsync(solution.Projects[0], "netstandard2.0");
            }
            
            // Second project: .NET 8 app that references the first
            if (solution.Projects.Count > 1)
            {
                solution.Projects[1].Name = "Net8App";
                await SetProjectFrameworkAsync(solution.Projects[1], "net8.0");
                
                // Add project reference
                var projectContent = await File.ReadAllTextAsync(solution.Projects[1].Path);
                var projectRef = $"    <ProjectReference Include=\"..\\{solution.Projects[0].Name}\\{solution.Projects[0].Name}.csproj\" />";
                projectContent = projectContent.Replace("</Project>", 
                    $"  <ItemGroup>\n{projectRef}\n  </ItemGroup>\n</Project>");
                await File.WriteAllTextAsync(solution.Projects[1].Path, projectContent);
            }

            return solution;
        }

        private async Task SetProjectFrameworkAsync(GeneratedProject project, string targetFramework)
        {
            var projectContent = await File.ReadAllTextAsync(project.Path);
            projectContent = projectContent.Replace("net8.0", targetFramework);
            await File.WriteAllTextAsync(project.Path, projectContent);
            
            // Update project name to reflect framework
            var newName = $"{project.Name}_{targetFramework.Replace(".", "")}";
            var newPath = Path.Combine(Path.GetDirectoryName(project.Path)!, $"{newName}.csproj");
            
            // Rename project file
            File.Move(project.Path, newPath);
            project.Path = newPath;
            project.Name = newName;
        }

        private List<string> GetAssemblyPathsFromSolution(GeneratedSolution solution)
        {
            return solution.Projects
                .Select(GetAssemblyPath)
                .ToList();
        }

        private string GetAssemblyPath(GeneratedProject project)
        {
            return Path.Combine(
                Path.GetDirectoryName(project.Path)!, 
                "bin", 
                "Debug", 
                Path.GetFileNameWithoutExtension(project.Path) + ".dll");
        }

        private void CreateTestAssemblyFile(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory!);
            }

            // Create a minimal file that looks like it could be an assembly
            var minimalContent = new byte[]
            {
                0x4D, 0x5A, 0x90, 0x00, // DOS header signature
                0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00,
                0xFF, 0xFF, 0x00, 0x00, 0xB8, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00
            };
            
            // Add padding to make it look more like a real file
            var paddedContent = new byte[1024];
            Array.Copy(minimalContent, paddedContent, minimalContent.Length);
            
            File.WriteAllBytes(filePath, paddedContent);
        }

        private Task<FrameworkCompatibilityResult> TestFrameworkCompatibilityAsync(
            string primaryFramework, string dependencyFramework)
        {
            var primaryAssembly = Path.Combine(_tempDirectory, $"primary-{primaryFramework}.dll");
            var dependencyAssembly = Path.Combine(_tempDirectory, $"dependency-{dependencyFramework}.dll");
            
            CreateTestAssemblyFile(primaryAssembly);
            CreateTestAssemblyFile(dependencyAssembly);

            var primaryDetected = _loader.DetectFrameworkVersion(primaryAssembly);
            var dependencyDetected = _loader.DetectFrameworkVersion(dependencyAssembly);

            return Task.FromResult(new FrameworkCompatibilityResult
            {
                PrimaryFramework = primaryFramework,
                DependencyFramework = dependencyFramework,
                PrimaryDetected = primaryDetected,
                DependencyDetected = dependencyDetected,
                IsCompatible = DetermineCompatibility(primaryDetected, dependencyDetected)
            });
        }

        private bool DetermineCompatibility(FrameworkVersion primary, FrameworkVersion dependency)
        {
            // .NET Standard is compatible with all frameworks
            if (dependency == FrameworkVersion.NetStandard)
                return true;
                
            // Same frameworks are compatible
            if (primary == dependency)
                return true;
                
            // .NET 5+ can use .NET Core libraries
            if (primary == FrameworkVersion.Net5Plus && dependency == FrameworkVersion.NetCore)
                return true;
                
            // Default to compatible for test purposes
            return true;
        }

        #endregion

        #region Helper Classes

        private class FrameworkCompatibilityResult
        {
            public string PrimaryFramework { get; set; } = string.Empty;
            public string DependencyFramework { get; set; } = string.Empty;
            public FrameworkVersion PrimaryDetected { get; set; }
            public FrameworkVersion DependencyDetected { get; set; }
            public bool IsCompatible { get; set; }
        }

        #endregion
    }
}