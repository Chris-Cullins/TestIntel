using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.SelectionEngine.Interfaces;
using TestIntelligence.SelectionEngine.Models;

namespace TestIntelligence.SelectionEngine.Algorithms
{
    /// <summary>
    /// Scoring algorithm that prioritizes tests based on historical failure patterns and success rates.
    /// </summary>
    public class HistoricalScoringAlgorithm : ITestScoringAlgorithm
    {
        private readonly ILogger<HistoricalScoringAlgorithm> _logger;

        public HistoricalScoringAlgorithm(ILogger<HistoricalScoringAlgorithm> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string Name => "Historical Pattern Scoring";

        public double Weight => 0.3; // 30% weight in combined scoring

        public Task<double> CalculateScoreAsync(
            TestInfo testInfo, 
            TestScoringContext context, 
            CancellationToken cancellationToken = default)
        {
            if (testInfo == null) throw new ArgumentNullException(nameof(testInfo));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var score = CalculateHistoricalScore(testInfo, context);

            _logger.LogTrace("Historical score for {TestName}: {Score:F3}", 
                testInfo.GetDisplayName(), score);

            return Task.FromResult(score);
        }

        private double CalculateHistoricalScore(TestInfo testInfo, TestScoringContext context)
        {
            var score = 0.0;

            // Base score from execution history
            score += CalculateReliabilityScore(testInfo);
            
            // Score based on recent failure patterns
            score += CalculateRecentFailureScore(testInfo);
            
            // Score based on test frequency and recency
            score += CalculateFrequencyScore(testInfo);
            
            // Adjust for flaky tests
            score = AdjustForFlakiness(score, testInfo, context);

            return Math.Max(0.0, Math.Min(1.0, score));
        }

        private double CalculateReliabilityScore(TestInfo testInfo)
        {
            if (testInfo.ExecutionHistory.Count == 0)
                return 0.5; // Neutral score for tests with no history

            var totalExecutions = testInfo.ExecutionHistory.Count;
            var passCount = testInfo.ExecutionHistory.Count(r => r.Passed);
            var successRate = (double)passCount / totalExecutions;

            // Favor tests with moderate success rates over those that always pass or always fail
            // Tests that sometimes fail are more likely to catch regressions
            if (successRate >= 0.7 && successRate <= 0.95) // Sweet spot: 70-95% success
            {
                return 0.8;
            }
            else if (successRate >= 0.95) // Very reliable tests
            {
                return 0.6;
            }
            else if (successRate >= 0.5) // Moderately reliable
            {
                return 0.5;
            }
            else if (successRate >= 0.2) // Often failing - might be important but problematic
            {
                return 0.3;
            }
            else // Almost always failing - likely broken
            {
                return 0.1;
            }
        }

        private double CalculateRecentFailureScore(TestInfo testInfo)
        {
            var now = DateTimeOffset.UtcNow;
            var recentFailures = testInfo.ExecutionHistory
                .Where(r => !r.Passed && (now - r.ExecutedAt).TotalDays <= 7)
                .OrderByDescending(r => r.ExecutedAt)
                .Take(5)
                .ToList();

            if (recentFailures.Count == 0)
                return 0.0;

            var score = 0.0;

            // Score based on how recent the failures are
            foreach (var failure in recentFailures)
            {
                var daysAgo = (now - failure.ExecutedAt).TotalDays;
                if (daysAgo <= 1) // Within 24 hours
                    score += 0.3;
                else if (daysAgo <= 3) // Within 3 days
                    score += 0.2;
                else // Within a week
                    score += 0.1;
            }

            // Bonus for consistent recent failures (might indicate a new regression)
            if (recentFailures.Count >= 2)
            {
                var failureSpan = recentFailures.Max(f => f.ExecutedAt) - recentFailures.Min(f => f.ExecutedAt);
                if (failureSpan.TotalDays <= 2) // Failures clustered within 2 days
                    score += 0.2;
            }

            return Math.Min(0.5, score); // Cap at 0.5
        }

        private double CalculateFrequencyScore(TestInfo testInfo)
        {
            var now = DateTimeOffset.UtcNow;
            
            // Check execution frequency in different time windows
            var executions7Days = testInfo.ExecutionHistory.Count(r => (now - r.ExecutedAt).TotalDays <= 7);
            var executions30Days = testInfo.ExecutionHistory.Count(r => (now - r.ExecutedAt).TotalDays <= 30);

            var score = 0.0;

            // Reward tests that are executed frequently (they're probably important)
            if (executions7Days >= 5) // Executed 5+ times in last week
                score += 0.2;
            else if (executions7Days >= 2) // Executed 2+ times in last week
                score += 0.1;

            if (executions30Days >= 10) // Executed 10+ times in last month
                score += 0.2;
            else if (executions30Days >= 5) // Executed 5+ times in last month
                score += 0.1;

            // Check if test was executed recently
            var lastExecution = testInfo.GetLastExecutionResult();
            if (lastExecution != null)
            {
                var daysSinceLastRun = (now - lastExecution.ExecutedAt).TotalDays;
                if (daysSinceLastRun <= 1) // Executed within 24 hours
                    score += 0.1;
                else if (daysSinceLastRun <= 7) // Executed within a week
                    score += 0.05;
            }

            return Math.Min(0.4, score); // Cap at 0.4
        }

        private double AdjustForFlakiness(double score, TestInfo testInfo, TestScoringContext context)
        {
            if (!testInfo.IsFlaky())
                return score;

            // Handle flaky tests based on confidence level
            switch (context.ConfidenceLevel)
            {
                case ConfidenceLevel.Fast:
                    // For fast feedback, heavily penalize flaky tests
                    return score * 0.3;

                case ConfidenceLevel.Medium:
                    // For medium confidence, moderately penalize flaky tests
                    return score * 0.6;

                case ConfidenceLevel.High:
                    // For high confidence, slightly penalize flaky tests
                    return score * 0.8;

                case ConfidenceLevel.Full:
                    // For full confidence, include flaky tests but with penalty
                    return score * 0.9;

                default:
                    return score * 0.7;
            }
        }
    }
}