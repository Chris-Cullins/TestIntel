using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.SelectionEngine.Interfaces;
using TestIntelligence.ImpactAnalyzer.Models;
using TestIntelligence.SelectionEngine.Algorithms;

namespace TestIntelligence.SelectionEngine.Services
{
    /// <summary>
    /// Service responsible for scoring tests using various algorithms.
    /// </summary>
    public class TestScoringService : ITestScoringService
    {
        private readonly ILogger<TestScoringService> _logger;
        private readonly List<ITestScoringAlgorithm> _scoringAlgorithms;

        public TestScoringService(
            ILogger<TestScoringService> logger,
            IEnumerable<ITestScoringAlgorithm>? scoringAlgorithms = null,
            ILoggerFactory? loggerFactory = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Initialize default scoring algorithms if none provided
            _scoringAlgorithms = new List<ITestScoringAlgorithm>(scoringAlgorithms ?? CreateDefaultAlgorithms(loggerFactory));
        }

        private static List<ITestScoringAlgorithm> CreateDefaultAlgorithms(ILoggerFactory? loggerFactory)
        {
            return new List<ITestScoringAlgorithm>
            {
                new ImpactBasedScoringAlgorithm(loggerFactory?.CreateLogger<ImpactBasedScoringAlgorithm>() ?? 
                    new NullLogger<ImpactBasedScoringAlgorithm>()),
                new ExecutionTimeScoringAlgorithm(loggerFactory?.CreateLogger<ExecutionTimeScoringAlgorithm>() ?? 
                    new NullLogger<ExecutionTimeScoringAlgorithm>()),
                new HistoricalScoringAlgorithm(loggerFactory?.CreateLogger<HistoricalScoringAlgorithm>() ?? 
                    new NullLogger<HistoricalScoringAlgorithm>())
            };
        }

        public IReadOnlyList<ITestScoringAlgorithm> ScoringAlgorithms => _scoringAlgorithms.AsReadOnly();

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
                test.SelectionScore = await CalculateCombinedScoreAsync(test, context, cancellationToken);
            }

            return tests.OrderByDescending(t => t.SelectionScore).ToList();
        }

        public async Task<double> CalculateCombinedScoreAsync(
            TestInfo testInfo, 
            TestScoringContext context,
            CancellationToken cancellationToken = default)
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
    }

    /// <summary>
    /// Null logger implementation for cases where specific logger isn't available.
    /// </summary>
    internal class NullLogger<T> : ILogger<T>
    {
        IDisposable? ILogger.BeginScope<TState>(TState state) => new NullDisposable();
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }

        private class NullDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}