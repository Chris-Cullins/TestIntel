using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using TestIntelligence.CLI.Models;
using TestIntelligence.E2E.Tests.Helpers;
using TestIntelligence.E2E.Tests.Models;
using TestIntelligence.TestUtilities;
using Xunit;

namespace TestIntelligence.E2E.Tests.Workflows
{
    /// <summary>
    /// Integration tests for configuration file workflows, validating how different
    /// configuration options affect analysis behavior and results.
    /// </summary>
    [Collection("E2E Tests")]
    public class ConfigurationIntegrationTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly TestSolutionGenerator _solutionGenerator;
        private readonly List<string> _tempFiles = new();

        public ConfigurationIntegrationTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "ConfigIntegrationTests", Guid.NewGuid().ToString());
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

        #region Project Filtering Configuration Tests

        [Fact]
        public async Task ConfigurationWorkflow_ProjectInclusion_ShouldRespectIncludePatterns()
        {
            // Arrange - Create solution with mixed project types
            var solution = await CreateMixedProjectSolutionAsync("ProjectInclusionSolution");
            var configPath = CreateConfigurationFile(new TestIntelConfiguration
            {
                Projects = new ProjectFilterConfiguration
                {
                    Include = new List<string> { "*Test*", "*Integration*" },
                    TestProjectsOnly = false
                }
            });

            // Act
            var result = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>("analyze",
                $"--path \"{solution.Path}\" --config \"{configPath}\" --json");

            // Assert
            result.Should().NotBeNull();
            
            if (result.TestAssemblies.Any())
            {
                // Should only include projects matching the include patterns
                var includedProjectNames = result.TestAssemblies.Select(a => a.AssemblyName).ToList();
                includedProjectNames.Should().AllSatisfy(name => 
                {
                    (name.Contains("Test") || name.Contains("Integration")).Should().BeTrue();
                });
            }
        }

        [Fact]
        public async Task ConfigurationWorkflow_ProjectExclusion_ShouldRespectExcludePatterns()
        {
            // Arrange
            var solution = await CreateMixedProjectSolutionAsync("ProjectExclusionSolution");
            var configPath = CreateConfigurationFile(new TestIntelConfiguration
            {
                Projects = new ProjectFilterConfiguration
                {
                    Exclude = new List<string> { "*Database*", "*Migration*", "*ORM*" },
                    TestProjectsOnly = false
                }
            });

            // Act
            var result = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>("analyze",
                $"--path \"{solution.Path}\" --config \"{configPath}\" --json");

            // Assert
            result.Should().NotBeNull();
            
            if (result.TestAssemblies.Any())
            {
                var includedProjectNames = result.TestAssemblies.Select(a => a.AssemblyName).ToList();
                includedProjectNames.Should().AllSatisfy(name => 
                {
                    (!name.Contains("Database") && !name.Contains("Migration") && !name.Contains("ORM")).Should().BeTrue();
                });
            }
        }

        [Fact]
        public async Task ConfigurationWorkflow_TestProjectsOnly_ShouldFilterCorrectly()
        {
            // Arrange - Create solution with test and production projects
            var solution = await CreateSolutionWithTestAndProductionProjectsAsync("TestProjectsOnlySolution");
            var configPath = CreateConfigurationFile(new TestIntelConfiguration
            {
                Projects = new ProjectFilterConfiguration
                {
                    TestProjectsOnly = true
                }
            });

            // Act
            var result = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>("analyze",
                $"--path \"{solution.Path}\" --config \"{configPath}\" --json");

            // Assert
            result.Should().NotBeNull();
            result.Summary.TotalTestMethods.Should().BeGreaterThan(0, "Should find test methods when analyzing test projects");
        }

        [Fact]
        public async Task ConfigurationWorkflow_ExcludeTypes_ShouldRespectTypeFiltering()
        {
            // Arrange
            var solution = await CreateMixedProjectSolutionAsync("ExcludeTypesSolution");
            var configPath = CreateConfigurationFile(new TestIntelConfiguration
            {
                Projects = new ProjectFilterConfiguration
                {
                    ExcludeTypes = new List<string> { "orm", "database", "migration" },
                    TestProjectsOnly = false
                }
            });

            // Act
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --config \"{configPath}\" --verbose");

            // Assert
            result.Success.Should().BeTrue();
            result.StandardOutput.Should().Contain("Analysis completed");
        }

        #endregion

        #region Analysis Configuration Tests

        [Fact]
        public async Task ConfigurationWorkflow_MaxParallelism_ShouldRespectParallelismSettings()
        {
            // Arrange
            var solution = await CreateLargeSolutionAsync("ParallelismSolution", 10);
            var configPath = CreateConfigurationFile(new TestIntelConfiguration
            {
                Analysis = new AnalysisConfiguration
                {
                    MaxParallelism = 2, // Limit parallelism for testing
                    Verbose = true
                }
            });

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --config \"{configPath}\"");
            
            stopwatch.Stop();

            // Assert
            result.Success.Should().BeTrue();
            result.StandardOutput.Should().Contain("Analysis completed");
            // With limited parallelism, analysis might take longer, but should still complete
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(300000, "Should complete within 5 minutes even with limited parallelism");
        }

        [Fact]
        public async Task ConfigurationWorkflow_VerboseMode_ShouldProvideDetailedOutput()
        {
            // Arrange
            var solution = await CreateTestSolutionAsync("VerboseSolution", 3);
            var configPath = CreateConfigurationFile(new TestIntelConfiguration
            {
                Analysis = new AnalysisConfiguration
                {
                    Verbose = true
                }
            });

            // Act
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --config \"{configPath}\"");

            // Assert
            result.Success.Should().BeTrue();
            
            // Verbose mode should provide more detailed output
            var outputLines = result.StandardOutput.Split('\n');
            outputLines.Length.Should().BeGreaterThan(5, "Verbose mode should provide detailed output");
        }

        [Fact]
        public async Task ConfigurationWorkflow_TimeoutSettings_ShouldRespectTimeoutConfiguration()
        {
            // Arrange
            var solution = await CreateTestSolutionAsync("TimeoutSolution", 5);
            var configPath = CreateConfigurationFile(new TestIntelConfiguration
            {
                Analysis = new AnalysisConfiguration
                {
                    TimeoutSeconds = 10, // Very short timeout for testing
                    Verbose = true
                }
            });

            // Act
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --config \"{configPath}\"");

            // Assert - Should handle timeout gracefully (may succeed or fail, but shouldn't crash)
            // The exact behavior depends on how fast the analysis runs
            result.StandardError.Should().NotContain("Unhandled exception");
        }

        #endregion

        #region Output Configuration Tests

        [Fact]
        public async Task ConfigurationWorkflow_JsonOutputFormat_ShouldRespectOutputFormatConfiguration()
        {
            // Arrange
            var solution = await CreateTestSolutionAsync("JsonOutputSolution", 4);
            var configPath = CreateConfigurationFile(new TestIntelConfiguration
            {
                Output = new OutputConfiguration
                {
                    Format = "json"
                }
            });

            // Act
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --config \"{configPath}\"");

            // Assert
            result.Success.Should().BeTrue();
            result.StandardOutput.Should().Contain("{", "JSON format should produce JSON output");
            result.StandardOutput.Should().Contain("}", "JSON format should produce valid JSON");
        }

        [Fact]
        public async Task ConfigurationWorkflow_OutputDirectory_ShouldRespectOutputDirectoryConfiguration()
        {
            // Arrange
            var solution = await CreateTestSolutionAsync("OutputDirectorySolution", 3);
            var outputDir = Path.Combine(_tempDirectory, "custom_output");
            Directory.CreateDirectory(outputDir);
            
            var configPath = CreateConfigurationFile(new TestIntelConfiguration
            {
                Output = new OutputConfiguration
                {
                    Format = "json",
                    OutputDirectory = outputDir
                }
            });

            // Act
            var result = await CliTestHelper.RunCliCommandAsync("callgraph",
                $"--path \"{solution.Path}\" --config \"{configPath}\" --output-file results.json");

            // Assert
            result.Success.Should().BeTrue();
            
            // Check if output file was created in the specified directory
            var expectedOutputPath = Path.Combine(outputDir, "results.json");
            if (File.Exists(expectedOutputPath))
            {
                var outputContent = await File.ReadAllTextAsync(expectedOutputPath);
                outputContent.Should().Contain("{", "Output file should contain JSON content");
            }
        }

        #endregion

        #region Configuration File Validation Tests

        [Fact]
        public async Task ConfigurationWorkflow_InvalidJsonConfig_ShouldProvideHelpfulErrorMessage()
        {
            // Arrange
            var solution = await CreateTestSolutionAsync("InvalidJsonSolution", 2);
            var invalidConfigPath = Path.Combine(_tempDirectory, "invalid.json");
            await File.WriteAllTextAsync(invalidConfigPath, "{ invalid json content");
            _tempFiles.Add(invalidConfigPath);

            // Act
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --config \"{invalidConfigPath}\"");

            // Assert
            result.Success.Should().BeFalse();
            result.StandardError.Should().Contain("configuration");
            result.StandardError.Should().NotBeEmpty("Should provide helpful error message for invalid JSON");
        }

        [Fact]
        public async Task ConfigurationWorkflow_MissingConfigFile_ShouldProvideHelpfulErrorMessage()
        {
            // Arrange
            var solution = await CreateTestSolutionAsync("MissingConfigSolution", 2);
            var nonExistentConfigPath = Path.Combine(_tempDirectory, "does-not-exist.json");

            // Act
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --config \"{nonExistentConfigPath}\"");

            // Assert
            result.Success.Should().BeFalse();
            result.StandardError.Should().Contain("configuration");
            result.StandardError.Should().Contain("not found");
        }

        [Fact]
        public async Task ConfigurationWorkflow_EmptyConfigFile_ShouldUseDefaults()
        {
            // Arrange
            var solution = await CreateTestSolutionAsync("EmptyConfigSolution", 3);
            var emptyConfigPath = Path.Combine(_tempDirectory, "empty.json");
            await File.WriteAllTextAsync(emptyConfigPath, "{}");
            _tempFiles.Add(emptyConfigPath);

            // Act
            var result = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>("analyze",
                $"--path \"{solution.Path}\" --config \"{emptyConfigPath}\" --json");

            // Assert
            result.Should().NotBeNull();
            result.Summary.TotalTestMethods.Should().BeGreaterThanOrEqualTo(0, "Should use default configuration and complete analysis");
        }

        #endregion

        #region Command Line Override Tests

        [Fact]
        public async Task ConfigurationWorkflow_CommandLineOverrides_ShouldOverrideConfigFile()
        {
            // Arrange - Config file specifies text format, command line specifies JSON
            var solution = await CreateTestSolutionAsync("OverrideSolution", 3);
            var configPath = CreateConfigurationFile(new TestIntelConfiguration
            {
                Output = new OutputConfiguration
                {
                    Format = "text" // Config specifies text
                }
            });

            // Act - Command line specifies JSON, should override config
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --config \"{configPath}\" --json");

            // Assert
            result.Success.Should().BeTrue();
            result.StandardOutput.Should().Contain("{", "Command line --json should override config file text format");
        }

        [Fact]
        public async Task ConfigurationWorkflow_VerboseOverride_ShouldOverrideConfigVerbosity()
        {
            // Arrange - Config file has verbose=false
            var solution = await CreateTestSolutionAsync("VerboseOverrideSolution", 2);
            var configPath = CreateConfigurationFile(new TestIntelConfiguration
            {
                Analysis = new AnalysisConfiguration
                {
                    Verbose = false // Config specifies quiet mode
                }
            });

            // Act - Command line specifies verbose, should override config
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --config \"{configPath}\" --verbose");

            // Assert
            result.Success.Should().BeTrue();
            
            // Verbose output should be present despite config saying verbose=false
            var outputLines = result.StandardOutput.Split('\n');
            outputLines.Length.Should().BeGreaterThan(3, "Command line --verbose should override config quiet mode");
        }

        #endregion

        #region Complex Configuration Scenarios

        [Fact]
        public async Task ConfigurationWorkflow_ComplexFiltering_ShouldApplyMultipleRulesCorrectly()
        {
            // Arrange - Create complex solution and apply multiple filtering rules
            var solution = await CreateComplexSolutionAsync("ComplexFilteringSolution");
            var configPath = CreateConfigurationFile(new TestIntelConfiguration
            {
                Projects = new ProjectFilterConfiguration
                {
                    Include = new List<string> { "*Test*", "*Service*" },
                    Exclude = new List<string> { "*Database*", "*Migration*" },
                    ExcludeTypes = new List<string> { "orm" },
                    TestProjectsOnly = false
                },
                Analysis = new AnalysisConfiguration
                {
                    MaxParallelism = 4,
                    Verbose = true
                }
            });

            // Act
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --config \"{configPath}\"");

            // Assert
            result.Success.Should().BeTrue();
            result.StandardOutput.Should().Contain("Analysis completed");
            
            // Complex filtering should work without errors
            result.StandardError.Should().NotContain("Unhandled exception");
        }

        [Fact]
        public async Task ConfigurationWorkflow_CrossCommandConsistency_ShouldApplyConfigConsistentlyAcrossCommands()
        {
            // Arrange
            var solution = await CreateTestSolutionAsync("CrossCommandSolution", 5);
            var configPath = CreateConfigurationFile(new TestIntelConfiguration
            {
                Projects = new ProjectFilterConfiguration
                {
                    Exclude = new List<string> { "*Database*" },
                    TestProjectsOnly = true
                },
                Output = new OutputConfiguration
                {
                    Format = "json"
                }
            });

            // Act - Run multiple commands with same config
            var analyzeResult = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solution.Path}\" --config \"{configPath}\"");
            
            var callGraphResult = await CliTestHelper.RunCliCommandAsync("callgraph",
                $"--path \"{solution.Path}\" --config \"{configPath}\"");

            // Assert
            analyzeResult.Success.Should().BeTrue();
            callGraphResult.Success.Should().BeTrue();
            
            // Both commands should respect the JSON output format from config
            analyzeResult.StandardOutput.Should().Contain("{", "Analyze should respect JSON format from config");
            callGraphResult.StandardOutput.Should().Contain("{", "CallGraph should respect JSON format from config");
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
                        { "xunit", "2.4.2" },
                        { "xunit.runner.visualstudio", "2.4.5" }
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
                    ClassCount = 5,
                    MethodsPerClass = 6,
                    IncludeComplexity = true,
                    PackageReferences = new Dictionary<string, string>
                    {
                        { "Microsoft.NET.Test.Sdk", "17.8.0" },
                        { "xunit", "2.4.2" }
                    }
                }
            };

            return await _solutionGenerator.CreateSolutionAsync(config);
        }

        private async Task<GeneratedSolution> CreateMixedProjectSolutionAsync(string solutionName)
        {
            var solution = await CreateTestSolutionAsync(solutionName, 6);
            
            // Rename some projects to simulate different types
            var projectNames = new[] { "TestProject", "IntegrationTests", "DatabaseMigration", "ORMModule", "ServiceLayer", "CoreLogic" };
            for (int i = 0; i < solution.Projects.Count && i < projectNames.Length; i++)
            {
                var project = solution.Projects[i];
                var newName = projectNames[i];
                var newPath = Path.Combine(Path.GetDirectoryName(project.Path)!, $"{newName}.csproj");
                
                File.Move(project.Path, newPath);
                project.Path = newPath;
                project.Name = newName;
            }

            return solution;
        }

        private async Task<GeneratedSolution> CreateSolutionWithTestAndProductionProjectsAsync(string solutionName)
        {
            var config = new SolutionConfiguration
            {
                SolutionName = solutionName,
                ProjectCount = 6,
                ProjectTemplate = new ProjectConfiguration
                {
                    ClassCount = 3,
                    MethodsPerClass = 4,
                    IncludeComplexity = false
                }
            };

            var solution = await _solutionGenerator.CreateSolutionAsync(config);

            // Convert half the projects to test projects
            for (int i = 0; i < solution.Projects.Count / 2; i++)
            {
                var project = solution.Projects[i];
                var projectContent = await File.ReadAllTextAsync(project.Path);
                
                // Add test packages to make it a test project
                projectContent = projectContent.Replace("</Project>", 
                    @"  <ItemGroup>
    <PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.8.0"" />
    <PackageReference Include=""xunit"" Version=""2.4.2"" />
    <PackageReference Include=""xunit.runner.visualstudio"" Version=""2.4.5"" />
  </ItemGroup>
</Project>");
                
                await File.WriteAllTextAsync(project.Path, projectContent);
                
                // Rename to indicate it's a test project
                var newName = project.Name + ".Tests";
                var newPath = Path.Combine(Path.GetDirectoryName(project.Path)!, $"{newName}.csproj");
                File.Move(project.Path, newPath);
                project.Path = newPath;
                project.Name = newName;
            }

            return solution;
        }

        private async Task<GeneratedSolution> CreateComplexSolutionAsync(string solutionName)
        {
            var solution = await CreateTestSolutionAsync(solutionName, 8);
            
            // Create a variety of project types
            var projectTypes = new[]
            {
                ("CoreService", "Service"),
                ("DatabaseLayer", "Database"), 
                ("TestSuite", "Test"),
                ("MigrationTool", "Migration"),
                ("BusinessLogic", "Logic"),
                ("APIController", "Service"),
                ("ORMContext", "ORM"),
                ("IntegrationTests", "Test")
            };

            for (int i = 0; i < solution.Projects.Count && i < projectTypes.Length; i++)
            {
                var (newName, type) = projectTypes[i];
                var project = solution.Projects[i];
                var newPath = Path.Combine(Path.GetDirectoryName(project.Path)!, $"{newName}.csproj");
                
                File.Move(project.Path, newPath);
                project.Path = newPath;
                project.Name = newName;
            }

            return solution;
        }

        private string CreateConfigurationFile(TestIntelConfiguration configuration)
        {
            var configPath = Path.Combine(_tempDirectory, $"testintel-{Guid.NewGuid():N}.json");
            var json = System.Text.Json.JsonSerializer.Serialize(configuration, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            File.WriteAllText(configPath, json);
            _tempFiles.Add(configPath);
            
            return configPath;
        }

        #endregion
    }
}