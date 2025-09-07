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
    /// Performance regression tests that validate system performance remains within
    /// acceptable thresholds as the codebase evolves, using the PerformanceTestHarness.
    /// </summary>
    [Collection("E2E Tests")]
    public class PerformanceRegressionTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly TestSolutionGenerator _solutionGenerator;
        private readonly List<string> _tempFiles = new();
        
        // Performance thresholds - these should be adjusted based on baseline measurements
        private static readonly Dictionary<string, PerformanceThresholds> Thresholds = new()
        {
            ["small_solution"] = new() { MaxAnalysisTime = TimeSpan.FromSeconds(30), MaxMemoryMB = 512 },
            ["medium_solution"] = new() { MaxAnalysisTime = TimeSpan.FromMinutes(2), MaxMemoryMB = 1024 },
            ["large_solution"] = new() { MaxAnalysisTime = TimeSpan.FromMinutes(5), MaxMemoryMB = 2048 },
            ["concurrent_analysis"] = new() { MaxAnalysisTime = TimeSpan.FromMinutes(3), MaxMemoryMB = 1536 }
        };

        public PerformanceRegressionTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "PerformanceRegressionTests", Guid.NewGuid().ToString());
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

        #region Analysis Performance Regression Tests

        [Fact]
        public async Task PerformanceRegression_SmallSolution_ShouldMaintainBaseline()
        {
            // Arrange
            using var harness = new PerformanceTestHarness();
            harness.StartTest("SmallSolutionAnalysis");
            
            var solution = await CreateSmallSolutionAsync();
            var threshold = Thresholds["small_solution"];

            // Act - Measure analysis performance
            var result = await harness.MeasureAsync("AnalyzeCommand", async () =>
            {
                var cliResult = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>(
                    "analyze", $"--path \"{solution.Path}\" --json");
                
                cliResult.Should().NotBeNull();
                return cliResult;
            }, iterations: 3);

            var report = harness.EndTest();

            // Assert - Performance should be within thresholds
            result.Measurement.AverageTime.Should().BeLessThan(threshold.MaxAnalysisTime,
                $"Small solution analysis should complete within {threshold.MaxAnalysisTime.TotalSeconds}s");
            
            result.Measurement.MemoryUsed.Should().BeLessThan(threshold.MaxMemoryMB * 1024 * 1024,
                $"Memory usage should be under {threshold.MaxMemoryMB}MB");

            // Log performance metrics for regression tracking
            LogPerformanceBaseline("SmallSolution", result.Measurement);
        }

        [Fact]
        public async Task PerformanceRegression_MediumSolution_ShouldScaleAppropriately()
        {
            // Arrange
            using var harness = new PerformanceTestHarness();
            harness.StartTest("MediumSolutionAnalysis");
            
            var solution = await CreateMediumSolutionAsync();
            var threshold = Thresholds["medium_solution"];

            // Act - Measure performance across multiple operations
            var analyzeResult = await harness.MeasureAsync("AnalyzeCommand", async () =>
                await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>(
                    "analyze", $"--path \"{solution.Path}\" --json"), iterations: 2);

            var callgraphResult = await harness.MeasureAsync("CallGraphCommand", async () =>
                await CliTestHelper.RunCliCommandWithJsonOutputAsync<CallGraphJsonOutput>(
                    "callgraph", $"--path \"{solution.Path}\" --json"), iterations: 2);

            var report = harness.EndTest();

            // Assert - Both operations should be within thresholds
            analyzeResult.Measurement.AverageTime.Should().BeLessThan(threshold.MaxAnalysisTime);
            callgraphResult.Measurement.AverageTime.Should().BeLessThan(threshold.MaxAnalysisTime);
            
            // Combined memory usage should be reasonable
            var totalMemory = analyzeResult.Measurement.MemoryUsed + callgraphResult.Measurement.MemoryUsed;
            totalMemory.Should().BeLessThan(threshold.MaxMemoryMB * 1024 * 1024);

            LogPerformanceBaseline("MediumSolution", analyzeResult.Measurement, callgraphResult.Measurement);
        }

        [Fact]
        public async Task PerformanceRegression_LargeSolution_ShouldHandleScaleGracefully()
        {
            // Arrange
            using var harness = new PerformanceTestHarness();
            harness.StartTest("LargeSolutionAnalysis");
            
            var solution = await CreateLargeSolutionAsync();
            var threshold = Thresholds["large_solution"];

            // Act - Single iteration for large solutions due to time constraints
            var result = await harness.MeasureAsync("LargeAnalysis", async () =>
            {
                var analyzeResult = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>(
                    "analyze", $"--path \"{solution.Path}\" --json --max-parallelism 4");
                
                analyzeResult.Should().NotBeNull();
                analyzeResult.Summary.TotalTestMethods.Should().BeGreaterThan(0);
                return analyzeResult;
            }, iterations: 1, warmup: false);

            var report = harness.EndTest();

            // Assert - Should handle large solutions within acceptable time
            result.Measurement.TotalTime.Should().BeLessThan(threshold.MaxAnalysisTime,
                $"Large solution analysis should complete within {threshold.MaxAnalysisTime.TotalMinutes} minutes");
            
            LogPerformanceBaseline("LargeSolution", result.Measurement);
        }

        [Fact]
        public async Task PerformanceRegression_ConcurrentAnalysis_ShouldMaintainEfficiency()
        {
            // Arrange
            using var harness = new PerformanceTestHarness();
            harness.StartTest("ConcurrentAnalysisEfficiency");
            
            var solutions = await CreateMultipleSolutionsAsync(3);
            var threshold = Thresholds["concurrent_analysis"];

            // Act - Measure concurrent analysis performance
            var concurrentResult = await harness.MeasureConcurrentAsync("ConcurrentAnalysis", 
                async (taskIndex) =>
                {
                    var solution = solutions[taskIndex % solutions.Count];
                    var result = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>(
                        "analyze", $"--path \"{solution.Path}\" --json");
                    return result;
                }, 
                concurrentTasks: 3, 
                iterationsPerTask: 1);

            var report = harness.EndTest();

            // Assert - Concurrent execution should be efficient
            concurrentResult.Measurement.OverallDuration.Should().BeLessThan(threshold.MaxAnalysisTime);
            
            // Parallel efficiency should be reasonable (> 0.5 means good parallelization)
            concurrentResult.Measurement.ParallelismEfficiency.Should().BeGreaterThan(0.3,
                "Concurrent analysis should show reasonable parallelism efficiency");

            LogConcurrentPerformanceBaseline("ConcurrentAnalysis", concurrentResult.Measurement);
        }

        #endregion

        #region Memory Usage Regression Tests

        [Fact]
        public async Task PerformanceRegression_MemoryUsage_ShouldNotLeak()
        {
            // Arrange
            using var harness = new PerformanceTestHarness();
            using var memoryHarness = new MemoryPressureTestHarness();
            
            harness.StartTest("MemoryLeakDetection");
            var solution = await CreateMediumSolutionAsync();
            
            var initialMemory = GC.GetTotalMemory(true);

            // Act - Run multiple analysis cycles to detect leaks
            var result = await harness.MeasureAsync("RepeatedAnalysis", async () =>
            {
                var analysisResult = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>(
                    "analyze", $"--path \"{solution.Path}\" --json");
                
                // Force garbage collection between runs
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                return analysisResult;
            }, iterations: 5);

            var finalMemory = GC.GetTotalMemory(true);
            var report = harness.EndTest();

            // Assert - Memory growth should be minimal
            var memoryGrowth = finalMemory - initialMemory;
            memoryGrowth.Should().BeLessThan(100 * 1024 * 1024, // 100MB threshold
                "Memory usage should not grow significantly across multiple analysis runs");
            
            // Average memory per operation should be consistent
            var avgMemoryPerOperation = result.Measurement.MemoryUsed / result.Measurement.Iterations;
            avgMemoryPerOperation.Should().BeLessThan(50 * 1024 * 1024, // 50MB per operation
                "Memory usage per operation should be reasonable");
        }

        [Fact]
        public async Task PerformanceRegression_MemoryUnderPressure_ShouldDegradeGracefully()
        {
            // Arrange
            using var harness = new PerformanceTestHarness();
            using var memoryPressure = new MemoryPressureTestHarness();
            
            harness.StartTest("MemoryPressureHandling");
            var solution = await CreateMediumSolutionAsync();

            // Apply memory pressure
            memoryPressure.ApplyPressure(targetMemoryMB: 500, durationSeconds: 60);

            // Act - Measure performance under memory pressure
            var result = await harness.MeasureAsync("AnalysisUnderPressure", async () =>
            {
                var analysisResult = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>(
                    "analyze", $"--path \"{solution.Path}\" --json --max-parallelism 2");
                return analysisResult;
            }, iterations: 2, warmup: false);

            var report = harness.EndTest();

            // Assert - Should complete even under memory pressure
            result.Results.Should().AllSatisfy(r => r.Should().NotBeNull());
            result.Measurement.AverageTime.Should().BeLessThan(TimeSpan.FromMinutes(3),
                "Should complete within reasonable time even under memory pressure");
        }

        #endregion

        #region Throughput Regression Tests

        [Fact]
        public async Task PerformanceRegression_Throughput_ShouldMaintainProcessingRate()
        {
            // Arrange
            using var harness = new PerformanceTestHarness();
            harness.StartTest("ThroughputMeasurement");
            
            var solutions = await CreateMultipleSolutionsAsync(5);

            // Act - Measure processing throughput
            var throughputResults = new List<PerformanceMeasurement>();
            
            foreach (var (solution, index) in solutions.Select((s, i) => (s, i)))
            {
                var result = await harness.MeasureAsync($"Solution{index:D2}", async () =>
                {
                    var analysisResult = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>(
                        "analyze", $"--path \"{solution.Path}\" --json");
                    return analysisResult.Summary.TotalTestMethods;
                }, iterations: 1);
                
                throughputResults.Add(result.Measurement);
            }

            var report = harness.EndTest();

            // Assert - Throughput should be consistent across solutions
            var throughputs = throughputResults.Select(m => m.Throughput).ToArray();
            var avgThroughput = throughputs.Average();
            var throughputVariance = throughputs.Select(t => Math.Pow(t - avgThroughput, 2)).Average();
            var throughputStdDev = Math.Sqrt(throughputVariance);

            // Coefficient of variation should be reasonable (< 0.5)
            var coefficientOfVariation = throughputStdDev / avgThroughput;
            coefficientOfVariation.Should().BeLessThan(0.5, 
                "Throughput should be relatively consistent across similar solutions");

            avgThroughput.Should().BeGreaterThan(0.1, "Should maintain minimum throughput of 0.1 operations/second");
        }

        [Fact]
        public async Task PerformanceRegression_ScalingFactors_ShouldScaleLinearly()
        {
            // Arrange
            using var harness = new PerformanceTestHarness();
            harness.StartTest("ScalingFactorAnalysis");
            
            var scalingFactors = new[] { 2, 5, 10 }; // Number of projects
            var scalingResults = new List<(int Projects, TimeSpan Duration, int TestMethods)>();

            // Act - Measure performance at different scales
            foreach (var projectCount in scalingFactors)
            {
                var solution = await CreateScalableSolutionAsync(projectCount);
                
                var result = await harness.MeasureAsync($"Scale_{projectCount:D2}", async () =>
                {
                    var analysisResult = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>(
                        "analyze", $"--path \"{solution.Path}\" --json");
                    
                    return new { Duration = result.Measurement.AverageTime, TestMethods = analysisResult.Summary.TotalTestMethods };
                }, iterations: 1);

                scalingResults.Add((projectCount, result.Measurement.AverageTime, result.Results[0].TestMethods));
            }

            var report = harness.EndTest();

            // Assert - Performance should scale reasonably with solution size
            // Calculate scaling efficiency
            var baseResult = scalingResults.First();
            var largestResult = scalingResults.Last();

            var projectRatio = (double)largestResult.Projects / baseResult.Projects;
            var timeRatio = largestResult.Duration.TotalMilliseconds / baseResult.Duration.TotalMilliseconds;
            
            // Time scaling should be sub-quadratic (better than O(n²))
            var scalingEfficiency = projectRatio * projectRatio / timeRatio;
            scalingEfficiency.Should().BeGreaterThan(0.5, 
                "Performance should scale better than quadratically with solution size");
        }

        #endregion

        #region Cache Performance Regression Tests

        [Fact]
        public async Task PerformanceRegression_CacheEffectiveness_ShouldImproveRepeatPerformance()
        {
            // Arrange
            using var harness = new PerformanceTestHarness();
            harness.StartTest("CacheEffectivenessTest");
            
            var solution = await CreateMediumSolutionAsync();

            // Act - First run (cold cache)
            var coldResult = await harness.MeasureAsync("ColdCacheRun", async () =>
            {
                return await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>(
                    "analyze", $"--path \"{solution.Path}\" --json --force-rebuild-cache");
            }, iterations: 1);

            // Second run (warm cache)
            var warmResult = await harness.MeasureAsync("WarmCacheRun", async () =>
            {
                return await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>(
                    "analyze", $"--path \"{solution.Path}\" --json");
            }, iterations: 3);

            var report = harness.EndTest();

            // Assert - Cache should provide performance improvement
            var speedupRatio = coldResult.Measurement.AverageTime.TotalMilliseconds / 
                              warmResult.Measurement.AverageTime.TotalMilliseconds;
            
            speedupRatio.Should().BeGreaterThan(1.2, 
                "Cached runs should be at least 20% faster than cold cache runs");

            // Cached runs should be more consistent (lower standard deviation)
            warmResult.Measurement.StandardDeviation.Should().BeLessThan(
                coldResult.Measurement.StandardDeviation * 2,
                "Cached runs should be more consistent in timing");
        }

        #endregion

        #region Helper Methods

        private async Task<GeneratedSolution> CreateSmallSolutionAsync()
        {
            var config = new SolutionConfiguration
            {
                SolutionName = "SmallPerformanceSolution",
                ProjectCount = 3,
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

        private async Task<GeneratedSolution> CreateMediumSolutionAsync()
        {
            var config = new SolutionConfiguration
            {
                SolutionName = "MediumPerformanceSolution",
                ProjectCount = 8,
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

        private async Task<GeneratedSolution> CreateLargeSolutionAsync()
        {
            var config = new SolutionConfiguration
            {
                SolutionName = "LargePerformanceSolution",
                ProjectCount = 20,
                ProjectTemplate = new ProjectConfiguration
                {
                    ProjectType = ProjectType.TestProject,
                    ClassCount = 12,
                    MethodsPerClass = 15,
                    IncludeComplexity = true,
                    IncludeAsync = true,
                    IncludeEntityFramework = true,
                    PackageReferences = new Dictionary<string, string>
                    {
                        { "Microsoft.NET.Test.Sdk", "17.8.0" },
                        { "xunit", "2.4.2" },
                        { "FluentAssertions", "6.12.0" },
                        { "Moq", "4.20.69" }
                    }
                }
            };

            return await _solutionGenerator.CreateSolutionAsync(config);
        }

        private async Task<GeneratedSolution> CreateScalableSolutionAsync(int projectCount)
        {
            var config = new SolutionConfiguration
            {
                SolutionName = $"ScalableSolution_{projectCount:D2}",
                ProjectCount = projectCount,
                ProjectTemplate = new ProjectConfiguration
                {
                    ProjectType = ProjectType.TestProject,
                    ClassCount = 6,
                    MethodsPerClass = 10,
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

        private async Task<List<GeneratedSolution>> CreateMultipleSolutionsAsync(int count)
        {
            var solutions = new List<GeneratedSolution>();
            
            for (int i = 0; i < count; i++)
            {
                var config = new SolutionConfiguration
                {
                    SolutionName = $"ConcurrentSolution_{i:D2}",
                    ProjectCount = 4,
                    ProjectTemplate = new ProjectConfiguration
                    {
                        ProjectType = ProjectType.TestProject,
                        ClassCount = 6,
                        MethodsPerClass = 8,
                        IncludeComplexity = false,
                        PackageReferences = new Dictionary<string, string>
                        {
                            { "Microsoft.NET.Test.Sdk", "17.8.0" },
                            { "xunit", "2.4.2" }
                        }
                    }
                };

                var solution = await _solutionGenerator.CreateSolutionAsync(config);
                solutions.Add(solution);
            }

            return solutions;
        }

        private void LogPerformanceBaseline(string testName, params PerformanceMeasurement[] measurements)
        {
            // In a real implementation, this would log to a performance tracking system
            // For now, we'll create a simple log entry
            var logPath = Path.Combine(_tempDirectory, $"performance_baseline_{testName}.log");
            var logEntries = measurements.Select(m => 
                $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} | {m.OperationName} | " +
                $"Avg: {m.AverageTime.TotalMilliseconds:F2}ms | " +
                $"Min: {m.MinTime.TotalMilliseconds:F2}ms | " +
                $"Max: {m.MaxTime.TotalMilliseconds:F2}ms | " +
                $"Throughput: {m.Throughput:F2}/s | " +
                $"Memory: {m.MemoryUsed / (1024.0 * 1024.0):F2}MB");
            
            File.WriteAllLines(logPath, logEntries);
            _tempFiles.Add(logPath);
        }

        private void LogConcurrentPerformanceBaseline(string testName, ConcurrencyPerformanceMeasurement measurement)
        {
            var logPath = Path.Combine(_tempDirectory, $"concurrent_performance_baseline_{testName}.log");
            var logEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} | {measurement.OperationName} | " +
                          $"Overall: {measurement.OverallDuration.TotalMilliseconds:F2}ms | " +
                          $"AvgTask: {measurement.AverageTaskTime.TotalMilliseconds:F2}ms | " +
                          $"Throughput: {measurement.OverallThroughput:F2}/s | " +
                          $"Efficiency: {measurement.ParallelismEfficiency:F2} | " +
                          $"Tasks: {measurement.ConcurrentTasks}";
            
            File.WriteAllText(logPath, logEntry);
            _tempFiles.Add(logPath);
        }

        #endregion

        #region Helper Classes

        private class PerformanceThresholds
        {
            public TimeSpan MaxAnalysisTime { get; set; }
            public long MaxMemoryMB { get; set; }
        }

        #endregion
    }
}