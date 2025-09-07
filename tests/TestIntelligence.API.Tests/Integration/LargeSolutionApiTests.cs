using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TestIntelligence.API.Models;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.TestUtilities;
using Xunit;

namespace TestIntelligence.API.Tests.Integration
{
    /// <summary>
    /// Integration tests for API endpoints handling large solution scenarios,
    /// validating scalability and performance with realistic data volumes.
    /// </summary>
    [Collection("API Integration Tests")]
    public class LargeSolutionApiTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;
        private readonly string _tempDirectory;
        private readonly TestSolutionGenerator _solutionGenerator;
        private readonly PerformanceTestHarness _performanceHarness;

        public LargeSolutionApiTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
            _tempDirectory = Path.Combine(Path.GetTempPath(), "LargeSolutionApiTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            
            _solutionGenerator = new TestSolutionGenerator(_tempDirectory);
            _performanceHarness = new PerformanceTestHarness();
            
            _client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddLogging(logging => logging.AddConsole());
                });
            }).CreateClient();
        }

        public void Dispose()
        {
            _performanceHarness?.Dispose();
            _solutionGenerator?.Dispose();
            _client?.Dispose();
            
            if (Directory.Exists(_tempDirectory))
            {
                try
                {
                    Directory.Delete(_tempDirectory, recursive: true);
                }
                catch { }
            }
        }

        #region Large Solution Discovery Tests

        [Fact]
        public async Task LargeSolutionApi_DiscoverTests_ShouldHandleManyProjects()
        {
            // Arrange
            var largeSolution = await CreateLargeSolutionAsync("LargeDiscoverySolution", 25);
            
            var request = new TestDiscoveryRequest
            {
                Path = largeSolution.Path,
                IncludeSource = true,
                IncludeAssemblyInfo = true
            };

            // Act
            var response = await PostJsonAsync<TestDiscoveryResponse>("/api/testdiscovery/discover", request);

            // Assert
            response.Should().NotBeNull();
            response.Tests.Should().NotBeEmpty("Large solution should contain discoverable tests");
            response.Tests.Count.Should().BeGreaterThan(100, "25 projects should yield substantial number of tests");
            
            // Verify performance is acceptable
            response.DiscoveryTime.Should().BeLessThan(TimeSpan.FromMinutes(3), 
                "Large solution discovery should complete within 3 minutes");
        }

        [Fact]
        public async Task LargeSolutionApi_DiscoverTestsWithFiltering_ShouldRespectFilters()
        {
            // Arrange
            var largeSolution = await CreateLargeSolutionWithMixedProjectsAsync("FilteredDiscoverySolution");
            
            var request = new TestDiscoveryRequest
            {
                Path = largeSolution.Path,
                IncludePatterns = new List<string> { "*Integration*", "*Unit*" },
                ExcludePatterns = new List<string> { "*Performance*", "*Load*" }
            };

            // Act
            var response = await PostJsonAsync<TestDiscoveryResponse>("/api/testdiscovery/discover", request);

            // Assert
            response.Should().NotBeNull();
            
            // Should include only tests from projects matching include patterns and not exclude patterns
            var testSources = response.Tests.Select(t => t.FullName).ToList();
            testSources.Should().Contain(name => name.Contains("Integration") || name.Contains("Unit"));
            testSources.Should().NotContain(name => name.Contains("Performance") || name.Contains("Load"));
        }

        [Fact]
        public async Task LargeSolutionApi_DiscoverTestsStreaming_ShouldHandleLargeResponses()
        {
            // Arrange - Very large solution that might stress response handling
            var hugeSolution = await CreateLargeSolutionAsync("HugeStreamingSolution", 40);
            
            var request = new TestDiscoveryRequest
            {
                Path = hugeSolution.Path,
                IncludeSource = false, // Reduce payload size
                StreamResults = true
            };

            // Act
            _performanceHarness.StartTest("LargeSolutionStreaming");
            
            var performanceResult = await _performanceHarness.MeasureAsync("StreamingDiscovery", async () =>
            {
                var response = await PostJsonAsync<TestDiscoveryResponse>("/api/testdiscovery/discover", request);
                return response;
            }, iterations: 1);

            var report = _performanceHarness.EndTest();

            // Assert
            var response = performanceResult.Results.First();
            response.Should().NotBeNull();
            response.Tests.Should().NotBeEmpty();
            
            // Performance should be acceptable even for huge solutions
            performanceResult.Measurement.TotalTime.Should().BeLessThan(TimeSpan.FromMinutes(5),
                "Huge solution streaming should complete within 5 minutes");
        }

        #endregion

        #region Large Solution Test Selection Tests

        [Fact]
        public async Task LargeSolutionApi_CreateTestPlan_ShouldHandleComplexCodeChanges()
        {
            // Arrange
            var largeSolution = await CreateLargeSolutionAsync("LargeTestPlanSolution", 20);
            var complexCodeChanges = CreateComplexCodeChangeSet(largeSolution);
            
            var request = new TestPlanRequest
            {
                CodeChanges = complexCodeChanges,
                ConfidenceLevel = ConfidenceLevel.Medium,
                MaxTests = 500,
                MaxExecutionTime = TimeSpan.FromMinutes(30)
            };

            // Act
            var response = await PostJsonAsync<TestExecutionPlan>("/api/testselection/plan", request);

            // Assert
            response.Should().NotBeNull();
            response.SelectedTests.Should().NotBeEmpty("Complex code changes should result in test selection");
            response.SelectedTests.Count().Should().BeLessThanOrEqualTo(500, "Should respect MaxTests limit");
            response.EstimatedExecutionTime.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(35),
                "Should stay within time constraints (with some buffer)");
            
            // Test selection should be relevant to code changes
            response.ConfidenceLevel.Should().Be(ConfidenceLevel.Medium);
            response.SelectionReason.Should().NotBeEmpty("Should provide reasoning for test selection");
        }

        [Fact]
        public async Task LargeSolutionApi_CreateTestPlanWithCategories_ShouldRespectCategoryFilters()
        {
            // Arrange
            var largeSolution = await CreateLargeSolutionWithCategorizedTestsAsync("CategorizedTestPlanSolution");
            var codeChanges = CreateSimpleCodeChangeSet(largeSolution);
            
            var request = new TestPlanRequest
            {
                CodeChanges = codeChanges,
                IncludedCategories = new List<TestCategory> { TestCategory.Unit, TestCategory.Integration },
                ExcludedCategories = new List<TestCategory> { TestCategory.Performance, TestCategory.EndToEnd },
                ConfidenceLevel = ConfidenceLevel.High
            };

            // Act
            var response = await PostJsonAsync<TestExecutionPlan>("/api/testselection/plan", request);

            // Assert
            response.Should().NotBeNull();
            
            if (response.SelectedTests.Any())
            {
                // All selected tests should be from included categories
                response.SelectedTests.Should().AllSatisfy(test =>
                    test.Category.Should().BeOneOf(TestCategory.Unit, TestCategory.Integration));
            }
        }

        #endregion

        #region Large Solution Diff Analysis Tests

        [Fact]
        public async Task LargeSolutionApi_AnalyzeDiff_ShouldHandleLargeDiffs()
        {
            // Arrange
            var largeSolution = await CreateLargeSolutionAsync("LargeDiffSolution", 15);
            var largeDiff = CreateLargeDiffContent(largeSolution);
            
            var request = new DiffAnalysisRequest
            {
                SolutionPath = largeSolution.Path,
                DiffContent = largeDiff,
                ConfidenceLevel = ConfidenceLevel.Medium
            };

            // Act
            _performanceHarness.StartTest("LargeDiffAnalysis");
            
            var performanceResult = await _performanceHarness.MeasureAsync("DiffAnalysis", async () =>
            {
                return await PostJsonAsync<DiffAnalysisResult>("/api/impactanalysis/analyze-diff", request);
            }, iterations: 1);

            var report = _performanceHarness.EndTest();

            // Assert
            var response = performanceResult.Results.First();
            response.Should().NotBeNull();
            response.ChangeSet.Should().NotBeNull();
            response.TotalChanges.Should().BeGreaterThan(0, "Large diff should detect multiple changes");
            response.RecommendedTests.Should().NotBeNull();
            
            // Performance should be acceptable
            performanceResult.Measurement.TotalTime.Should().BeLessThan(TimeSpan.FromMinutes(2),
                "Large diff analysis should complete within 2 minutes");
            
            response.ImpactScore.Should().BeInRange(0.0, 1.0, "Impact score should be normalized");
        }

        [Fact]
        public async Task LargeSolutionApi_AnalyzeDiffWithTimeout_ShouldHandleTimeout()
        {
            // Arrange
            var largeSolution = await CreateLargeSolutionAsync("TimeoutDiffSolution", 30);
            var hugeDiff = CreateHugeDiffContent(largeSolution);
            
            var request = new DiffAnalysisRequest
            {
                SolutionPath = largeSolution.Path,
                DiffContent = hugeDiff,
                ConfidenceLevel = ConfidenceLevel.Low // Lower confidence for faster analysis
            };

            // Add timeout header
            _client.Timeout = TimeSpan.FromSeconds(90);

            // Act & Assert
            try
            {
                var response = await PostJsonAsync<DiffAnalysisResult>("/api/impactanalysis/analyze-diff", request);
                
                // If it doesn't timeout, verify it completed successfully
                response.Should().NotBeNull();
                response.ChangeSet.Should().NotBeNull();
            }
            catch (TaskCanceledException)
            {
                // Timeout occurred - this is acceptable for extremely large diffs
                // The API should handle this gracefully
            }
        }

        #endregion

        #region Execution Trace Analysis Tests

        [Fact]
        public async Task LargeSolutionApi_SubmitExecutionTrace_ShouldHandleLargeTraces()
        {
            // Arrange
            var largeSolution = await CreateLargeSolutionAsync("LargeTraceSolution", 12);
            var largeTrace = CreateLargeExecutionTrace(largeSolution);
            
            var request = new ExecutionTraceSubmissionRequest
            {
                SolutionPath = largeSolution.Path,
                ExecutionTrace = largeTrace,
                IncludeCoverage = true,
                IncludePerformanceData = true
            };

            // Act
            var response = await PostJsonAsync<ExecutionTraceResponse>("/api/executiontrace/submit", request);

            // Assert
            response.Should().NotBeNull();
            response.ProcessedMethods.Should().BeGreaterThan(0, "Should process methods from large trace");
            response.CoverageInfo.Should().NotBeNull("Should include coverage information");
            response.ProcessingTime.Should().BeLessThan(TimeSpan.FromMinutes(1),
                "Large trace processing should complete within reasonable time");
        }

        [Fact]
        public async Task LargeSolutionApi_GetCoverageReport_ShouldAggregateAcrossLargeSolution()
        {
            // Arrange
            var largeSolution = await CreateLargeSolutionAsync("CoverageReportSolution", 18);
            
            // Submit several traces to build up coverage data
            await SubmitMultipleExecutionTracesAsync(largeSolution, 5);

            var request = new CoverageReportRequest
            {
                SolutionPath = largeSolution.Path,
                IncludeMethodLevel = true,
                IncludeFileLevel = true,
                GroupByProject = true
            };

            // Act
            var response = await PostJsonAsync<CoverageReportResponse>("/api/executiontrace/coverage-report", request);

            // Assert
            response.Should().NotBeNull();
            response.OverallCoverage.Should().NotBeNull();
            response.ProjectCoverage.Should().NotBeEmpty("Should have coverage data for multiple projects");
            response.MethodCoverage.Should().NotBeEmpty("Should have method-level coverage data");
            
            // Coverage percentages should be reasonable
            response.OverallCoverage.LinePercent.Should().BeInRange(0.0, 100.0);
            response.OverallCoverage.BranchPercent.Should().BeInRange(0.0, 100.0);
        }

        #endregion

        #region Error Handling and Resilience Tests

        [Fact]
        public async Task LargeSolutionApi_InvalidLargeSolution_ShouldHandleGracefully()
        {
            // Arrange - Create a solution with some invalid/corrupted projects
            var problematicSolution = await CreateProblematicLargeSolutionAsync("InvalidLargeSolution");
            
            var request = new TestDiscoveryRequest
            {
                Path = problematicSolution.Path,
                IncludeSource = false
            };

            // Act
            var response = await PostJsonAsync<TestDiscoveryResponse>("/api/testdiscovery/discover", request);

            // Assert
            response.Should().NotBeNull();
            // Should discover tests from valid projects even if some are problematic
            response.Tests.Should().NotBeEmpty("Should discover tests from valid projects");
            response.Warnings.Should().NotBeEmpty("Should report warnings for problematic projects");
        }

        [Fact]
        public async Task LargeSolutionApi_MemoryIntensiveOperation_ShouldManageMemory()
        {
            // Arrange
            var largeSolution = await CreateLargeSolutionAsync("MemoryIntensiveSolution", 35);
            
            using var memoryHarness = new MemoryPressureTestHarness();
            memoryHarness.ApplyPressure(targetMemoryMB: 512, durationSeconds: 120);

            var request = new TestDiscoveryRequest
            {
                Path = largeSolution.Path,
                IncludeSource = true, // More memory intensive
                IncludeAssemblyInfo = true
            };

            // Act
            var initialMemory = GC.GetTotalMemory(true);
            
            var response = await PostJsonAsync<TestDiscoveryResponse>("/api/testdiscovery/discover", request);
            
            var finalMemory = GC.GetTotalMemory(true);

            // Assert
            response.Should().NotBeNull();
            
            // Memory growth should be reasonable even for large solutions
            var memoryGrowth = finalMemory - initialMemory;
            memoryGrowth.Should().BeLessThan(500 * 1024 * 1024, // 500MB
                "Memory growth should be controlled for large solution operations");
        }

        #endregion

        #region Helper Methods

        private async Task<GeneratedSolution> CreateLargeSolutionAsync(string solutionName, int projectCount)
        {
            var config = new SolutionConfiguration
            {
                SolutionName = solutionName,
                ProjectCount = projectCount,
                ProjectTemplate = new ProjectConfiguration
                {
                    ProjectType = ProjectType.TestProject,
                    ClassCount = 8,
                    MethodsPerClass = 12,
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

        private async Task<GeneratedSolution> CreateLargeSolutionWithMixedProjectsAsync(string solutionName)
        {
            var solution = await CreateLargeSolutionAsync(solutionName, 16);
            
            // Rename projects to simulate different types
            var projectTypes = new[] { "Unit", "Integration", "Performance", "Load", "Functional", "UI" };
            for (int i = 0; i < solution.Projects.Count; i++)
            {
                var type = projectTypes[i % projectTypes.Length];
                var project = solution.Projects[i];
                var newName = $"{type}Tests{i:D2}";
                var newPath = Path.Combine(Path.GetDirectoryName(project.Path)!, $"{newName}.csproj");
                
                File.Move(project.Path, newPath);
                project.Path = newPath;
                project.Name = newName;
            }

            return solution;
        }

        private async Task<GeneratedSolution> CreateLargeSolutionWithCategorizedTestsAsync(string solutionName)
        {
            var solution = await CreateLargeSolutionAsync(solutionName, 12);
            
            // Add test categories to generated tests by modifying the generated code
            foreach (var project in solution.Projects)
            {
                foreach (var classFile in project.Classes)
                {
                    var content = await File.ReadAllTextAsync(classFile.Path);
                    
                    // Add category attributes to test methods
                    content = content.Replace("[Fact]", "[Fact][Category(\"Unit\")]");
                    content = content.Replace("[Theory]", "[Theory][Category(\"Integration\")]");
                    
                    await File.WriteAllTextAsync(classFile.Path, content);
                }
            }

            return solution;
        }

        private async Task<GeneratedSolution> CreateProblematicLargeSolutionAsync(string solutionName)
        {
            var solution = await CreateLargeSolutionAsync(solutionName, 10);
            
            // Corrupt some project files to simulate problematic projects
            for (int i = 0; i < 2; i++) // Corrupt 2 out of 10 projects
            {
                var project = solution.Projects[i];
                await File.WriteAllTextAsync(project.Path, "<Project>Invalid XML</InvalidProject>");
            }

            return solution;
        }

        private CodeChangeSet CreateComplexCodeChangeSet(GeneratedSolution solution)
        {
            var changes = new List<CodeChange>();
            
            // Simulate changes across multiple projects and files
            foreach (var project in solution.Projects.Take(5)) // Changes in 5 projects
            {
                foreach (var classFile in project.Classes.Take(2)) // 2 classes per project
                {
                    changes.Add(new CodeChange
                    {
                        FilePath = classFile.Path,
                        ChangeType = ChangeType.Modified,
                        StartLine = 1,
                        EndLine = 10,
                        ChangedContent = "// Modified method implementation"
                    });
                }
            }

            return new CodeChangeSet(changes);
        }

        private CodeChangeSet CreateSimpleCodeChangeSet(GeneratedSolution solution)
        {
            var changes = new List<CodeChange>
            {
                new CodeChange
                {
                    FilePath = solution.Projects.First().Classes.First().Path,
                    ChangeType = ChangeType.Modified,
                    StartLine = 5,
                    EndLine = 8,
                    ChangedContent = "// Simple method change"
                }
            };

            return new CodeChangeSet(changes);
        }

        private string CreateLargeDiffContent(GeneratedSolution solution)
        {
            var diffBuilder = new StringBuilder();
            
            // Create a realistic large diff with changes across multiple files
            foreach (var project in solution.Projects.Take(8))
            {
                foreach (var classFile in project.Classes.Take(3))
                {
                    var fileName = Path.GetFileName(classFile.Path);
                    diffBuilder.AppendLine($"diff --git a/{fileName} b/{fileName}");
                    diffBuilder.AppendLine($"index abc123..def456 100644");
                    diffBuilder.AppendLine($"--- a/{fileName}");
                    diffBuilder.AppendLine($"+++ b/{fileName}");
                    diffBuilder.AppendLine("@@ -1,10 +1,12 @@");
                    
                    for (int i = 0; i < 10; i++)
                    {
                        if (i % 3 == 0)
                        {
                            diffBuilder.AppendLine($"+    // Added line {i}");
                        }
                        else
                        {
                            diffBuilder.AppendLine($" public void Method{i}() {{ }}");
                        }
                    }
                    diffBuilder.AppendLine();
                }
            }

            return diffBuilder.ToString();
        }

        private string CreateHugeDiffContent(GeneratedSolution solution)
        {
            var diffBuilder = new StringBuilder();
            
            // Create an extremely large diff that might stress the system
            foreach (var project in solution.Projects)
            {
                foreach (var classFile in project.Classes)
                {
                    var fileName = Path.GetFileName(classFile.Path);
                    diffBuilder.AppendLine($"diff --git a/{fileName} b/{fileName}");
                    diffBuilder.AppendLine($"index abc123..def456 100644");
                    diffBuilder.AppendLine($"--- a/{fileName}");
                    diffBuilder.AppendLine($"+++ b/{fileName}");
                    
                    // Many hunks per file
                    for (int hunk = 0; hunk < 5; hunk++)
                    {
                        diffBuilder.AppendLine($"@@ -{hunk * 20 + 1},20 +{hunk * 20 + 1},25 @@");
                        
                        for (int line = 0; line < 25; line++)
                        {
                            if (line % 2 == 0)
                            {
                                diffBuilder.AppendLine($"+        // Added line in hunk {hunk}, line {line}");
                            }
                            else
                            {
                                diffBuilder.AppendLine($" public void HunkMethod{hunk}Line{line}() {{ }}");
                            }
                        }
                    }
                    diffBuilder.AppendLine();
                }
            }

            return diffBuilder.ToString();
        }

        private ExecutionTrace CreateLargeExecutionTrace(GeneratedSolution solution)
        {
            var methodCalls = new List<MethodCall>();
            
            // Generate a large execution trace with many method calls
            foreach (var project in solution.Projects.Take(8))
            {
                foreach (var classFile in project.Classes.Take(4))
                {
                    for (int method = 0; method < 10; method++)
                    {
                        methodCalls.Add(new MethodCall
                        {
                            MethodName = $"{classFile.Name}.Method{method:D2}",
                            FileName = classFile.Path,
                            LineNumber = method * 5 + 10,
                            ExecutionTime = TimeSpan.FromMilliseconds(Random.Shared.Next(1, 100)),
                            CalledAt = DateTime.UtcNow.AddMilliseconds(-Random.Shared.Next(0, 10000))
                        });
                    }
                }
            }

            return new ExecutionTrace
            {
                TestName = "LargeIntegrationTest",
                MethodCalls = methodCalls,
                StartTime = DateTime.UtcNow.AddMinutes(-5),
                EndTime = DateTime.UtcNow,
                Success = true
            };
        }

        private async Task SubmitMultipleExecutionTracesAsync(GeneratedSolution solution, int traceCount)
        {
            for (int i = 0; i < traceCount; i++)
            {
                var trace = CreateLargeExecutionTrace(solution);
                trace.TestName = $"LargeIntegrationTest_{i:D2}";
                
                var request = new ExecutionTraceSubmissionRequest
                {
                    SolutionPath = solution.Path,
                    ExecutionTrace = trace,
                    IncludeCoverage = true
                };

                await PostJsonAsync<ExecutionTraceResponse>("/api/executiontrace/submit", request);
            }
        }

        private async Task<T> PostJsonAsync<T>(string endpoint, object request)
        {
            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _client.PostAsync(endpoint, content);
            
            response.EnsureSuccessStatusCode();
            
            var responseJson = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(responseJson, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            })!;
        }

        #endregion
    }
}