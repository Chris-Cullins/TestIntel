using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.ImpactAnalyzer.Models;
using TestIntelligence.SelectionEngine.Interfaces;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.SelectionEngine.Algorithms;

namespace TestIntelligence.SelectionEngine.Engine
{
    /// <summary>
    /// Main implementation of intelligent test selection engine.
    /// </summary>
    public class TestSelectionEngine : ITestSelectionEngine
    {
        private readonly ILogger<TestSelectionEngine> _logger;
        private readonly List<ITestScoringAlgorithm> _scoringAlgorithms;
        private readonly ITestCategorizer? _testCategorizer;
        private readonly IImpactAnalyzer? _impactAnalyzer;

        // In-memory storage for demonstration - in production this would be a database
        private readonly Dictionary<string, TestInfo> _testRepository;
        private readonly List<TestExecutionResult> _executionHistory;

        public TestSelectionEngine(
            ILogger<TestSelectionEngine> logger,
            IEnumerable<ITestScoringAlgorithm>? scoringAlgorithms = null,
            ITestCategorizer? testCategorizer = null,
            IImpactAnalyzer? impactAnalyzer = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _testCategorizer = testCategorizer;
            _impactAnalyzer = impactAnalyzer;
            _testRepository = new Dictionary<string, TestInfo>();
            _executionHistory = new List<TestExecutionResult>();

            // Initialize default scoring algorithms if none provided
            _scoringAlgorithms = new List<ITestScoringAlgorithm>(scoringAlgorithms ?? new List<ITestScoringAlgorithm>
            {
                new ImpactBasedScoringAlgorithm(_logger as ILogger<ImpactBasedScoringAlgorithm> ?? 
                    new NullLogger<ImpactBasedScoringAlgorithm>()),
                new ExecutionTimeScoringAlgorithm(_logger as ILogger<ExecutionTimeScoringAlgorithm> ?? 
                    new NullLogger<ExecutionTimeScoringAlgorithm>()),
                new HistoricalScoringAlgorithm(_logger as ILogger<HistoricalScoringAlgorithm> ?? 
                    new NullLogger<HistoricalScoringAlgorithm>())
            });
        }

        public async Task<TestExecutionPlan> GetOptimalTestPlanAsync(
            CodeChangeSet changes, 
            ConfidenceLevel confidenceLevel, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Creating optimal test plan for {ChangeCount} changes with {ConfidenceLevel} confidence", 
                changes.Changes.Count, confidenceLevel);

            var options = new TestSelectionOptions();
            return await GetTestPlanInternalAsync(changes, confidenceLevel, options, cancellationToken);
        }

        public async Task<TestExecutionPlan> GetTestPlanAsync(
            ConfidenceLevel confidenceLevel, 
            TestSelectionOptions? options = null, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Creating test plan with {ConfidenceLevel} confidence", confidenceLevel);

            options ??= new TestSelectionOptions();
            return await GetTestPlanInternalAsync(null, confidenceLevel, options, cancellationToken);
        }

        public async Task<IReadOnlyList<TestInfo>> ScoreTestsAsync(
            IEnumerable<TestInfo> candidateTests, 
            CodeChangeSet? changes = null, 
            CancellationToken cancellationToken = default)
        {
            var tests = candidateTests.ToList();
            _logger.LogInformation("Scoring {TestCount} candidate tests", tests.Count);

            var context = new TestScoringContext(ConfidenceLevel.Medium, changes);
            
            foreach (var test in tests)
            {
                test.SelectionScore = await CalculateCombinedScore(test, context, cancellationToken);
            }

            return tests.OrderByDescending(t => t.SelectionScore).ToList();
        }

        public async Task UpdateTestExecutionHistoryAsync(
            IEnumerable<TestExecutionResult> results, 
            CancellationToken cancellationToken = default)
        {
            var resultsList = results.ToList();
            _logger.LogInformation("Updating execution history for {ResultCount} test results", resultsList.Count);

            foreach (var result in resultsList)
            {
                _executionHistory.Add(result);
                
                // Update test info if it exists in repository
                // Note: This is simplified - in production we'd need better test identification
                var testInfo = _testRepository.Values.FirstOrDefault(t => 
                    t.GetDisplayName().Contains("TestMethod")); // Simplified matching
                
                if (testInfo != null)
                {
                    testInfo.ExecutionHistory.Add(result);
                    testInfo.LastExecuted = result.ExecutedAt;
                    
                    // Update average execution time
                    if (result.Passed) // Only use successful runs for timing
                    {
                        var successfulRuns = testInfo.ExecutionHistory.Where(r => r.Passed).ToList();
                        if (successfulRuns.Count > 0)
                        {
                            var avgMs = successfulRuns.Average(r => r.Duration.TotalMilliseconds);
                            testInfo.AverageExecutionTime = TimeSpan.FromMilliseconds(avgMs);
                        }
                    }
                }
            }

            await Task.CompletedTask; // Placeholder for async database operations
        }

        public async Task<IReadOnlyList<TestInfo>> GetTestHistoryAsync(
            string? testFilter = null, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Retrieving test history with filter: {Filter}", testFilter ?? "none");

            var tests = _testRepository.Values.AsEnumerable();

            if (!string.IsNullOrEmpty(testFilter))
            {
                tests = tests.Where(t => 
                    t.GetDisplayName().IndexOf(testFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.GetUniqueId().IndexOf(testFilter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return await Task.FromResult(tests.ToList());
        }

        private async Task<TestExecutionPlan> GetTestPlanInternalAsync(
            CodeChangeSet? changes,
            ConfidenceLevel confidenceLevel, 
            TestSelectionOptions options,
            CancellationToken cancellationToken)
        {
            // For now, use a mock set of tests - in production this would query the test repository
            var candidateTests = await GetCandidateTests(changes, options, cancellationToken);

            if (candidateTests.Count == 0)
            {
                _logger.LogWarning("No candidate tests found for selection");
                return new TestExecutionPlan(
                    Array.Empty<TestInfo>(), 
                    confidenceLevel, 
                    TimeSpan.Zero,
                    "No tests available");
            }

            // Score all candidate tests
            var context = new TestScoringContext(confidenceLevel, changes, options);
            var scoredTests = new List<TestInfo>();

            foreach (var test in candidateTests)
            {
                test.SelectionScore = await CalculateCombinedScore(test, context, cancellationToken);
                scoredTests.Add(test);
            }

            // Select tests based on confidence level and constraints
            var selectedTests = await SelectTestsForPlan(scoredTests, confidenceLevel, options, cancellationToken);

            // Calculate total estimated duration
            var estimatedDuration = TimeSpan.FromMilliseconds(
                selectedTests.Sum(t => t.AverageExecutionTime.TotalMilliseconds));

            var plan = new TestExecutionPlan(selectedTests, confidenceLevel, estimatedDuration, 
                $"Selected {selectedTests.Count} tests based on {confidenceLevel} confidence level");

            // Create execution batches for parallel execution
            plan.CreateExecutionBatches(options.MaxParallelism);

            _logger.LogInformation("Created test plan: {TestCount} tests, estimated duration {Duration}",
                selectedTests.Count, estimatedDuration);

            return plan;
        }

        private async Task<List<TestInfo>> GetCandidateTests(
            CodeChangeSet? changes, 
            TestSelectionOptions options,
            CancellationToken cancellationToken)
        {
            // In production, this would query a test repository/database
            // For now, return mock tests for demonstration
            var candidates = new List<TestInfo>();

            // This is a simplified mock - in production, we'd have a proper test discovery service
            await Task.CompletedTask;

            return candidates;
        }

        private async Task<double> CalculateCombinedScore(
            TestInfo testInfo, 
            TestScoringContext context,
            CancellationToken cancellationToken)
        {
            var totalWeight = 0.0;
            var weightedScore = 0.0;

            foreach (var algorithm in _scoringAlgorithms)
            {
                try
                {
                    var score = await algorithm.CalculateScoreAsync(testInfo, context, cancellationToken);
                    weightedScore += score * algorithm.Weight;
                    totalWeight += algorithm.Weight;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error calculating score using {Algorithm} for test {Test}", 
                        algorithm.Name, testInfo.GetDisplayName());
                }
            }

            return totalWeight > 0 ? weightedScore / totalWeight : 0.0;
        }

        private async Task<List<TestInfo>> SelectTestsForPlan(
            List<TestInfo> scoredTests,
            ConfidenceLevel confidenceLevel,
            TestSelectionOptions options,
            CancellationToken cancellationToken)
        {
            // Sort by score descending
            var sortedTests = scoredTests.OrderByDescending(t => t.SelectionScore).ToList();

            // Apply confidence level limits
            var maxTests = Math.Min(
                options.MaxTestCount ?? confidenceLevel.GetMaxTestCount(),
                confidenceLevel.GetMaxTestCount());

            var maxDuration = options.MaxExecutionTime ?? confidenceLevel.GetEstimatedDuration();
            var minScore = options.MinSelectionScore ?? GetMinScoreForConfidenceLevel(confidenceLevel);

            var selectedTests = new List<TestInfo>();
            var currentDuration = TimeSpan.Zero;

            foreach (var test in sortedTests)
            {
                // Check constraints
                if (selectedTests.Count >= maxTests)
                    break;

                if (test.SelectionScore < minScore)
                    break;

                if (currentDuration + test.AverageExecutionTime > maxDuration)
                    continue; // Skip this test, try others

                // Apply category filters
                if (options.ExcludedCategories?.Contains(test.Category) == true)
                    continue;

                if (options.IncludedCategories?.Count > 0 && 
                    !options.IncludedCategories.Contains(test.Category))
                    continue;

                // Apply tag filters
                if (options.ExcludedTags?.Count > 0 && 
                    test.Tags.Any(tag => options.ExcludedTags.Contains(tag)))
                    continue;

                if (options.RequiredTags?.Count > 0 && 
                    !options.RequiredTags.Any(tag => test.Tags.Contains(tag)))
                    continue;

                // Apply flaky test filter
                if (!options.IncludeFlakyTests && test.IsFlaky())
                    continue;

                selectedTests.Add(test);
                currentDuration += test.AverageExecutionTime;

                // Update selection timestamp
                test.LastSelected = DateTimeOffset.UtcNow;
            }

            await Task.CompletedTask; // Placeholder for async operations

            _logger.LogDebug("Selected {SelectedCount} tests out of {CandidateCount} candidates", 
                selectedTests.Count, scoredTests.Count);

            return selectedTests;
        }

        private static double GetMinScoreForConfidenceLevel(ConfidenceLevel confidenceLevel)
        {
            return confidenceLevel switch
            {
                ConfidenceLevel.Fast => 0.6,
                ConfidenceLevel.Medium => 0.4,
                ConfidenceLevel.High => 0.2,
                ConfidenceLevel.Full => 0.0,
                _ => 0.3
            };
        }
    }

    /// <summary>
    /// Null logger implementation for cases where specific logger isn't available.
    /// </summary>
    internal class NullLogger<T> : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) => new NullDisposable();
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }

        private class NullDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}