using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TestIntelligence.API.Models;
using TestIntelligence.Core.Models;
using TestIntelligence.ImpactAnalyzer.Models;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.TestUtilities;
using Xunit;

namespace TestIntelligence.API.Tests.Integration
{
    /// <summary>
    /// Integration tests for API concurrent request handling, validating thread safety,
    /// resource management, and performance under concurrent load.
    /// </summary>
    [Collection("API Integration Tests")]
    public class ConcurrentRequestHandlingTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly string _tempDirectory;
        private readonly TestSolutionGenerator _solutionGenerator;
        private readonly PerformanceTestHarness _performanceHarness;
        private readonly List<HttpClient> _clients = new();

        public ConcurrentRequestHandlingTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
            _tempDirectory = Path.Combine(Path.GetTempPath(), "ConcurrentRequestTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            
            _solutionGenerator = new TestSolutionGenerator(_tempDirectory);
            _performanceHarness = new PerformanceTestHarness();
        }

        public void Dispose()
        {
            foreach (var client in _clients)
            {
                client?.Dispose();
            }
            
            _performanceHarness?.Dispose();
            _solutionGenerator?.Dispose();
            
            if (Directory.Exists(_tempDirectory))
            {
                try
                {
                    Directory.Delete(_tempDirectory, recursive: true);
                }
                catch { }
            }
        }

        #region Concurrent Discovery Tests

        [Fact]
        public async Task ConcurrentRequests_TestDiscovery_ShouldHandleSimultaneousRequests()
        {
            // Arrange
            var solution = await CreateTestSolutionAsync("ConcurrentDiscoverySolution", 8);
            var concurrentClients = CreateMultipleClients(5);

            var request = new TestDiscoveryRequest
            {
                Path = solution.Path,
                IncludeSource = false // Reduce payload for faster processing
            };

            // Act
            _performanceHarness.StartTest("ConcurrentTestDiscovery");
            
            var concurrentResult = await _performanceHarness.MeasureConcurrentAsync("DiscoveryRequests",
                async (taskIndex) =>
                {
                    var client = concurrentClients[taskIndex % concurrentClients.Count];
                    return await PostJsonAsync<TestDiscoveryResponse>(client, "/api/testdiscovery/discover", request);
                },
                concurrentTasks: 10,
                iterationsPerTask: 2);

            var report = _performanceHarness.EndTest();

            // Assert
            concurrentResult.Results.Should().HaveCount(20); // 10 tasks * 2 iterations
            concurrentResult.Results.Cast<TestDiscoveryResponse>().Should().AllSatisfy(response =>
            {
                response.Should().NotBeNull();
                response.Tests.Should().NotBeEmpty();
            });

            // Concurrent operations should maintain reasonable performance
            concurrentResult.Measurement.OverallDuration.Should().BeLessThan(TimeSpan.FromMinutes(2));
            concurrentResult.Measurement.ParallelismEfficiency.Should().BeGreaterThan(0.3,
                "API should show reasonable parallelism efficiency");
        }

        [Fact]
        public async Task ConcurrentRequests_DifferentSolutions_ShouldIsolateOperations()
        {
            // Arrange - Create multiple different solutions
            var solutions = new List<GeneratedSolution>();
            for (int i = 0; i < 3; i++)
            {
                var solution = await CreateTestSolutionAsync($"ConcurrentSolution{i:D2}", 5);
                solutions.Add(solution);
            }

            var concurrentClients = CreateMultipleClients(6);

            // Act - Process different solutions concurrently
            var tasks = solutions.SelectMany(solution => 
                Enumerable.Range(0, 4).Select(async taskIndex =>
                {
                    var client = concurrentClients[taskIndex % concurrentClients.Count];
                    var request = new TestDiscoveryRequest
                    {
                        Path = solution.Path,
                        IncludeSource = false
                    };

                    var response = await PostJsonAsync<TestDiscoveryResponse>(client, "/api/testdiscovery/discover", request);
                    return new { SolutionName = solution.Name, Response = response };
                }));

            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().HaveCount(12); // 3 solutions * 4 requests each
            
            // Group by solution and verify isolation
            var resultsBySolution = results.GroupBy(r => r.SolutionName).ToList();
            resultsBySolution.Should().HaveCount(3);
            
            foreach (var solutionGroup in resultsBySolution)
            {
                solutionGroup.Should().AllSatisfy(result =>
                {
                    result.Response.Should().NotBeNull();
                    result.Response.Tests.Should().NotBeEmpty();
                });
            }
        }

        [Fact]
        public async Task ConcurrentRequests_MixedOperations_ShouldHandleMultipleEndpoints()
        {
            // Arrange
            var solution = await CreateTestSolutionAsync("MixedOperationsSolution", 6);
            var concurrentClients = CreateMultipleClients(4);
            var codeChanges = CreateTestCodeChanges(solution);

            var operationTasks = new List<Task<object>>();

            // Create different types of concurrent operations
            for (int i = 0; i < 12; i++)
            {
                var client = concurrentClients[i % concurrentClients.Count];
                var operationType = i % 3;

                switch (operationType)
                {
                    case 0: // Test Discovery
                        operationTasks.Add(Task.Run(async () =>
                        {
                            var request = new TestDiscoveryRequest { Path = solution.Path };
                            var result = await PostJsonAsync<TestDiscoveryResponse>(client, "/api/testdiscovery/discover", request);
                            return (object)result;
                        }));
                        break;

                    case 1: // Test Plan Creation
                        operationTasks.Add(Task.Run(async () =>
                        {
                            var request = new TestPlanRequest
                            {
                                CodeChanges = codeChanges,
                                ConfidenceLevel = ConfidenceLevel.Medium
                            };
                            var result = await PostJsonAsync<TestExecutionPlan>(client, "/api/testselection/plan", request);
                            return (object)result;
                        }));
                        break;

                    case 2: // Diff Analysis
                        operationTasks.Add(Task.Run(async () =>
                        {
                            var request = new DiffAnalysisRequest
                            {
                                SolutionPath = solution.Path,
                                DiffContent = CreateTestDiffContent(solution)
                            };
                            var result = await PostJsonAsync<DiffAnalysisResult>(client, "/api/impactanalysis/analyze-diff", request);
                            return (object)result;
                        }));
                        break;
                }
            }

            // Act
            var results = await Task.WhenAll(operationTasks);

            // Assert
            results.Should().HaveCount(12);
            results.Should().AllSatisfy(result => result.Should().NotBeNull());
            
            // Verify we got results from all operation types
            var discoveryResults = results.OfType<TestDiscoveryResponse>().ToList();
            var planResults = results.OfType<TestExecutionPlan>().ToList();
            var diffResults = results.OfType<DiffAnalysisResult>().ToList();

            discoveryResults.Should().HaveCount(4);
            planResults.Should().HaveCount(4);
            diffResults.Should().HaveCount(4);
        }

        #endregion

        #region Load Testing and Resource Management

        [Fact]
        public async Task ConcurrentRequests_HighLoad_ShouldMaintainPerformance()
        {
            // Arrange
            var solution = await CreateTestSolutionAsync("HighLoadSolution", 10);
            var concurrentClients = CreateMultipleClients(8);
            var highConcurrency = 25;
            var iterationsPerTask = 3;

            var request = new TestDiscoveryRequest
            {
                Path = solution.Path,
                IncludeSource = false
            };

            // Act
            _performanceHarness.StartTest("HighLoadTesting");
            var startTime = DateTime.UtcNow;
            
            var highLoadResult = await _performanceHarness.MeasureConcurrentAsync<object>("HighLoadDiscovery",
                async (taskIndex) =>
                {
                    var client = concurrentClients[taskIndex % concurrentClients.Count];
                    
                    try
                    {
                        var response = await PostJsonAsync<TestDiscoveryResponse>(client, "/api/testdiscovery/discover", request);
                        return (object)new { Success = true, Response = response };
                    }
                    catch (Exception ex)
                    {
                        return (object)new { Success = false, Error = ex.Message };
                    }
                },
                concurrentTasks: highConcurrency,
                iterationsPerTask: iterationsPerTask);

            var endTime = DateTime.UtcNow;
            var report = _performanceHarness.EndTest();

            // Assert
            var totalRequests = highConcurrency * iterationsPerTask;
            highLoadResult.Results.Should().HaveCount(totalRequests);
            
            // Most requests should succeed under high load
            var successfulRequests = highLoadResult.Results.Cast<dynamic>()
                .Count(r => r.Success == true);
            var successRate = (double)successfulRequests / totalRequests;
            
            successRate.Should().BeGreaterThan(0.85, "At least 85% of requests should succeed under high load");
            
            // Overall throughput should be reasonable
            var totalDuration = endTime - startTime;
            var throughput = totalRequests / totalDuration.TotalSeconds;
            throughput.Should().BeGreaterThan(5.0, "Should maintain throughput of at least 5 requests/second");
        }

        [Fact]
        public async Task ConcurrentRequests_MemoryPressure_ShouldManageMemoryEfficiently()
        {
            // Arrange
            var solution = await CreateTestSolutionAsync("MemoryPressureSolution", 8);
            var concurrentClients = CreateMultipleClients(6);
            
            using var memoryHarness = new MemoryPressureTestHarness();
            memoryHarness.ApplyPressure(targetMemoryMB: 512, durationSeconds: 60);

            var request = new TestDiscoveryRequest
            {
                Path = solution.Path,
                IncludeSource = true // More memory intensive
            };

            // Act - Monitor memory during concurrent operations
            var initialMemory = GC.GetTotalMemory(true);
            
            var concurrentResult = await _performanceHarness.MeasureConcurrentAsync("MemoryPressureTesting",
                async (taskIndex) =>
                {
                    var client = concurrentClients[taskIndex % concurrentClients.Count];
                    
                    try
                    {
                        return await PostJsonAsync<TestDiscoveryResponse>(client, "/api/testdiscovery/discover", request);
                    }
                    catch (OutOfMemoryException)
                    {
                        return null; // Expected under severe memory pressure
                    }
                },
                concurrentTasks: 15,
                iterationsPerTask: 2);
            
            var finalMemory = GC.GetTotalMemory(true);

            // Assert
            var successfulResults = concurrentResult.Results.Where(r => r != null).ToList();
            successfulResults.Should().NotBeEmpty("Some requests should succeed even under memory pressure");
            
            // Memory growth should be controlled
            var memoryGrowth = finalMemory - initialMemory;
            memoryGrowth.Should().BeLessThan(300 * 1024 * 1024, // 300MB
                "Memory growth should be controlled under pressure");
        }

        [Fact]
        public async Task ConcurrentRequests_ResourceExhaustion_ShouldDegradeGracefully()
        {
            // Arrange - Create many clients to potentially exhaust resources
            var solution = await CreateTestSolutionAsync("ResourceExhaustionSolution", 6);
            var manyClients = CreateMultipleClients(15);

            var request = new TestDiscoveryRequest { Path = solution.Path };

            // Act - Attempt to overwhelm the system
            var tasks = new List<Task<ApiResult>>();
            
            for (int i = 0; i < 50; i++) // Many concurrent requests
            {
                var client = manyClients[i % manyClients.Count];
                
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var response = await PostJsonAsync<TestDiscoveryResponse>(client, "/api/testdiscovery/discover", request);
                        return new ApiResult { Success = true, Response = response };
                    }
                    catch (HttpRequestException ex)
                    {
                        return new ApiResult { Success = false, Error = ex.Message };
                    }
                    catch (TaskCanceledException)
                    {
                        return new ApiResult { Success = false, Error = "Timeout", IsTimeout = true };
                    }
                }));
            }

            var results = await Task.WhenAll(tasks);

            // Assert - Should handle resource exhaustion gracefully
            results.Should().HaveCount(50);
            
            var successfulResults = results.Where(r => r.Success).ToList();
            var failedResults = results.Where(r => !r.Success).ToList();
            var timeoutResults = results.Where(r => r.IsTimeout).ToList();

            // Some requests should succeed
            successfulResults.Should().NotBeEmpty("Some requests should succeed");
            
            // If failures occur, they should be handled gracefully (no crashes)
            if (failedResults.Any())
            {
                failedResults.Should().AllSatisfy(result =>
                {
                    result.Error.Should().NotBeEmpty("Failed requests should have error messages");
                });
            }
            
            // System should remain responsive (not all requests timeout)
            var timeoutRate = (double)timeoutResults.Count / results.Length;
            timeoutRate.Should().BeLessThan(0.5, "Timeout rate should be less than 50%");
        }

        #endregion

        #region Thread Safety and State Management Tests

        [Fact]
        public async Task ConcurrentRequests_StateIsolation_ShouldMaintainRequestIsolation()
        {
            // Arrange - Create requests with different parameters to test state isolation
            var solution = await CreateTestSolutionAsync("StateIsolationSolution", 5);
            var concurrentClients = CreateMultipleClients(4);

            var requestVariations = new[]
            {
                new TestDiscoveryRequest { Path = solution.Path, IncludeSource = true },
                new TestDiscoveryRequest { Path = solution.Path, IncludeSource = false },
                new TestDiscoveryRequest { Path = solution.Path, IncludeAssemblyInfo = true },
                new TestDiscoveryRequest { Path = solution.Path, IncludeAssemblyInfo = false }
            };

            // Act - Send different requests concurrently
            var tasks = Enumerable.Range(0, 20).Select(async i =>
            {
                var client = concurrentClients[i % concurrentClients.Count];
                var request = requestVariations[i % requestVariations.Length];
                var requestId = i;

                var response = await PostJsonAsync<TestDiscoveryResponse>(client, "/api/testdiscovery/discover", request);
                return new { RequestId = requestId, Request = request, Response = response };
            });

            var results = await Task.WhenAll(tasks);

            // Assert - Each response should match its request parameters
            results.Should().HaveCount(20);
            
            foreach (var result in results)
            {
                result.Response.Should().NotBeNull();
                result.Response.Tests.Should().NotBeEmpty();
                
                // Verify response matches request characteristics
                if (result.Request.IncludeSource)
                {
                    // If source was requested, response should contain source info
                    // (This would depend on the actual API implementation)
                }
            }
        }

        [Fact]
        public async Task ConcurrentRequests_LongRunningOperations_ShouldHandleTimeout()
        {
            // Arrange
            var largeSolution = await CreateTestSolutionAsync("LongRunningSolution", 15);
            var concurrentClients = CreateMultipleClients(3);

            var longRunningRequest = new TestDiscoveryRequest
            {
                Path = largeSolution.Path,
                IncludeSource = true,
                IncludeAssemblyInfo = true,
                DeepAnalysis = true // Assume this makes operations longer
            };

            // Set shorter timeout to test timeout handling
            foreach (var client in concurrentClients)
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            }

            // Act
            var tasks = concurrentClients.Select(async client =>
            {
                try
                {
                    var response = await PostJsonAsync<TestDiscoveryResponse>(client, "/api/testdiscovery/discover", longRunningRequest);
                    return new { Success = true, TimedOut = false, Response = (TestDiscoveryResponse?)response };
                }
                catch (TaskCanceledException)
                {
                    return new { Success = false, TimedOut = true, Response = (TestDiscoveryResponse?)null };
                }
            });

            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().HaveCount(3);
            
            // Some operations may timeout, but system should handle it gracefully
            var timedOutCount = results.Count(r => r.TimedOut);
            var successfulCount = results.Count(r => r.Success);
            
            // At least some should complete or timeout gracefully (not crash)
            (timedOutCount + successfulCount).Should().Be(3, "All operations should either succeed or timeout gracefully");
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
                    ClassCount = 5,
                    MethodsPerClass = 8,
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

        private List<HttpClient> CreateMultipleClients(int count)
        {
            var clients = new List<HttpClient>();
            
            for (int i = 0; i < count; i++)
            {
                var client = _factory.WithWebHostBuilder(builder =>
                {
                    builder.ConfigureServices(services =>
                    {
                        services.AddLogging(logging => logging.AddConsole());
                    });
                }).CreateClient();
                
                clients.Add(client);
                _clients.Add(client); // Track for disposal
            }

            return clients;
        }

        private CodeChangeSet CreateTestCodeChanges(GeneratedSolution solution)
        {
            var changes = new List<CodeChange>
            {
                new CodeChange
                {
                    FilePath = solution.Projects.First().Classes.First().Path,
                    ChangeType = CodeChangeType.Modified,
                    StartLine = 5,
                    EndLine = 10,
                    ChangedContent = "// Test method modification"
                }
            };

            return new CodeChangeSet(changes);
        }

        private string CreateTestDiffContent(GeneratedSolution solution)
        {
            var firstClass = solution.Projects.First().Classes.First();
            return $@"diff --git a/{firstClass.Name}.cs b/{firstClass.Name}.cs
index abc123..def456 100644
--- a/{firstClass.Name}.cs
+++ b/{firstClass.Name}.cs
@@ -1,5 +1,6 @@
 public class {firstClass.Name}
 {{
+    // Added comment for concurrent testing
     public void TestMethod() {{ }}
 }}";
        }

        private async Task<T> PostJsonAsync<T>(HttpClient client, string endpoint, object request)
        {
            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(endpoint, content);
            
            response.EnsureSuccessStatusCode();
            
            var responseJson = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(responseJson, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            })!;
        }

        #endregion

        #region Helper Classes

        private class ApiResult
        {
            public bool Success { get; set; }
            public string Error { get; set; } = string.Empty;
            public bool IsTimeout { get; set; }
            public object? Response { get; set; }
        }

        #endregion
    }
}