using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.ImpactAnalyzer.Models;

namespace TestIntelligence.SelectionEngine.Services
{
    /// <summary>
    /// Service responsible for creating test execution plans from scored tests.
    /// </summary>
    public class TestPlanService : ITestPlanService
    {
        private readonly ILogger<TestPlanService> _logger;
        private readonly ITestDiscoveryService _testDiscoveryService;
        private readonly ITestScoringService _testScoringService;

        public TestPlanService(
            ILogger<TestPlanService> logger,
            ITestDiscoveryService testDiscoveryService,
            ITestScoringService testScoringService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _testDiscoveryService = testDiscoveryService ?? throw new ArgumentNullException(nameof(testDiscoveryService));
            _testScoringService = testScoringService ?? throw new ArgumentNullException(nameof(testScoringService));
        }

        public async Task<TestExecutionPlan> CreateOptimalTestPlanAsync(
            CodeChangeSet changes, 
            ConfidenceLevel confidenceLevel, 
            TestSelectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Creating optimal test plan for {ChangeCount} changes with {ConfidenceLevel} confidence", 
                changes.Changes.Count, confidenceLevel);

            options ??= new TestSelectionOptions();
            return await CreateTestPlanInternalAsync(changes, confidenceLevel, options, cancellationToken);
        }

        public async Task<TestExecutionPlan> CreateTestPlanAsync(
            ConfidenceLevel confidenceLevel, 
            TestSelectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Creating test plan with {ConfidenceLevel} confidence", confidenceLevel);

            options ??= new TestSelectionOptions();
            return await CreateTestPlanInternalAsync(null, confidenceLevel, options, cancellationToken);
        }

        public async Task<List<TestInfo>> SelectTestsForPlanAsync(
            List<TestInfo> scoredTests,
            ConfidenceLevel confidenceLevel,
            TestSelectionOptions options,
            CancellationToken cancellationToken = default)
        {
            // Sort by score descending
            var sortedTests = scoredTests.OrderByDescending(t => t.SelectionScore).ToList();

            // Apply confidence level limits
            var maxTests = options.MaxTestCount ?? confidenceLevel.GetMaxTestCount();

            var maxDuration = options.MaxExecutionTime ?? confidenceLevel.GetEstimatedDuration();
            var minScore = options.MinSelectionScore ?? GetMinScoreForConfidenceLevel(confidenceLevel);

            var selectedTests = new List<TestInfo>();
            
            // For Fast confidence, use strategic category balance
            if (confidenceLevel == ConfidenceLevel.Fast)
            {
                selectedTests = await SelectFastConfidenceTestsAsync(sortedTests, maxTests, maxDuration, minScore, options, cancellationToken);
            }
            else
            {
                // Standard selection for other confidence levels
                selectedTests = SelectStandardTests(sortedTests, maxTests, maxDuration, minScore, options);
            }

            await Task.CompletedTask; // Placeholder for async operations

            _logger.LogDebug("Selected {SelectedCount} tests out of {CandidateCount} candidates", 
                selectedTests.Count, scoredTests.Count);

            return selectedTests;
        }

        private async Task<TestExecutionPlan> CreateTestPlanInternalAsync(
            CodeChangeSet? changes,
            ConfidenceLevel confidenceLevel, 
            TestSelectionOptions options,
            CancellationToken cancellationToken)
        {
            // Get candidate tests
            var candidateTests = await _testDiscoveryService.GetCandidateTestsAsync(changes, options, cancellationToken: cancellationToken);

            if (candidateTests.Count == 0)
            {
                _logger.LogWarning("No candidate tests found for selection");
                return new TestExecutionPlan(
                    Array.Empty<TestInfo>(), 
                    confidenceLevel, 
                    TimeSpan.Zero,
                    "No tests available");
            }

            List<TestInfo> selectedTests;
            
            // For Full confidence, select ALL tests without scoring
            if (confidenceLevel == ConfidenceLevel.Full)
            {
                _logger.LogInformation("Full confidence selected - including ALL {TestCount} tests", candidateTests.Count);
                selectedTests = candidateTests;
                
                // Apply basic filtering for Full confidence (exclude flaky tests if requested)
                if (!options.IncludeFlakyTests)
                {
                    selectedTests = selectedTests.Where(t => !t.IsFlaky()).ToList();
                }
                
                // Apply category and tag filters if specified
                selectedTests = ApplyBasicFilters(selectedTests, options);
            }
            else
            {
                // Score all candidate tests for other confidence levels
                var scoredTestsResult = await _testScoringService.ScoreTestsAsync(candidateTests, changes, cancellationToken);
                var scoredTests = scoredTestsResult.ToList();

                // Select tests based on confidence level and constraints
                selectedTests = await SelectTestsForPlanAsync(scoredTests, confidenceLevel, options, cancellationToken);
            }

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

        private static List<TestInfo> ApplyBasicFilters(List<TestInfo> tests, TestSelectionOptions options)
        {
            var filtered = tests.AsEnumerable();

            // Apply category filters
            if (options.ExcludedCategories?.Count > 0)
            {
                filtered = filtered.Where(t => !options.ExcludedCategories.Contains(t.Category));
            }

            if (options.IncludedCategories?.Count > 0)
            {
                filtered = filtered.Where(t => options.IncludedCategories.Contains(t.Category));
            }

            // Apply tag filters
            if (options.ExcludedTags?.Count > 0)
            {
                filtered = filtered.Where(t => !t.Tags.Any(tag => options.ExcludedTags.Contains(tag)));
            }

            if (options.RequiredTags?.Count > 0)
            {
                filtered = filtered.Where(t => options.RequiredTags.Any(tag => t.Tags.Contains(tag)));
            }

            return filtered.ToList();
        }

        private Task<List<TestInfo>> SelectFastConfidenceTestsAsync(
            List<TestInfo> sortedTests,
            int maxTests,
            TimeSpan maxDuration,
            double minScore,
            TestSelectionOptions options,
            CancellationToken cancellationToken)
        {
            var selectedTests = new List<TestInfo>();
            var currentDuration = TimeSpan.Zero;

            // Strategy for Fast confidence: prioritize high-scoring unit tests first, then integration tests
            // 80% Unit tests (fast feedback and direct relationships), 20% Integration tests (broader coverage)
            var unitTestLimit = Math.Max(1, (int)(maxTests * 0.8));
            var integrationTestLimit = maxTests - unitTestLimit;
            
            var unitTestsSelected = 0;
            var integrationTestsSelected = 0;

            // Use a higher score threshold for Fast confidence to ensure quality, but allow unit tests with direct relationships
            var fastMinScore = Math.Max(minScore, 0.5); // Lower threshold to include direct unit tests

            _logger.LogDebug("Fast selection: targeting {UnitLimit} unit tests, {IntegrationLimit} integration tests, min score {MinScore}",
                unitTestLimit, integrationTestLimit, fastMinScore);

            // FIRST PASS: Select highest-scoring unit tests (likely direct relationships)
            var unitTests = sortedTests.Where(t => t.Category == TestCategory.Unit).OrderByDescending(t => t.SelectionScore);
            foreach (var test in unitTests)
            {
                if (unitTestsSelected >= unitTestLimit)
                    break;

                if (test.SelectionScore < fastMinScore)
                    continue;

                if (currentDuration + test.AverageExecutionTime > maxDuration)
                    continue;

                if (!PassesBasicFilters(test, options))
                    continue;

                selectedTests.Add(test);
                currentDuration += test.AverageExecutionTime;
                test.LastSelected = DateTimeOffset.UtcNow;
                unitTestsSelected++;

                _logger.LogDebug("Selected unit test: {TestName} (score: {Score:F3})", 
                    test.GetDisplayName(), test.SelectionScore);
            }

            // SECOND PASS: Select integration tests to fill remaining slots
            var integrationTests = sortedTests.Where(t => t.Category == TestCategory.Integration).OrderByDescending(t => t.SelectionScore);
            foreach (var test in integrationTests)
            {
                if (integrationTestsSelected >= integrationTestLimit)
                    break;

                if (selectedTests.Count >= maxTests)
                    break;

                if (test.SelectionScore < Math.Max(minScore, 0.4)) // Slightly higher threshold for integration tests
                    continue;

                if (currentDuration + test.AverageExecutionTime > maxDuration)
                    continue;

                if (!PassesBasicFilters(test, options))
                    continue;

                selectedTests.Add(test);
                currentDuration += test.AverageExecutionTime;
                test.LastSelected = DateTimeOffset.UtcNow;
                integrationTestsSelected++;

                _logger.LogDebug("Selected integration test: {TestName} (score: {Score:F3})", 
                    test.GetDisplayName(), test.SelectionScore);
            }

            // THIRD PASS: Fill remaining slots with any high-scoring tests if we have capacity
            if (selectedTests.Count < maxTests)
            {
                var remainingTests = sortedTests.Where(t => !selectedTests.Contains(t))
                    .OrderByDescending(t => t.SelectionScore);
                
                foreach (var test in remainingTests)
                {
                    if (selectedTests.Count >= maxTests)
                        break;

                    if (test.SelectionScore < minScore)
                        continue;

                    if (currentDuration + test.AverageExecutionTime > maxDuration)
                        continue;

                    if (!PassesBasicFilters(test, options))
                        continue;

                    selectedTests.Add(test);
                    currentDuration += test.AverageExecutionTime;
                    test.LastSelected = DateTimeOffset.UtcNow;
                }
            }

            _logger.LogInformation("Fast selection: selected {Total} tests ({Unit} unit, {Integration} integration, {Other} other), avg score: {AvgScore:F3}",
                selectedTests.Count,
                selectedTests.Count(t => t.Category == TestCategory.Unit),
                selectedTests.Count(t => t.Category == TestCategory.Integration),
                selectedTests.Count(t => t.Category != TestCategory.Unit && t.Category != TestCategory.Integration),
                selectedTests.Count > 0 ? selectedTests.Average(t => t.SelectionScore) : 0.0);

            return Task.FromResult(selectedTests);
        }

        private List<TestInfo> SelectStandardTests(
            List<TestInfo> sortedTests,
            int maxTests,
            TimeSpan maxDuration,
            double minScore,
            TestSelectionOptions options)
        {
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

                if (!PassesBasicFilters(test, options))
                    continue;

                selectedTests.Add(test);
                currentDuration += test.AverageExecutionTime;
                test.LastSelected = DateTimeOffset.UtcNow;
            }

            return selectedTests;
        }

        private static bool PassesBasicFilters(TestInfo test, TestSelectionOptions options)
        {
            // Apply category filters
            if (options.ExcludedCategories?.Contains(test.Category) == true)
                return false;

            if (options.IncludedCategories?.Count > 0 && 
                !options.IncludedCategories.Contains(test.Category))
                return false;

            // Apply tag filters
            if (options.ExcludedTags?.Count > 0 && 
                test.Tags.Any(tag => options.ExcludedTags.Contains(tag)))
                return false;

            if (options.RequiredTags?.Count > 0 && 
                !options.RequiredTags.Any(tag => test.Tags.Contains(tag)))
                return false;

            // Apply flaky test filter
            if (!options.IncludeFlakyTests && test.IsFlaky())
                return false;

            return true;
        }
    }
}