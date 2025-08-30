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
    /// Scoring algorithm that prioritizes tests based on execution time (faster tests get higher scores).
    /// </summary>
    public class ExecutionTimeScoringAlgorithm : ITestScoringAlgorithm
    {
        private readonly ILogger<ExecutionTimeScoringAlgorithm> _logger;

        // Thresholds for different confidence levels
        private static readonly TimeSpan FastTestThreshold = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan MediumTestThreshold = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan SlowTestThreshold = TimeSpan.FromSeconds(30);

        public ExecutionTimeScoringAlgorithm(ILogger<ExecutionTimeScoringAlgorithm> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string Name => "Execution Time Scoring";

        public double Weight => 0.3; // 30% weight in combined scoring

        public Task<double> CalculateScoreAsync(
            TestInfo testInfo, 
            TestScoringContext context, 
            CancellationToken cancellationToken = default)
        {
            if (testInfo == null) throw new ArgumentNullException(nameof(testInfo));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var score = CalculateExecutionTimeScore(testInfo, context);

            _logger.LogTrace("Execution time score for {TestName}: {Score:F3} (Duration: {Duration}ms)", 
                testInfo.GetDisplayName(), score, testInfo.AverageExecutionTime.TotalMilliseconds);

            return Task.FromResult(score);
        }

        private double CalculateExecutionTimeScore(TestInfo testInfo, TestScoringContext context)
        {
            var executionTime = testInfo.AverageExecutionTime;
            var confidenceLevel = context.ConfidenceLevel;

            // Base score based on execution time (faster = higher score)
            var score = CalculateBaseTimeScore(executionTime);

            // Adjust score based on confidence level
            score = AdjustForConfidenceLevel(score, confidenceLevel, executionTime);

            // Penalty for tests with high variability in execution time
            score = ApplyVariabilityPenalty(score, testInfo);

            return Math.Max(0.0, Math.Min(1.0, score));
        }

        private double CalculateBaseTimeScore(TimeSpan executionTime)
        {
            var milliseconds = executionTime.TotalMilliseconds;

            // Logarithmic scoring - very fast tests get high scores
            if (milliseconds <= 50) // Super fast tests (≤50ms)
                return 1.0;
            
            if (milliseconds <= 100) // Very fast tests (≤100ms)
                return 0.9;
            
            if (milliseconds <= 250) // Fast tests (≤250ms)
                return 0.8;
            
            if (milliseconds <= 500) // Medium-fast tests (≤500ms)
                return 0.7;
            
            if (milliseconds <= 1000) // Medium tests (≤1s)
                return 0.5;
            
            if (milliseconds <= 5000) // Slow tests (≤5s)
                return 0.3;
            
            if (milliseconds <= 15000) // Very slow tests (≤15s)
                return 0.2;
            
            // Extremely slow tests (>15s)
            return 0.1;
        }

        private double AdjustForConfidenceLevel(double baseScore, ConfidenceLevel confidenceLevel, TimeSpan executionTime)
        {
            switch (confidenceLevel)
            {
                case ConfidenceLevel.Fast:
                    // Fast confidence: heavily penalize slow tests
                    if (executionTime > FastTestThreshold)
                        return baseScore * 0.3; // Severe penalty for tests >500ms
                    return baseScore * 1.2; // Boost fast tests

                case ConfidenceLevel.Medium:
                    // Medium confidence: moderate penalty for slow tests
                    if (executionTime > MediumTestThreshold)
                        return baseScore * 0.6; // Penalty for tests >5s
                    return baseScore;

                case ConfidenceLevel.High:
                    // High confidence: small penalty for very slow tests
                    if (executionTime > SlowTestThreshold)
                        return baseScore * 0.8; // Small penalty for tests >30s
                    return baseScore;

                case ConfidenceLevel.Full:
                    // Full confidence: execution time matters less
                    return baseScore * 0.9; // Slight preference for faster tests
                
                default:
                    return baseScore;
            }
        }

        private double ApplyVariabilityPenalty(double score, TestInfo testInfo)
        {
            if (testInfo.ExecutionHistory.Count < 3)
                return score; // Not enough history to determine variability

            // Calculate coefficient of variation for execution times
            var times = new List<double>();
            foreach (var result in testInfo.ExecutionHistory)
            {
                times.Add(result.Duration.TotalMilliseconds);
            }

            if (times.Count == 0) return score;

            var mean = times.Average();
            var variance = times.Select(t => Math.Pow(t - mean, 2)).Average();
            var stdDev = Math.Sqrt(variance);
            
            var coefficientOfVariation = mean > 0 ? stdDev / mean : 0;

            // Apply penalty for high variability (unpredictable execution times)
            if (coefficientOfVariation > 0.5) // High variability (>50%)
            {
                return score * 0.8;
            }
            else if (coefficientOfVariation > 0.3) // Medium variability (>30%)
            {
                return score * 0.9;
            }

            return score; // Low variability, no penalty
        }
    }
}