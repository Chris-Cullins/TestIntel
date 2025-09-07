using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.SelectionEngine.Interfaces;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.SelectionEngine.Services;
using TestIntelligence.ImpactAnalyzer.Models;

namespace TestIntelligence.SelectionEngine.Engine
{
    /// <summary>
    /// Refactored test selection engine that coordinates focused services.
    /// This replaces the monolithic TestSelectionEngine with a cleaner, service-oriented approach.
    /// </summary>
    public class TestSelectionEngineRefactored : ITestSelectionEngine
    {
        private readonly ILogger<TestSelectionEngineRefactored> _logger;
        private readonly ITestDiscoveryService _testDiscoveryService;
        private readonly ITestScoringService _testScoringService;
        private readonly ITestPlanService _testPlanService;
        private readonly ITestHistoryService _testHistoryService;

        public TestSelectionEngineRefactored(
            ILogger<TestSelectionEngineRefactored> logger,
            ITestDiscoveryService testDiscoveryService,
            ITestScoringService testScoringService,
            ITestPlanService testPlanService,
            ITestHistoryService testHistoryService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _testDiscoveryService = testDiscoveryService ?? throw new ArgumentNullException(nameof(testDiscoveryService));
            _testScoringService = testScoringService ?? throw new ArgumentNullException(nameof(testScoringService));
            _testPlanService = testPlanService ?? throw new ArgumentNullException(nameof(testPlanService));
            _testHistoryService = testHistoryService ?? throw new ArgumentNullException(nameof(testHistoryService));
        }

        public async Task<TestExecutionPlan> GetOptimalTestPlanAsync(
            CodeChangeSet changes, 
            ConfidenceLevel confidenceLevel, 
            TestSelectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Creating optimal test plan for {ChangeCount} changes with {ConfidenceLevel} confidence", 
                changes.Changes.Count, confidenceLevel);

            return await _testPlanService.CreateOptimalTestPlanAsync(changes, confidenceLevel, options, cancellationToken);
        }

        public async Task<TestExecutionPlan> GetTestPlanAsync(
            ConfidenceLevel confidenceLevel, 
            TestSelectionOptions? options = null, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Creating test plan with {ConfidenceLevel} confidence", confidenceLevel);

            return await _testPlanService.CreateTestPlanAsync(confidenceLevel, options, cancellationToken);
        }

        public async Task<IReadOnlyList<TestInfo>> ScoreTestsAsync(
            IEnumerable<TestInfo> candidateTests, 
            CodeChangeSet? changes = null, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Scoring candidate tests");

            return await _testScoringService.ScoreTestsAsync(candidateTests, changes, cancellationToken);
        }

        public async Task UpdateTestExecutionHistoryAsync(
            IEnumerable<TestExecutionResult> results, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Updating test execution history");

            await _testHistoryService.UpdateTestExecutionHistoryAsync(results, cancellationToken);
        }

        public async Task<IReadOnlyList<TestInfo>> GetTestHistoryAsync(
            string? testFilter = null, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Retrieving test history with filter: {Filter}", testFilter ?? "none");

            return await _testHistoryService.GetTestHistoryAsync(testFilter, cancellationToken);
        }
    }
}