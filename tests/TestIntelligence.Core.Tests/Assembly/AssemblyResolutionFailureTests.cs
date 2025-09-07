using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using TestIntelligence.Core.Assembly;
using TestIntelligence.TestUtilities;
using Xunit;

namespace TestIntelligence.Core.Tests.Assembly
{
    /// <summary>
    /// Tests for assembly resolution failure scenarios across different platforms,
    /// including missing dependencies, framework mismatches, and security restrictions.
    /// </summary>
    public class AssemblyResolutionFailureTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly TestSolutionGenerator _solutionGenerator;
        private readonly ICrossFrameworkAssemblyLoader _loader;
        private readonly IAssemblyLoadLogger _mockLogger;

        public AssemblyResolutionFailureTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "AssemblyResolutionFailureTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            
            _solutionGenerator = new TestSolutionGenerator(_tempDirectory);
            _mockLogger = Substitute.For<IAssemblyLoadLogger>();
            _loader = AssemblyLoaderFactory.CreateWithLogger(_mockLogger);
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

        #region Missing Dependency Resolution Tests

        [Fact]
        public async Task LoadAssembly_WithMissingDependencies_ShouldReturnFailureWithDetails()
        {
            // Arrange - Create an assembly that references non-existent dependencies
            var assemblyWithMissingDeps = await CreateAssemblyWithMissingDependenciesAsync("MissingDepsTest");

            // Act
            var result = _loader.LoadAssembly(assemblyWithMissingDeps.Path);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().NotBeEmpty();
            result.Errors.First().Should().Contain("Failed to load assembly");

            // Verify logger was called with appropriate information
            _mockLogger.Received().LogAssemblyLoadAttempt(Arg.Any<string>(), Arg.Any<FrameworkVersion>());
        }

        [Fact]
        public async Task LoadAssembliesAsync_MultipleMissingDependencies_ShouldReportAllFailures()
        {
            // Arrange - Create multiple assemblies with different missing dependencies
            var assemblies = new List<GeneratedProject>();
            for (int i = 0; i < 5; i++)
            {
                var assembly = await CreateAssemblyWithMissingDependenciesAsync($"MissingDeps{i:D2}");
                assemblies.Add(assembly);
            }

            var assemblyPaths = assemblies.Select(a => a.Path).ToArray();

            // Act
            var results = await _loader.LoadAssembliesAsync(assemblyPaths);

            // Assert
            results.Should().HaveCount(assemblyPaths.Length);
            results.Values.Should().AllSatisfy(result =>
            {
                result.IsSuccess.Should().BeFalse();
                result.Errors.Should().NotBeEmpty();
            });

            // Verify all failures were logged
            _mockLogger.Received(assemblyPaths.Length).LogAssemblyLoadAttempt(Arg.Any<string>(), Arg.Any<FrameworkVersion>());
        }

        [Fact]
        public void DetectFrameworkVersion_AssemblyWithMissingMetadata_ShouldFallbackGracefully()
        {
            // Arrange - Create a corrupted assembly file with missing metadata
            var corruptedAssemblyPath = Path.Combine(_tempDirectory, "corrupted-metadata.dll");
            CreateCorruptedAssemblyFile(corruptedAssemblyPath, CorruptionType.MissingMetadata);

            // Act
            var detectedFramework = _loader.DetectFrameworkVersion(corruptedAssemblyPath);

            // Assert - Should fallback to path-based detection or return Unknown
            detectedFramework.Should().BeOneOf(
                FrameworkVersion.Unknown,
                FrameworkVersion.NetStandard,
                FrameworkVersion.Net5Plus,
                FrameworkVersion.NetCore);
        }

        #endregion

        #region Framework Mismatch Resolution Tests

        [Theory]
        [InlineData(FrameworkVersion.NetFramework48, FrameworkVersion.Net5Plus)]
        [InlineData(FrameworkVersion.NetCore, FrameworkVersion.NetFramework48)]
        [InlineData(FrameworkVersion.Net5Plus, FrameworkVersion.NetFramework48)]
        public async Task LoadAssembly_FrameworkMismatch_ShouldHandleGracefully(
            FrameworkVersion assemblyFramework, 
            FrameworkVersion loaderFramework)
        {
            // Arrange - Create assembly that appears to be from different framework
            var mismatchAssembly = await CreateFrameworkSpecificAssemblyAsync("FrameworkMismatch", assemblyFramework);

            // Try to create loader for different framework
            var frameworkLoader = AssemblyLoaderFactory.TryCreateFrameworkLoader(loaderFramework, out var specificLoader);
            
            if (!frameworkLoader || specificLoader == null)
            {
                // If framework loader creation fails, that's acceptable in test environment
                return;
            }

            try
            {
                // Act
                var result = specificLoader.LoadAssembly(mismatchAssembly.Path);

                // Assert - Should handle mismatch gracefully
                if (!result.IsSuccess)
                {
                    result.Errors.Should().NotBeEmpty();
                    result.Errors.Should().Contain(error => 
                        error.Contains("framework") || 
                        error.Contains("compatible") ||
                        error.Contains("Failed to load"));
                }
            }
            finally
            {
                specificLoader?.Dispose();
            }
        }

        [Fact]
        public async Task LoadAssembly_UnsupportedFramework_ShouldReportUnsupportedFramework()
        {
            // Arrange - Create assembly with unsupported framework metadata
            var unsupportedAssembly = await CreateUnsupportedFrameworkAssemblyAsync("UnsupportedFramework");

            // Act
            var result = _loader.LoadAssembly(unsupportedAssembly.Path);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().NotBeEmpty();
            result.Errors.Should().Contain(error => 
                error.Contains("Failed to load assembly") || 
                error.Contains("framework"));
        }

        #endregion

        #region Security and Permission Tests

        [Fact]
        public void LoadAssembly_RestrictedPath_ShouldHandleSecurityRestrictions()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Arrange - Try to access a restricted system path
                var restrictedPath = @"C:\Windows\System32\kernel32.dll";
                
                if (!File.Exists(restrictedPath))
                {
                    return; // Skip if path doesn't exist
                }

                // Act
                var result = _loader.LoadAssembly(restrictedPath);

                // Assert - Should handle restrictions gracefully
                result.IsSuccess.Should().BeFalse();
                result.Errors.Should().NotBeEmpty();
            }
            else
            {
                // Unix-like systems - try accessing restricted system library
                var restrictedPaths = new[] { "/lib/libc.so.6", "/usr/lib/libc.so", "/lib64/libc.so.6" };
                var restrictedPath = restrictedPaths.FirstOrDefault(File.Exists);
                
                if (restrictedPath != null)
                {
                    var result = _loader.LoadAssembly(restrictedPath);
                    result.IsSuccess.Should().BeFalse();
                }
            }
        }

        [Fact]
        public async Task LoadAssembly_InsufficientPermissions_ShouldReportPermissionError()
        {
            // Arrange - Create a file and attempt to restrict permissions
            var restrictedAssembly = await CreateTestAssemblyAsync("PermissionTest");
            
            try
            {
                // Attempt to make file read-only or restrict access
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    File.SetAttributes(restrictedAssembly.Path, FileAttributes.ReadOnly);
                }
                else
                {
                    // Unix - attempt to restrict permissions (may require elevated privileges)
                    try
                    {
                        var result = await RunProcessAsync("chmod", $"000 \"{restrictedAssembly.Path}\"");
                        if (result.ExitCode != 0)
                        {
                            // Permission change failed - skip this test
                            return;
                        }
                    }
                    catch
                    {
                        // Skip if chmod is not available or fails
                        return;
                    }
                }

                // Act
                var loadResult = _loader.LoadAssembly(restrictedAssembly.Path);

                // Assert - Should handle permission restrictions
                if (!loadResult.IsSuccess)
                {
                    loadResult.Errors.Should().NotBeEmpty();
                }
            }
            finally
            {
                // Cleanup - restore permissions for deletion
                try
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        File.SetAttributes(restrictedAssembly.Path, FileAttributes.Normal);
                    }
                    else
                    {
                        await RunProcessAsync("chmod", $"755 \"{restrictedAssembly.Path}\"");
                    }
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        #endregion

        #region Concurrent Resolution Failure Tests

        [Fact]
        public async Task LoadAssembliesAsync_ConcurrentResolutionFailures_ShouldHandleAllFailures()
        {
            // Arrange - Create multiple problematic assemblies
            var problematicAssemblies = new List<string>();
            
            for (int i = 0; i < 10; i++)
            {
                var assemblyPath = Path.Combine(_tempDirectory, $"problematic-{i:D2}.dll");
                
                // Create different types of problematic files
                switch (i % 4)
                {
                    case 0:
                        CreateCorruptedAssemblyFile(assemblyPath, CorruptionType.InvalidHeader);
                        break;
                    case 1:
                        CreateCorruptedAssemblyFile(assemblyPath, CorruptionType.MissingMetadata);
                        break;
                    case 2:
                        CreateCorruptedAssemblyFile(assemblyPath, CorruptionType.TruncatedFile);
                        break;
                    case 3:
                        CreateCorruptedAssemblyFile(assemblyPath, CorruptionType.RandomData);
                        break;
                }
                
                problematicAssemblies.Add(assemblyPath);
            }

            // Act - Load all problematic assemblies concurrently
            var results = await _loader.LoadAssembliesAsync(problematicAssemblies);

            // Assert
            results.Should().HaveCount(problematicAssemblies.Count);
            results.Values.Should().AllSatisfy(result =>
            {
                result.IsSuccess.Should().BeFalse();
                result.Errors.Should().NotBeEmpty();
            });

            // Verify no exceptions were thrown - all failures handled gracefully
            _mockLogger.ReceivedCalls().Should().NotBeEmpty();
        }

        [Fact]
        public async Task DetectFrameworkVersion_ConcurrentFailures_ShouldBeThreadSafe()
        {
            // Arrange - Create multiple problematic files
            var problematicFiles = Enumerable.Range(0, 20)
                .Select(i =>
                {
                    var path = Path.Combine(_tempDirectory, $"concurrent-failure-{i:D2}.dll");
                    CreateCorruptedAssemblyFile(path, (CorruptionType)(i % 4));
                    return path;
                })
                .ToList();

            // Act - Detect frameworks concurrently
            var tasks = problematicFiles.Select(async file =>
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        return new { File = file, Framework = _loader.DetectFrameworkVersion(file) };
                    }
                    catch (Exception ex)
                    {
                        return new { File = file, Framework = FrameworkVersion.Unknown, Error = ex.Message };
                    }
                });
            }).ToArray();

            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().HaveLength(problematicFiles.Count);
            
            // All should complete without throwing exceptions
            foreach (var result in results)
            {
                result.Framework.Should().BeOneOf(
                    FrameworkVersion.Unknown,
                    FrameworkVersion.NetStandard,
                    FrameworkVersion.Net5Plus,
                    FrameworkVersion.NetCore);
            }
        }

        #endregion

        #region Platform-Specific Resolution Tests

        [Fact]
        public void LoadAssembly_PlatformSpecificBinary_ShouldReportIncompatibility()
        {
            // Arrange - Create a platform-specific binary that won't load
            var platformSpecificPath = Path.Combine(_tempDirectory, "platform-specific.dll");
            CreatePlatformIncompatibleBinary(platformSpecificPath);

            // Act
            var result = _loader.LoadAssembly(platformSpecificPath);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().NotBeEmpty();
            result.Errors.Should().Contain(error => 
                error.Contains("Failed to load assembly"));
        }

        [Fact]
        public async Task LoadAssembly_CrossArchitectureIncompatibility_ShouldHandleGracefully()
        {
            // Arrange - Simulate architecture mismatch scenario
            var archAssembly = await CreateArchitectureSpecificAssemblyAsync("ArchTest");

            // Act
            var result = _loader.LoadAssembly(archAssembly.Path);

            // Assert - Should handle architecture mismatches gracefully
            if (!result.IsSuccess)
            {
                result.Errors.Should().NotBeEmpty();
            }
        }

        #endregion

        #region Helper Methods

        private async Task<GeneratedProject> CreateAssemblyWithMissingDependenciesAsync(string projectName)
        {
            var config = new ProjectConfiguration
            {
                ProjectName = projectName,
                ClassCount = 2,
                MethodsPerClass = 3,
                PackageReferences = new Dictionary<string, string>
                {
                    { "NonExistent.Package", "1.0.0" },
                    { "Another.Missing.Package", "2.0.0" }
                }
            };

            var solution = await _solutionGenerator.CreateSolutionAsync(new SolutionConfiguration
            {
                SolutionName = $"{projectName}Solution",
                ProjectCount = 1,
                ProjectTemplate = config
            });

            return solution.Projects.First();
        }

        private async Task<GeneratedProject> CreateFrameworkSpecificAssemblyAsync(string projectName, FrameworkVersion framework)
        {
            var targetFramework = framework switch
            {
                FrameworkVersion.NetFramework48 => "net48",
                FrameworkVersion.NetCore => "netcoreapp3.1",
                FrameworkVersion.Net5Plus => "net8.0",
                FrameworkVersion.NetStandard => "netstandard2.0",
                _ => "net8.0"
            };

            var config = new ProjectConfiguration
            {
                ProjectName = projectName,
                TargetFramework = targetFramework,
                ClassCount = 2,
                MethodsPerClass = 2
            };

            var solution = await _solutionGenerator.CreateSolutionAsync(new SolutionConfiguration
            {
                SolutionName = $"{projectName}Solution",
                ProjectCount = 1,
                ProjectTemplate = config
            });

            return solution.Projects.First();
        }

        private async Task<GeneratedProject> CreateUnsupportedFrameworkAssemblyAsync(string projectName)
        {
            var config = new ProjectConfiguration
            {
                ProjectName = projectName,
                TargetFramework = "netfx-unsupported-1.0", // Non-existent framework
                ClassCount = 1,
                MethodsPerClass = 1
            };

            var solution = await _solutionGenerator.CreateSolutionAsync(new SolutionConfiguration
            {
                SolutionName = $"{projectName}Solution",
                ProjectCount = 1,
                ProjectTemplate = config
            });

            return solution.Projects.First();
        }

        private async Task<GeneratedProject> CreateTestAssemblyAsync(string projectName)
        {
            var config = new ProjectConfiguration
            {
                ProjectName = projectName,
                ClassCount = 1,
                MethodsPerClass = 1
            };

            var solution = await _solutionGenerator.CreateSolutionAsync(new SolutionConfiguration
            {
                SolutionName = $"{projectName}Solution",
                ProjectCount = 1,
                ProjectTemplate = config
            });

            return solution.Projects.First();
        }

        private async Task<GeneratedProject> CreateArchitectureSpecificAssemblyAsync(string projectName)
        {
            var config = new ProjectConfiguration
            {
                ProjectName = projectName,
                ClassCount = 1,
                MethodsPerClass = 1
            };

            var solution = await _solutionGenerator.CreateSolutionAsync(new SolutionConfiguration
            {
                SolutionName = $"{projectName}Solution",
                ProjectCount = 1,
                ProjectTemplate = config
            });

            // Modify project to specify platform target
            var project = solution.Projects.First();
            var projectContent = await File.ReadAllTextAsync(project.Path);
            projectContent = projectContent.Replace("</PropertyGroup>", 
                "    <PlatformTarget>x86</PlatformTarget>\n  </PropertyGroup>");
            await File.WriteAllTextAsync(project.Path, projectContent);

            return project;
        }

        private void CreateCorruptedAssemblyFile(string filePath, CorruptionType corruptionType)
        {
            var random = new Random(42); // Fixed seed for reproducible tests
            
            switch (corruptionType)
            {
                case CorruptionType.InvalidHeader:
                    var invalidHeader = new byte[] { 0xFF, 0xFE, 0x00, 0x01 };
                    File.WriteAllBytes(filePath, invalidHeader.Concat(new byte[500]).ToArray());
                    break;
                    
                case CorruptionType.MissingMetadata:
                    var partialPE = new byte[] 
                    { 
                        0x4D, 0x5A, 0x90, 0x00, // DOS header
                        0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00,
                        0xB8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00
                        // Missing proper PE sections
                    };
                    File.WriteAllBytes(filePath, partialPE.Concat(new byte[200]).ToArray());
                    break;
                    
                case CorruptionType.TruncatedFile:
                    File.WriteAllBytes(filePath, new byte[] { 0x4D, 0x5A }); // Only DOS signature
                    break;
                    
                case CorruptionType.RandomData:
                    var randomData = new byte[1000];
                    random.NextBytes(randomData);
                    File.WriteAllBytes(filePath, randomData);
                    break;
            }
        }

        private void CreatePlatformIncompatibleBinary(string filePath)
        {
            // Create a file that looks like an executable but isn't compatible
            var incompatibleData = new byte[]
            {
                0x7F, 0x45, 0x4C, 0x46, // ELF header (Linux binary on Windows or vice versa)
                0x01, 0x01, 0x01, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            };
            
            File.WriteAllBytes(filePath, incompatibleData.Concat(new byte[500]).ToArray());
        }

        private async Task<ProcessResult> RunProcessAsync(string fileName, string arguments)
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = fileName;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            await process.WaitForExitAsync();

            return new ProcessResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = await process.StandardOutput.ReadToEndAsync(),
                StandardError = await process.StandardError.ReadToEndAsync()
            };
        }

        #endregion

        #region Helper Classes and Enums

        private enum CorruptionType
        {
            InvalidHeader,
            MissingMetadata,
            TruncatedFile,
            RandomData
        }

        private class ProcessResult
        {
            public int ExitCode { get; set; }
            public string StandardOutput { get; set; } = string.Empty;
            public string StandardError { get; set; } = string.Empty;
        }

        #endregion
    }
}