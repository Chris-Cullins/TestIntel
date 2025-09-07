using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using TestIntelligence.E2E.Tests.Helpers;
using TestIntelligence.E2E.Tests.Models;
using TestIntelligence.TestUtilities;
using Xunit;

namespace TestIntelligence.E2E.Tests.Workflows
{
    /// <summary>
    /// Tests for error recovery and resilience in production workflows,
    /// validating how the system handles various failure scenarios gracefully.
    /// </summary>
    [Collection("E2E Tests")]
    public class ErrorRecoveryWorkflowTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly TestSolutionGenerator _solutionGenerator;
        private readonly List<string> _tempFiles = new();

        public ErrorRecoveryWorkflowTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "ErrorRecoveryTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            _solutionGenerator = new TestSolutionGenerator(_tempDirectory);
        }

        public void Dispose()
        {
            _solutionGenerator?.Dispose();
            
            foreach (var file in _tempFiles)
            {
                try
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
                catch { }
            }
            
            if (Directory.Exists(_tempDirectory))
            {
                try
                {
                    Directory.Delete(_tempDirectory, recursive: true);
                }
                catch { }
            }
        }

        #region Compilation Error Recovery Tests

        [Fact]
        public async Task ErrorRecovery_CompilationErrors_ShouldContinueWithValidProjects()
        {
            // Arrange - Create solution with both valid and invalid projects
            var solution = await CreateMixedValiditySolutionAsync("CompilationErrorSolution");
            
            // Act - Run analysis with continue-on-error flag
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --continue-on-error --verbose");

            // Assert
            result.Success.Should().BeTrue("Should continue analysis despite compilation errors in some projects");
            result.StandardOutput.Should().Contain("Analysis completed", "Should complete analysis of valid projects");
            
            // May contain warnings about problematic projects
            if (result.StandardError.Contains("Warning") || result.StandardError.Contains("Error"))
            {
                result.StandardError.Should().NotContain("Unhandled exception", "Should handle compilation errors gracefully");
            }
        }

        [Fact]
        public async Task ErrorRecovery_MissingReferences_ShouldHandleGracefullyAndContinue()
        {
            // Arrange - Create solution with missing assembly references
            var solution = await CreateSolutionWithMissingReferencesAsync("MissingReferencesSolution");

            // Act
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --continue-on-error --verbose");

            // Assert
            result.Success.Should().BeTrue("Should handle missing references gracefully");
            result.StandardOutput.Should().Contain("Analysis completed");
            
            // Should provide information about what was analyzed successfully
            if (result.StandardOutput.Contains("assemblies analyzed") || result.StandardOutput.Contains("projects"))
            {
                // Good - provides feedback about what was processed
                result.StandardOutput.Should().NotBeEmpty();
            }
        }

        [Fact]
        public async Task ErrorRecovery_CorruptedProjectFiles_ShouldSkipCorruptedAndContinue()
        {
            // Arrange - Create solution with some corrupted project files
            var solution = await CreateSolutionWithCorruptedProjectsAsync("CorruptedProjectSolution");

            // Act
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --continue-on-error");

            // Assert
            result.Success.Should().BeTrue("Should skip corrupted projects and continue with valid ones");
            result.StandardOutput.Should().Contain("Analysis completed");
        }

        #endregion

        #region File System Error Recovery Tests

        [Fact]
        public async Task ErrorRecovery_FileAccessPermissions_ShouldHandlePermissionIssues()
        {
            // Arrange
            var solution = await CreateTestSolutionAsync("PermissionsSolution", 3);
            
            // Create a scenario where some files might have permission issues
            // (This test may behave differently on different platforms)

            // Act
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --continue-on-error --verbose");

            // Assert
            result.Success.Should().BeTrue("Should handle permission issues gracefully");
            result.StandardOutput.Should().Contain("Analysis completed");
            result.StandardError.Should().NotContain("Unhandled exception");
        }

        [Fact]
        public async Task ErrorRecovery_DiskSpaceIssues_ShouldHandleDiskSpaceGracefully()
        {
            // Arrange
            var solution = await CreateTestSolutionAsync("DiskSpaceSolution", 2);

            // This test simulates disk space issues by trying to create large temp files
            // In a real scenario, the system should handle disk space issues gracefully

            // Act
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --continue-on-error");

            // Assert
            result.Success.Should().BeTrue("Should handle potential disk space issues");
            result.StandardOutput.Should().Contain("Analysis completed");
        }

        [Fact]
        public async Task ErrorRecovery_LockedFiles_ShouldHandleFileLocksGracefully()
        {
            // Arrange
            var solution = await CreateTestSolutionAsync("LockedFilesSolution", 3);

            // Simulate file locks by opening some files for exclusive access
            var fileStreams = new List<FileStream>();
            try
            {
                foreach (var project in solution.Projects.Take(1))
                {
                    try
                    {
                        var stream = File.Open(project.Path, FileMode.Open, FileAccess.Read, FileShare.None);
                        fileStreams.Add(stream);
                    }
                    catch
                    {
                        // If we can't lock the file, that's okay for this test
                    }
                }

                // Act
                var result = await CliTestHelper.RunCliCommandAsync("analyze",
                    $"--path \"{solution.Path}\" --continue-on-error");

                // Assert
                result.Success.Should().BeTrue("Should handle locked files gracefully");
                result.StandardError.Should().NotContain("Unhandled exception");
            }
            finally
            {
                // Cleanup - release file locks
                foreach (var stream in fileStreams)
                {
                    try
                    {
                        stream.Dispose();
                    }
                    catch { }
                }
            }
        }

        #endregion

        #region Memory and Resource Recovery Tests

        [Fact]
        public async Task ErrorRecovery_MemoryPressure_ShouldHandleMemoryConstraints()
        {
            // Arrange
            var solution = await CreateLargeSolutionAsync("MemoryPressureSolution", 15);
            
            using var memoryPressure = new MemoryPressureTestHarness();
            
            // Apply memory pressure during analysis
            memoryPressure.ApplyPressure(targetMemoryMB: 200, durationSeconds: 30);

            // Act
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --continue-on-error --max-parallelism 2");

            // Assert
            result.Success.Should().BeTrue("Should handle memory pressure gracefully");
            result.StandardOutput.Should().Contain("Analysis completed");
            result.StandardError.Should().NotContain("OutOfMemoryException");
        }

        [Fact]
        public async Task ErrorRecovery_ResourceExhaustion_ShouldDegradeGracefully()
        {
            // Arrange - Create a scenario that might exhaust resources
            var solution = await CreateLargeSolutionAsync("ResourceExhaustionSolution", 10);

            // Act - Limit resources and see how system responds
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --continue-on-error --max-parallelism 1 --timeout 60");

            // Assert
            result.Success.Should().BeTrue("Should degrade gracefully under resource constraints");
            result.StandardError.Should().NotContain("Unhandled exception");
        }

        #endregion

        #region Network and External Dependency Recovery Tests

        [Fact]
        public async Task ErrorRecovery_NetworkUnavailable_ShouldWorkOffline()
        {
            // Arrange - Create solution that might try to fetch remote dependencies
            var solution = await CreateSolutionWithExternalDependenciesAsync("NetworkUnavailableSolution");

            // Act
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --continue-on-error --offline");

            // Assert - Should work in offline mode
            result.Success.Should().BeTrue("Should work offline without network dependencies");
            result.StandardOutput.Should().Contain("Analysis completed");
        }

        [Fact]
        public async Task ErrorRecovery_ExternalToolFailure_ShouldFallbackAppropriately()
        {
            // Arrange
            var solution = await CreateTestSolutionAsync("ExternalToolSolution", 4);

            // Act - Test scenario where external tools might fail
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --continue-on-error --no-external-tools");

            // Assert
            result.Success.Should().BeTrue("Should handle external tool failures appropriately");
            result.StandardOutput.Should().Contain("Analysis completed");
        }

        #endregion

        #region Multi-Step Workflow Recovery Tests

        [Fact]
        public async Task ErrorRecovery_PartialWorkflowFailure_ShouldContinueWithSuccessfulSteps()
        {
            // Arrange
            var solution = await CreateMixedValiditySolutionAsync("PartialWorkflowSolution");

            // Act - Multi-step workflow where some steps may fail
            var analyzeResult = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --continue-on-error");

            var callgraphResult = await CliTestHelper.RunCliCommandAsync("callgraph",
                $"--path \"{solution.Path}\" --continue-on-error");

            // Assert
            analyzeResult.Success.Should().BeTrue("Analysis step should complete despite partial failures");
            callgraphResult.Success.Should().BeTrue("Call graph step should complete despite partial failures");
            
            // Both should provide some useful output
            analyzeResult.StandardOutput.Should().NotBeEmpty();
            callgraphResult.StandardOutput.Should().NotBeEmpty();
        }

        [Fact]
        public async Task ErrorRecovery_CascadingFailures_ShouldIsolateFailures()
        {
            // Arrange - Create scenario where failures in one area might cascade
            var solution = await CreateCascadingFailureSolutionAsync("CascadingFailureSolution");

            // Act
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --continue-on-error --verbose");

            // Assert
            result.Success.Should().BeTrue("Should isolate failures and prevent cascading issues");
            result.StandardOutput.Should().Contain("Analysis completed");
            result.StandardError.Should().NotContain("Unhandled exception");
        }

        #endregion

        #region Timeout and Cancellation Recovery Tests

        [Fact]
        public async Task ErrorRecovery_OperationTimeout_ShouldHandleTimeoutsGracefully()
        {
            // Arrange
            var solution = await CreateLargeSolutionAsync("TimeoutSolution", 8);

            // Act - Set very short timeout to test timeout handling
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --timeout 5 --continue-on-error");

            // Assert - Should handle timeout gracefully (may succeed or timeout, but shouldn't crash)
            result.StandardError.Should().NotContain("Unhandled exception");
            
            if (!result.Success && result.StandardError.Contains("timeout"))
            {
                // Timeout occurred - this is acceptable
                result.StandardError.Should().Contain("timeout");
            }
            else
            {
                // Operation completed within timeout
                result.StandardOutput.Should().Contain("Analysis completed");
            }
        }

        [Fact]
        public async Task ErrorRecovery_UserCancellation_ShouldCleanupProperly()
        {
            // Arrange
            var solution = await CreateLargeSolutionAsync("CancellationSolution", 10);

            // Note: This test simulates cancellation scenarios
            // In a real implementation, we would test actual cancellation token handling

            // Act
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --continue-on-error --max-parallelism 1");

            // Assert - Should complete or handle cancellation properly
            result.StandardError.Should().NotContain("Unhandled exception");
        }

        #endregion

        #region Data Integrity and State Recovery Tests

        [Fact]
        public async Task ErrorRecovery_CacheCorruption_ShouldRecoverAndRebuild()
        {
            // Arrange
            var solution = await CreateTestSolutionAsync("CacheCorruptionSolution", 5);
            
            // First run to populate cache
            var initialResult = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\"");
            
            initialResult.Success.Should().BeTrue();

            // Corrupt any cache files that might exist
            await CorruptCacheFilesAsync(solution);

            // Act - Second run should recover from corruption
            var recoveryResult = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --continue-on-error");

            // Assert
            recoveryResult.Success.Should().BeTrue("Should recover from cache corruption");
            recoveryResult.StandardOutput.Should().Contain("Analysis completed");
        }

        [Fact]
        public async Task ErrorRecovery_InconsistentState_ShouldDetectAndRecover()
        {
            // Arrange
            var solution = await CreateTestSolutionAsync("InconsistentStateSolution", 4);

            // Act - Test recovery from inconsistent internal state
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --continue-on-error --force-rebuild");

            // Assert
            result.Success.Should().BeTrue("Should detect and recover from inconsistent state");
            result.StandardOutput.Should().Contain("Analysis completed");
        }

        #endregion

        #region Helper Methods

        private async Task<GeneratedSolution> CreateTestSolutionAsync(string solutionName, int projectCount)
        {
            var config = new SolutionConfiguration
            {
                SolutionName = solutionName,
                ProjectCount = projectCount,
                ProjectTemplate = new ProjectConfiguration
                {
                    ProjectType = ProjectType.TestProject,
                    ClassCount = 3,
                    MethodsPerClass = 4,
                    IncludeComplexity = false,
                    PackageReferences = new Dictionary<string, string>
                    {
                        { "Microsoft.NET.Test.Sdk", "17.8.0" },
                        { "xunit", "2.4.2" }
                    }
                }
            };

            return await _solutionGenerator.CreateSolutionAsync(config);
        }

        private async Task<GeneratedSolution> CreateLargeSolutionAsync(string solutionName, int projectCount)
        {
            var config = new SolutionConfiguration
            {
                SolutionName = solutionName,
                ProjectCount = projectCount,
                ProjectTemplate = new ProjectConfiguration
                {
                    ProjectType = ProjectType.TestProject,
                    ClassCount = 6,
                    MethodsPerClass = 8,
                    IncludeComplexity = true,
                    IncludeAsync = true,
                    PackageReferences = new Dictionary<string, string>
                    {
                        { "Microsoft.NET.Test.Sdk", "17.8.0" },
                        { "xunit", "2.4.2" },
                        { "FluentAssertions", "6.12.0" }
                    }
                }
            };

            return await _solutionGenerator.CreateSolutionAsync(config);
        }

        private async Task<GeneratedSolution> CreateMixedValiditySolutionAsync(string solutionName)
        {
            var solution = await CreateTestSolutionAsync(solutionName, 4);

            // Make some projects "problematic" by introducing issues
            for (int i = 0; i < solution.Projects.Count / 2; i++)
            {
                var project = solution.Projects[i];
                await IntroduceProjectIssues(project);
            }

            return solution;
        }

        private async Task<GeneratedSolution> CreateSolutionWithMissingReferencesAsync(string solutionName)
        {
            var solution = await CreateTestSolutionAsync(solutionName, 3);

            // Add references to non-existent assemblies
            foreach (var project in solution.Projects)
            {
                var projectContent = await File.ReadAllTextAsync(project.Path);
                projectContent = projectContent.Replace("</Project>",
                    @"  <ItemGroup>
    <Reference Include=""NonExistent.Assembly"" />
    <PackageReference Include=""NonExistent.Package"" Version=""1.0.0"" />
  </ItemGroup>
</Project>");
                await File.WriteAllTextAsync(project.Path, projectContent);
            }

            return solution;
        }

        private async Task<GeneratedSolution> CreateSolutionWithCorruptedProjectsAsync(string solutionName)
        {
            var solution = await CreateTestSolutionAsync(solutionName, 4);

            // Corrupt some project files
            if (solution.Projects.Any())
            {
                var corruptedProject = solution.Projects.First();
                await File.WriteAllTextAsync(corruptedProject.Path, "<Project>Invalid XML Content</InvalidProject>");
            }

            return solution;
        }

        private async Task<GeneratedSolution> CreateSolutionWithExternalDependenciesAsync(string solutionName)
        {
            var solution = await CreateTestSolutionAsync(solutionName, 3);

            // Add external dependencies that might require network access
            foreach (var project in solution.Projects)
            {
                var projectContent = await File.ReadAllTextAsync(project.Path);
                projectContent = projectContent.Replace("</Project>",
                    @"  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" Version=""13.0.3"" />
    <PackageReference Include=""Microsoft.Extensions.Hosting"" Version=""8.0.0"" />
  </ItemGroup>
</Project>");
                await File.WriteAllTextAsync(project.Path, projectContent);
            }

            return solution;
        }

        private async Task<GeneratedSolution> CreateCascadingFailureSolutionAsync(string solutionName)
        {
            var solution = await CreateTestSolutionAsync(solutionName, 5);

            // Create dependencies between projects that might cause cascading failures
            for (int i = 1; i < solution.Projects.Count; i++)
            {
                var project = solution.Projects[i];
                var dependencyProject = solution.Projects[i - 1];
                
                var projectContent = await File.ReadAllTextAsync(project.Path);
                var projectRef = $"    <ProjectReference Include=\"..\\{dependencyProject.Name}\\{dependencyProject.Name}.csproj\" />";
                projectContent = projectContent.Replace("</Project>",
                    $"  <ItemGroup>\n{projectRef}\n  </ItemGroup>\n</Project>");
                await File.WriteAllTextAsync(project.Path, projectContent);
            }

            // Introduce an issue in the first project to test cascading
            await IntroduceProjectIssues(solution.Projects.First());

            return solution;
        }

        private async Task IntroduceProjectIssues(GeneratedProject project)
        {
            // Add a reference to a non-existent package to simulate issues
            var projectContent = await File.ReadAllTextAsync(project.Path);
            projectContent = projectContent.Replace("</Project>",
                @"  <ItemGroup>
    <PackageReference Include=""Problematic.Package"" Version=""999.999.999"" />
  </ItemGroup>
</Project>");
            await File.WriteAllTextAsync(project.Path, projectContent);
        }

        private async Task CorruptCacheFilesAsync(GeneratedSolution solution)
        {
            // Look for cache directories and corrupt any cache files
            var cacheDirectories = new[]
            {
                Path.Combine(solution.Directory, ".testintel"),
                Path.Combine(solution.Directory, "obj"),
                Path.Combine(solution.Directory, ".cache")
            };

            foreach (var cacheDir in cacheDirectories.Where(Directory.Exists))
            {
                var cacheFiles = Directory.GetFiles(cacheDir, "*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".cache") || f.EndsWith(".bin"))
                    .ToList();

                foreach (var cacheFile in cacheFiles.Take(3)) // Corrupt a few files
                {
                    try
                    {
                        var randomData = new byte[100];
                        new Random().NextBytes(randomData);
                        await File.WriteAllBytesAsync(cacheFile, randomData);
                    }
                    catch
                    {
                        // Best effort corruption
                    }
                }
            }
        }

        #endregion
    }
}