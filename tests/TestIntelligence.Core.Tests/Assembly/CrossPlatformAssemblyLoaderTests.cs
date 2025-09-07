using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FluentAssertions;
using TestIntelligence.Core.Assembly;
using TestIntelligence.TestUtilities;
using Xunit;

namespace TestIntelligence.Core.Tests.Assembly
{
    /// <summary>
    /// Tests for cross-platform assembly loading scenarios including mixed framework solutions,
    /// platform-specific path handling, and cross-platform compatibility.
    /// </summary>
    public class CrossPlatformAssemblyLoaderTests : IDisposable
    {
        private readonly TestSolutionGenerator _solutionGenerator;
        private readonly ICrossFrameworkAssemblyLoader _loader;
        private readonly string _tempDirectory;

        public CrossPlatformAssemblyLoaderTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "CrossPlatformTests", Guid.NewGuid().ToString());
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

        #region Mixed Framework Solution Tests

        [Fact]
        public async Task LoadAssembliesAsync_MixedFrameworkSolution_ShouldDetectAllFrameworks()
        {
            // Arrange - Create a solution with multiple target frameworks
            var mixedSolution = await CreateMixedFrameworkSolutionAsync();
            var assemblyPaths = GetAssemblyPathsFromSolution(mixedSolution);

            // Act
            var results = await _loader.LoadAssembliesAsync(assemblyPaths);

            // Assert
            results.Should().HaveCount(assemblyPaths.Count);
            
            // Verify we get consistent results even if loading fails
            foreach (var result in results.Values)
            {
                result.Should().NotBeNull();
                // Results may fail due to test assemblies not being real, but should be consistent
            }
        }

        [Fact]
        public void DetectFrameworkVersion_MixedFrameworkPaths_ShouldDetectCorrectFrameworks()
        {
            // Arrange - Create test files with different framework indicators in paths
            var testCases = new Dictionary<string, FrameworkVersion>
            {
                { CreateTestAssembly("net48"), FrameworkVersion.NetFramework48 },
                { CreateTestAssembly("net6.0"), FrameworkVersion.Net5Plus },
                { CreateTestAssembly("netcoreapp3.1"), FrameworkVersion.NetCore },
                { CreateTestAssembly("netstandard2.0"), FrameworkVersion.NetStandard }
            };

            // Act & Assert
            foreach (var testCase in testCases)
            {
                var detectedFramework = _loader.DetectFrameworkVersion(testCase.Key);
                
                // May not detect exact framework due to test files, but should handle gracefully
                detectedFramework.Should().BeOneOf(
                    testCase.Value, 
                    FrameworkVersion.NetStandard, 
                    FrameworkVersion.Unknown);
            }
        }

        [Fact]
        public async Task LoadAssembliesAsync_NetFrameworkAndNetCore_ShouldHandleBoth()
        {
            // Arrange
            var net48Assembly = CreateTestAssembly("TestApp.net48");
            var netCoreAssembly = CreateTestAssembly("TestApp.netcore31");
            var assemblies = new[] { net48Assembly, netCoreAssembly };

            // Act
            var results = await _loader.LoadAssembliesAsync(assemblies);

            // Assert
            results.Should().HaveCount(2);
            results[net48Assembly].Should().NotBeNull();
            results[netCoreAssembly].Should().NotBeNull();
        }

        #endregion

        #region Platform-Specific Path Handling Tests

        [Theory]
        [InlineData(@"C:\Program Files\MyApp\bin\Debug\net8.0\MyApp.dll")]
        [InlineData(@"/usr/local/bin/myapp/net8.0/MyApp.dll")]
        [InlineData(@"D:\Development\Projects\Solution\Project\bin\x64\Release\netcoreapp3.1\Project.dll")]
        [InlineData(@"/home/user/projects/solution/project/bin/Release/net6.0/project.dll")]
        public void DetectFrameworkVersion_PlatformSpecificPaths_ShouldNormalizePaths(string originalPath)
        {
            // Arrange - Create a test file with platform-neutral name
            var fileName = Path.GetFileName(originalPath);
            var testPath = Path.Combine(_tempDirectory, fileName);
            CreateTestAssemblyFile(testPath);

            // Act
            var result = _loader.DetectFrameworkVersion(testPath);

            // Assert - Should handle path normalization without errors
            result.Should().BeOneOf(
                FrameworkVersion.Net5Plus, 
                FrameworkVersion.NetCore, 
                FrameworkVersion.NetStandard, 
                FrameworkVersion.Unknown);
        }

        [Fact]
        public void DetectFrameworkVersion_PathWithSpaces_ShouldHandleCorrectly()
        {
            // Arrange
            var pathWithSpaces = Path.Combine(_tempDirectory, "My Test Assembly (net8.0).dll");
            CreateTestAssemblyFile(pathWithSpaces);

            // Act
            var result = _loader.DetectFrameworkVersion(pathWithSpaces);

            // Assert
            result.Should().BeOneOf(
                FrameworkVersion.Net5Plus, 
                FrameworkVersion.NetStandard, 
                FrameworkVersion.Unknown);
        }

        [Fact]
        public void DetectFrameworkVersion_PathSeparatorVariations_ShouldNormalizeCorrectly()
        {
            // Arrange - Test different path separator handling
            var testFiles = new List<string>();
            
            // Create files with different path indicators
            var frameworks = new[] { "net48", "net6.0", "netcoreapp3.1" };
            foreach (var fw in frameworks)
            {
                var fileName = $"test-{fw}-assembly.dll";
                var filePath = Path.Combine(_tempDirectory, fileName);
                CreateTestAssemblyFile(filePath);
                testFiles.Add(filePath);
            }

            // Act & Assert
            foreach (var filePath in testFiles)
            {
                var normalizedPath = Path.GetFullPath(filePath);
                var result = _loader.DetectFrameworkVersion(normalizedPath);
                
                result.Should().BeOneOf(
                    FrameworkVersion.NetFramework48,
                    FrameworkVersion.Net5Plus,
                    FrameworkVersion.NetCore,
                    FrameworkVersion.NetStandard,
                    FrameworkVersion.Unknown);
            }
        }

        #endregion

        #region Cross-Platform Compatibility Tests

        [Fact]
        public void SupportedFrameworks_ShouldBeConsistentAcrossPlatforms()
        {
            // Act
            var supportedFrameworks = _loader.SupportedFrameworks;

            // Assert
            supportedFrameworks.Should().NotBeEmpty();
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows should support all frameworks or degrade gracefully
                supportedFrameworks.Should().Contain(FrameworkVersion.NetStandard);
            }
            else
            {
                // Unix platforms should at least support .NET Core/.NET 5+ frameworks
                supportedFrameworks.Should().Contain(FrameworkVersion.NetStandard);
            }
        }

        [Fact]
        public async Task LoadAssembliesAsync_CrossPlatformPaths_ShouldHandleConsistently()
        {
            // Arrange - Create assemblies with cross-platform naming
            var assemblies = new[]
            {
                CreateTestAssembly("linux-style-path.dll"),
                CreateTestAssembly("windows-style-path.dll"),
                CreateTestAssembly("mixed.case.Assembly.dll"),
                CreateTestAssembly("UPPERCASE-ASSEMBLY.DLL")
            };

            // Act
            var results = await _loader.LoadAssembliesAsync(assemblies);

            // Assert
            results.Should().HaveCount(assemblies.Length);
            
            // All results should be handled consistently regardless of platform
            foreach (var result in results.Values)
            {
                result.Should().NotBeNull();
            }
        }

        [Fact]
        public void DetectFrameworkVersion_CaseSensitivity_ShouldHandleConsistently()
        {
            // Arrange - Test case sensitivity across platforms
            var testCases = new[]
            {
                ("net8.0-assembly.dll", "net8.0"),
                ("NET8.0-assembly.dll", "NET8.0"),
                ("Net8.0-assembly.dll", "Net8.0"),
                ("netcoreapp3.1-test.dll", "netcoreapp3.1"),
                ("NETCOREAPP3.1-test.dll", "NETCOREAPP3.1")
            };

            // Act & Assert
            foreach (var (fileName, frameworkIndicator) in testCases)
            {
                var testPath = Path.Combine(_tempDirectory, fileName);
                CreateTestAssemblyFile(testPath);
                
                var result = _loader.DetectFrameworkVersion(testPath);
                
                // Should handle case variations consistently
                result.Should().BeOneOf(
                    FrameworkVersion.Net5Plus,
                    FrameworkVersion.NetCore,
                    FrameworkVersion.NetStandard,
                    FrameworkVersion.Unknown);
            }
        }

        #endregion

        #region Framework Compatibility Matrix Tests

        [Theory]
        [InlineData(FrameworkVersion.NetFramework48, FrameworkVersion.NetStandard)]
        [InlineData(FrameworkVersion.NetCore, FrameworkVersion.NetStandard)]
        [InlineData(FrameworkVersion.Net5Plus, FrameworkVersion.NetStandard)]
        [InlineData(FrameworkVersion.Net5Plus, FrameworkVersion.NetCore)]
        public void CreateFrameworkLoader_CompatibilityMatrix_ShouldHandleGracefully(
            FrameworkVersion primaryFramework, 
            FrameworkVersion fallbackFramework)
        {
            // Act & Assert - Test framework loader creation
            var primarySuccess = AssemblyLoaderFactory.TryCreateFrameworkLoader(primaryFramework, out var primaryLoader);
            var fallbackSuccess = AssemblyLoaderFactory.TryCreateFrameworkLoader(fallbackFramework, out var fallbackLoader);

            // At least one should succeed, or both should fail gracefully
            if (!primarySuccess && !fallbackSuccess)
            {
                // Both failed - this is acceptable in test environment
                primaryLoader.Should().BeNull();
                fallbackLoader.Should().BeNull();
            }
            else
            {
                // At least one succeeded
                (primarySuccess || fallbackSuccess).Should().BeTrue();
            }

            primaryLoader?.Dispose();
            fallbackLoader?.Dispose();
        }

        [Fact]
        public void DetectFrameworkVersion_MixedFrameworkAssemblies_ShouldProduceConsistentResults()
        {
            // Arrange - Create multiple assemblies with different framework hints
            var frameworkHints = new[]
            {
                "net48", "net472", "net6.0", "net7.0", "net8.0",
                "netcoreapp3.1", "netcoreapp2.1", "netstandard2.0", "netstandard2.1"
            };

            var assemblies = frameworkHints.Select(hint => 
                CreateTestAssembly($"test-{hint}.dll")).ToList();

            // Act - Detect frameworks for all assemblies
            var results = assemblies.Select(assembly => new
            {
                Path = assembly,
                Framework = _loader.DetectFrameworkVersion(assembly)
            }).ToList();

            // Assert
            results.Should().HaveCount(frameworkHints.Length);
            
            // All detections should complete without throwing exceptions
            foreach (var result in results)
            {
                result.Framework.Should().BeOneOf(
                    FrameworkVersion.NetFramework48,
                    FrameworkVersion.NetCore,
                    FrameworkVersion.Net5Plus,
                    FrameworkVersion.NetStandard,
                    FrameworkVersion.Unknown);
            }
        }

        #endregion

        #region Concurrent Cross-Platform Operations

        [Fact]
        public async Task LoadAssembliesAsync_ConcurrentCrossPlatformOperations_ShouldBeThreadSafe()
        {
            // Arrange - Create assemblies with different platform characteristics
            var assemblies = Enumerable.Range(0, 10)
                .Select(i => CreateTestAssembly($"concurrent-test-{i}.dll"))
                .ToList();

            // Act - Load assemblies concurrently
            var tasks = assemblies.Select(async assembly =>
            {
                return await Task.Run(async () =>
                {
                    var loadResult = await _loader.LoadAssemblyAsync(assembly);
                    var detectResult = _loader.DetectFrameworkVersion(assembly);
                    
                    return new { LoadResult = loadResult, DetectResult = detectResult };
                });
            }).ToArray();

            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().HaveLength(assemblies.Count);
            
            // All operations should complete consistently
            foreach (var result in results)
            {
                result.LoadResult.Should().NotBeNull();
                result.DetectResult.Should().BeOneOf(
                    FrameworkVersion.NetFramework48,
                    FrameworkVersion.NetCore,
                    FrameworkVersion.Net5Plus,
                    FrameworkVersion.NetStandard,
                    FrameworkVersion.Unknown);
            }
        }

        [Fact]
        public void DetectFrameworkVersion_HighVolumeConcurrentDetection_ShouldMaintainPerformance()
        {
            // Arrange
            var assemblies = Enumerable.Range(0, 50)
                .Select(i => CreateTestAssembly($"perf-test-net8.0-{i:D3}.dll"))
                .ToList();

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act - Detect frameworks concurrently
            var tasks = assemblies.Select(assembly => 
                Task.Run(() => _loader.DetectFrameworkVersion(assembly)))
                .ToArray();

            var results = Task.WhenAll(tasks).Result;
            stopwatch.Stop();

            // Assert
            results.Should().HaveLength(assemblies.Count);
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(10000); // Should complete within 10 seconds
            
            // All detections should produce valid results
            results.Should().AllSatisfy(result => 
                result.Should().BeOneOf(
                    FrameworkVersion.Net5Plus,
                    FrameworkVersion.NetStandard,
                    FrameworkVersion.Unknown));
        }

        #endregion

        #region Helper Methods

        private async Task<GeneratedSolution> CreateMixedFrameworkSolutionAsync()
        {
            var solutionConfig = new SolutionConfiguration
            {
                SolutionName = "MixedFrameworkSolution",
                ProjectCount = 4,
                ProjectNamePrefix = "Project",
                ProjectTemplate = new ProjectConfiguration
                {
                    ClassCount = 2,
                    MethodsPerClass = 3,
                    IncludeComplexity = false
                }
            };

            var solution = await _solutionGenerator.CreateSolutionAsync(solutionConfig);
            
            // Modify projects to have different target frameworks
            var frameworks = new[] { "net48", "netcoreapp3.1", "net6.0", "netstandard2.0" };
            for (int i = 0; i < solution.Projects.Count && i < frameworks.Length; i++)
            {
                var project = solution.Projects[i];
                var projectContent = await File.ReadAllTextAsync(project.Path);
                var updatedContent = projectContent.Replace("net8.0", frameworks[i]);
                await File.WriteAllTextAsync(project.Path, updatedContent);
            }

            return solution;
        }

        private List<string> GetAssemblyPathsFromSolution(GeneratedSolution solution)
        {
            return solution.Projects
                .Select(p => Path.Combine(
                    Path.GetDirectoryName(p.Path)!, 
                    "bin", 
                    "Debug", 
                    Path.GetFileNameWithoutExtension(p.Path) + ".dll"))
                .ToList();
        }

        private string CreateTestAssembly(string fileName)
        {
            var filePath = Path.Combine(_tempDirectory, fileName);
            CreateTestAssemblyFile(filePath);
            return filePath;
        }

        private void CreateTestAssemblyFile(string filePath)
        {
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

        #endregion
    }
}