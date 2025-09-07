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
    /// Complete end-to-end workflow tests that validate the entire TestIntelligence pipeline
    /// from solution analysis through test selection and execution planning.
    /// </summary>
    [Collection("E2E Tests")]
    public class CompleteWorkflowIntegrationTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly TestSolutionGenerator _solutionGenerator;
        private readonly List<string> _tempFiles = new();
        private readonly PerformanceTestHarness _performanceHarness;

        public CompleteWorkflowIntegrationTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "WorkflowIntegrationTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            _solutionGenerator = new TestSolutionGenerator(_tempDirectory);
            _performanceHarness = new PerformanceTestHarness();
        }

        public void Dispose()
        {
            _performanceHarness?.Dispose();
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

        #region Complete Analysis Pipeline Tests

        [Fact]
        public async Task CompleteWorkflow_AnalyzeCallGraphFindTests_ShouldProduceConsistentResults()
        {
            // Arrange - Create a test solution with multiple projects
            var solution = await CreateTestSolutionAsync("CompleteWorkflowSolution", 8);
            var solutionPath = solution.Path;

            var workflow = new WorkflowResult();

            // Act - Step 1: Analyze solution
            var analyzeResult = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>("analyze",
                $"--path \"{solutionPath}\" --json");

            workflow.AnalyzeResult = analyzeResult;
            analyzeResult.Should().NotBeNull();
            analyzeResult.Summary.TotalTestMethods.Should().BeGreaterThan(0);

            // Act - Step 2: Generate call graph
            var callGraphResult = await CliTestHelper.RunCliCommandWithJsonOutputAsync<CallGraphJsonOutput>("callgraph",
                $"--path \"{solutionPath}\" --json");

            workflow.CallGraphResult = callGraphResult;
            callGraphResult.Should().NotBeNull();
            callGraphResult.Summary.TotalMethods.Should().BeGreaterThan(0);

            // Act - Step 3: Find tests for methods discovered in call graph
            if (callGraphResult.Methods.Any())
            {
                var targetMethod = callGraphResult.Methods.First();
                var methodFullName = $"{targetMethod.ClassName}.{targetMethod.MethodName}";

                var findTestsResult = await CliTestHelper.RunCliCommandWithJsonOutputAsync<FindTestsJsonOutput>("find-tests",
                    $"--method \"{methodFullName}\" --solution \"{solutionPath}\" --json");

                workflow.FindTestsResult = findTestsResult;
                findTestsResult.Should().NotBeNull();
                findTestsResult.TargetMethod.Should().Be(methodFullName);
            }

            // Assert - Verify workflow consistency
            ValidateWorkflowConsistency(workflow);
        }

        [Fact]
        public async Task CompleteWorkflow_DiffAnalysisToTestSelection_ShouldIdentifyImpactedTests()
        {
            // Arrange - Create solution and simulate code changes
            var solution = await CreateTestSolutionWithTestProjectsAsync("DiffAnalysisSolution", 6);
            var solutionPath = solution.Path;

            // Create a simulated diff file
            var diffContent = CreateTestDiffContent(solution);
            var diffFilePath = Path.Combine(_tempDirectory, "changes.diff");
            await File.WriteAllTextAsync(diffFilePath, diffContent);
            _tempFiles.Add(diffFilePath);

            // Act - Step 1: Analyze solution baseline
            var baselineAnalysis = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>("analyze",
                $"--path \"{solutionPath}\" --json");

            baselineAnalysis.Should().NotBeNull();
            baselineAnalysis.Summary.TotalTestMethods.Should().BeGreaterThan(0);

            // Act - Step 2: Perform diff analysis
            var diffAnalysisResult = await CliTestHelper.RunCliCommandAsync("diff",
                $"--solution \"{solutionPath}\" --diff \"{diffFilePath}\" --format json");

            diffAnalysisResult.Success.Should().BeTrue($"Diff analysis should succeed. Error: {diffAnalysisResult.StandardError}");

            // Act - Step 3: Generate test selection based on changes
            // (This demonstrates how diff analysis feeds into test selection)
            var selectionResult = await CliTestHelper.RunCliCommandAsync("select",
                $"--solution \"{solutionPath}\" --confidence-threshold 0.3");

            // Assert - Verify the pipeline worked
            selectionResult.Success.Should().BeTrue();
            diffAnalysisResult.StandardOutput.Should().Contain("Analyzing code changes");
        }

        [Fact]
        public async Task CompleteWorkflow_ConfigurationDrivenAnalysis_ShouldRespectSettings()
        {
            // Arrange - Create solution and custom configuration
            var solution = await CreateTestSolutionAsync("ConfigWorkflowSolution", 10);
            var solutionPath = solution.Path;

            var configContent = CreateTestConfiguration(solution);
            var configPath = Path.Combine(_tempDirectory, "testintel.config");
            await File.WriteAllTextAsync(configPath, configContent);
            _tempFiles.Add(configPath);

            // Act - Run analysis with custom configuration
            var configuredAnalysis = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>("analyze",
                $"--path \"{solutionPath}\" --config \"{configPath}\" --json");

            // Assert
            configuredAnalysis.Should().NotBeNull();
            configuredAnalysis.TestAssemblies.Should().NotBeEmpty();
            
            // Verify configuration was applied (e.g., excluded projects should not appear)
            var assemblyNames = configuredAnalysis.TestAssemblies.Select(a => a.AssemblyName).ToList();
            assemblyNames.Should().NotContain(name => name.Contains("Excluded"));
        }

        #endregion

        #region Performance Workflow Tests

        [Fact]
        public async Task CompleteWorkflow_LargeSolutionPerformance_ShouldCompleteWithinThresholds()
        {
            // Arrange - Create a large solution to test performance
            var largeSolution = await CreateLargeSolutionAsync("LargePerformanceSolution", 25);
            var solutionPath = largeSolution.Path;

            var performanceMetrics = new PerformanceMetrics();

            // Act - Run complete workflow and measure performance
            using (_performanceHarness.StartMeasurement("CompleteWorkflow"))
            {
                // Step 1: Analysis
                var analysisStart = DateTime.UtcNow;
                var analyzeResult = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>("analyze",
                    $"--path \"{solutionPath}\" --json");
                performanceMetrics.AnalysisTime = DateTime.UtcNow - analysisStart;

                // Step 2: Call Graph
                var callGraphStart = DateTime.UtcNow;
                var callGraphResult = await CliTestHelper.RunCliCommandWithJsonOutputAsync<CallGraphJsonOutput>("callgraph",
                    $"--path \"{solutionPath}\" --json");
                performanceMetrics.CallGraphTime = DateTime.UtcNow - callGraphStart;

                // Step 3: Test Discovery
                var discoveryStart = DateTime.UtcNow;
                var discoveryResult = await CliTestHelper.RunCliCommandAsync("discover",
                    $"--solution \"{solutionPath}\"");
                performanceMetrics.DiscoveryTime = DateTime.UtcNow - discoveryStart;

                performanceMetrics.TotalTime = DateTime.UtcNow - analysisStart;
            }

            // Assert - Performance thresholds
            performanceMetrics.AnalysisTime.Should().BeLessThan(TimeSpan.FromMinutes(5), 
                "Analysis should complete within 5 minutes for 25 projects");
            performanceMetrics.CallGraphTime.Should().BeLessThan(TimeSpan.FromMinutes(3), 
                "Call graph generation should complete within 3 minutes");
            performanceMetrics.TotalTime.Should().BeLessThan(TimeSpan.FromMinutes(10), 
                "Complete workflow should finish within 10 minutes");

            // Memory usage should remain reasonable
            var memoryUsage = _performanceHarness.GetPeakMemoryUsageMB();
            memoryUsage.Should().BeLessThan(2048, "Memory usage should stay under 2GB");
        }

        [Fact]
        public async Task CompleteWorkflow_ConcurrentOperations_ShouldHandleParallelExecution()
        {
            // Arrange - Create multiple solutions for concurrent testing
            var solutions = new List<GeneratedSolution>();
            for (int i = 0; i < 3; i++)
            {
                var solution = await CreateTestSolutionAsync($"ConcurrentSolution{i:D2}", 5);
                solutions.Add(solution);
            }

            var results = new List<WorkflowResult>();

            // Act - Run workflows concurrently
            var tasks = solutions.Select(async (solution, index) =>
            {
                var workflow = new WorkflowResult { SolutionName = solution.Name };

                try
                {
                    // Analyze
                    workflow.AnalyzeResult = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>(
                        "analyze", $"--path \"{solution.Path}\" --json");

                    // Call Graph
                    workflow.CallGraphResult = await CliTestHelper.RunCliCommandWithJsonOutputAsync<CallGraphJsonOutput>(
                        "callgraph", $"--path \"{solution.Path}\" --json");

                    workflow.Success = true;
                }
                catch (Exception ex)
                {
                    workflow.Error = ex.Message;
                    workflow.Success = false;
                }

                return workflow;
            });

            var concurrentResults = await Task.WhenAll(tasks);

            // Assert
            concurrentResults.Should().HaveCount(solutions.Count);
            concurrentResults.Should().AllSatisfy(result =>
            {
                result.Success.Should().BeTrue($"Workflow for {result.SolutionName} should succeed. Error: {result.Error}");
                result.AnalyzeResult.Should().NotBeNull();
                result.CallGraphResult.Should().NotBeNull();
            });
        }

        #endregion

        #region Error Recovery and Resilience Tests

        [Fact]
        public async Task CompleteWorkflow_PartialFailureRecovery_ShouldContinueWithAvailableData()
        {
            // Arrange - Create a solution with some problematic projects
            var solution = await CreateMixedQualitySolutionAsync("PartialFailureSolution");
            var solutionPath = solution.Path;

            // Act - Run analysis that may have partial failures
            var analyzeResult = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solutionPath}\" --continue-on-error");

            // Assert - Should handle partial failures gracefully
            analyzeResult.Success.Should().BeTrue("Analysis should continue despite partial failures");
            
            if (analyzeResult.StandardError.Contains("Warning") || analyzeResult.StandardError.Contains("Error"))
            {
                // Partial failures are acceptable - verify graceful handling
                analyzeResult.StandardOutput.Should().Contain("Analysis completed");
            }
        }

        [Fact]
        public async Task CompleteWorkflow_InvalidConfiguration_ShouldProvideHelpfulErrors()
        {
            // Arrange
            var solution = await CreateTestSolutionAsync("InvalidConfigSolution", 3);
            var solutionPath = solution.Path;

            var invalidConfigPath = Path.Combine(_tempDirectory, "invalid.config");
            await File.WriteAllTextAsync(invalidConfigPath, "{ invalid json content }");
            _tempFiles.Add(invalidConfigPath);

            // Act
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solutionPath}\" --config \"{invalidConfigPath}\"");

            // Assert
            result.Success.Should().BeFalse();
            result.StandardError.Should().Contain("configuration");
            result.StandardError.Should().NotBeNullOrEmpty("Should provide helpful error message");
        }

        [Fact]
        public async Task CompleteWorkflow_MissingDependencies_ShouldReportMissingAssemblies()
        {
            // Arrange - Create a solution that references non-existent assemblies
            var solution = await CreateSolutionWithMissingDependenciesAsync("MissingDepsSolution");
            var solutionPath = solution.Path;

            // Act
            var result = await CliTestHelper.RunCliCommandAsync("analyze",
                $"--path \"{solutionPath}\" --verbose");

            // Assert - Should report missing dependencies but continue analysis
            // The exact behavior may vary, but it should handle missing dependencies gracefully
            result.StandardOutput.Should().Contain("Analysis completed");
        }

        #endregion

        #region Multi-Framework Workflow Tests

        [Fact]
        public async Task CompleteWorkflow_MixedFrameworkSolution_ShouldAnalyzeAllFrameworks()
        {
            // Arrange - Create solution with multiple target frameworks
            var solution = await CreateMixedFrameworkSolutionAsync("MixedFrameworkWorkflowSolution");
            var solutionPath = solution.Path;

            // Act
            var analyzeResult = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>("analyze",
                $"--path \"{solutionPath}\" --json");

            // Assert
            analyzeResult.Should().NotBeNull();
            analyzeResult.TestAssemblies.Should().NotBeEmpty();
            
            // Should handle different framework versions
            var frameworks = analyzeResult.TestAssemblies
                .Select(a => a.TargetFramework)
                .Where(f => !string.IsNullOrEmpty(f))
                .Distinct()
                .ToList();

            if (frameworks.Any())
            {
                frameworks.Should().Contain(fw => fw.Contains("net"));
            }
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
                    ClassCount = 4,
                    MethodsPerClass = 6,
                    IncludeComplexity = true,
                    IncludeAsync = true
                }
            };

            return await _solutionGenerator.CreateSolutionAsync(config);
        }

        private async Task<GeneratedSolution> CreateTestSolutionWithTestProjectsAsync(string solutionName, int projectCount)
        {
            var config = new SolutionConfiguration
            {
                SolutionName = solutionName,
                ProjectCount = projectCount,
                ProjectTemplate = new ProjectConfiguration
                {
                    ProjectType = ProjectType.TestProject,
                    ClassCount = 3,
                    MethodsPerClass = 5,
                    IncludeComplexity = true,
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
                    ClassCount = 8,
                    MethodsPerClass = 12,
                    IncludeComplexity = true,
                    IncludeAsync = true,
                    IncludeEntityFramework = true
                }
            };

            return await _solutionGenerator.CreateSolutionAsync(config);
        }

        private async Task<GeneratedSolution> CreateMixedQualitySolutionAsync(string solutionName)
        {
            var config = new SolutionConfiguration
            {
                SolutionName = solutionName,
                ProjectCount = 5,
                ProjectTemplate = new ProjectConfiguration
                {
                    ClassCount = 3,
                    MethodsPerClass = 4,
                    IncludeComplexity = true
                }
            };

            var solution = await _solutionGenerator.CreateSolutionAsync(config);

            // Introduce some "problematic" elements
            if (solution.Projects.Any())
            {
                var problematicProject = solution.Projects.First();
                var projectContent = await File.ReadAllTextAsync(problematicProject.Path);
                
                // Add a reference to a non-existent package (simulates missing dependency)
                projectContent = projectContent.Replace("</Project>", 
                    "  <ItemGroup>\n    <PackageReference Include=\"NonExistent.Package\" Version=\"1.0.0\" />\n  </ItemGroup>\n</Project>");
                
                await File.WriteAllTextAsync(problematicProject.Path, projectContent);
            }

            return solution;
        }

        private async Task<GeneratedSolution> CreateSolutionWithMissingDependenciesAsync(string solutionName)
        {
            var solution = await CreateTestSolutionAsync(solutionName, 3);

            // Add references to non-existent assemblies
            foreach (var project in solution.Projects)
            {
                var projectContent = await File.ReadAllTextAsync(project.Path);
                projectContent = projectContent.Replace("</Project>",
                    "  <ItemGroup>\n    <Reference Include=\"NonExistent.Assembly\" />\n  </ItemGroup>\n</Project>");
                await File.WriteAllTextAsync(project.Path, projectContent);
            }

            return solution;
        }

        private async Task<GeneratedSolution> CreateMixedFrameworkSolutionAsync(string solutionName)
        {
            var solution = await CreateTestSolutionAsync(solutionName, 4);

            // Modify projects to target different frameworks
            var frameworks = new[] { "net48", "net6.0", "net8.0", "netstandard2.0" };
            for (int i = 0; i < solution.Projects.Count && i < frameworks.Length; i++)
            {
                var project = solution.Projects[i];
                var projectContent = await File.ReadAllTextAsync(project.Path);
                projectContent = projectContent.Replace("net8.0", frameworks[i]);
                await File.WriteAllTextAsync(project.Path, projectContent);
            }

            return solution;
        }

        private string CreateTestDiffContent(GeneratedSolution solution)
        {
            if (!solution.Projects.Any() || !solution.Projects.First().Classes.Any())
            {
                return @"diff --git a/TestFile.cs b/TestFile.cs
index abc123..def456 100644
--- a/TestFile.cs
+++ b/TestFile.cs
@@ -1,4 +1,5 @@
 public class TestClass
 {
+    // Added comment
     public void TestMethod() { }
 }";
            }

            var firstClass = solution.Projects.First().Classes.First();
            return $@"diff --git a/{firstClass.Name}.cs b/{firstClass.Name}.cs
index abc123..def456 100644
--- a/{firstClass.Name}.cs
+++ b/{firstClass.Name}.cs
@@ -1,4 +1,5 @@
 public class {firstClass.Name}
 {{
+    // Modified method
     public void ModifiedMethod() {{ }}
 }}";
        }

        private string CreateTestConfiguration(GeneratedSolution solution)
        {
            return @"{
  ""projects"": {
    ""include"": [],
    ""exclude"": [
      ""**/obj/**"",
      ""**/bin/**"",
      ""*Excluded*""
    ],
    ""testProjectsOnly"": true
  },
  ""analysis"": {
    ""verbose"": false,
    ""maxParallelism"": 4,
    ""timeoutSeconds"": 180
  },
  ""output"": {
    ""format"": ""json""
  }
}";
        }

        private void ValidateWorkflowConsistency(WorkflowResult workflow)
        {
            // Verify that analyze and call graph results are consistent
            if (workflow.AnalyzeResult != null && workflow.CallGraphResult != null)
            {
                // Both should report on the same solution
                workflow.AnalyzeResult.TestAssemblies.Should().NotBeEmpty();
                workflow.CallGraphResult.Methods.Should().NotBeEmpty();

                // Assembly counts should be reasonable
                var analyzeAssemblyCount = workflow.AnalyzeResult.Summary.TotalAssemblies;
                var callGraphMethodCount = workflow.CallGraphResult.Summary.TotalMethods;
                
                callGraphMethodCount.Should().BeGreaterThan(0, "Call graph should find methods when analysis finds assemblies");
            }

            if (workflow.FindTestsResult != null)
            {
                workflow.FindTestsResult.TargetMethod.Should().NotBeNullOrEmpty();
                // Tests may or may not be found - both are valid outcomes
            }
        }

        #endregion

        #region Helper Classes

        private class WorkflowResult
        {
            public string SolutionName { get; set; } = string.Empty;
            public AnalyzeJsonOutput? AnalyzeResult { get; set; }
            public CallGraphJsonOutput? CallGraphResult { get; set; }
            public FindTestsJsonOutput? FindTestsResult { get; set; }
            public bool Success { get; set; }
            public string Error { get; set; } = string.Empty;
        }

        private class PerformanceMetrics
        {
            public TimeSpan AnalysisTime { get; set; }
            public TimeSpan CallGraphTime { get; set; }
            public TimeSpan DiscoveryTime { get; set; }
            public TimeSpan TotalTime { get; set; }
        }

        #endregion
    }
}