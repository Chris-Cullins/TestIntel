using System;

namespace TestIntelligence.SelectionEngine.Models
{
    /// <summary>
    /// Represents different confidence levels for test selection strategies.
    /// </summary>
    public enum ConfidenceLevel
    {
        /// <summary>
        /// Fast feedback - only direct unit tests, limited scope, ~30 seconds.
        /// Confidence: ~70%
        /// </summary>
        Fast,

        /// <summary>
        /// Medium confidence - includes integration tests, ~5 minutes.
        /// Confidence: ~85%
        /// </summary>
        Medium,

        /// <summary>
        /// High confidence - includes most relevant tests, ~15 minutes.
        /// Confidence: ~95%
        /// </summary>
        High,

        /// <summary>
        /// Full validation - all tests, comprehensive coverage.
        /// Confidence: ~99%
        /// </summary>
        Full
    }

    /// <summary>
    /// Extension methods for ConfidenceLevel.
    /// </summary>
    public static class ConfidenceLevelExtensions
    {
        /// <summary>
        /// Gets the confidence score as a percentage (0.0 to 1.0).
        /// </summary>
        public static double GetConfidenceScore(this ConfidenceLevel level)
        {
            return level switch
            {
                ConfidenceLevel.Fast => 0.7,
                ConfidenceLevel.Medium => 0.85,
                ConfidenceLevel.High => 0.95,
                ConfidenceLevel.Full => 0.99,
                _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
            };
        }

        /// <summary>
        /// Gets the estimated maximum execution time for this confidence level.
        /// </summary>
        public static TimeSpan GetEstimatedDuration(this ConfidenceLevel level)
        {
            return level switch
            {
                ConfidenceLevel.Fast => TimeSpan.FromSeconds(30),
                ConfidenceLevel.Medium => TimeSpan.FromMinutes(5),
                ConfidenceLevel.High => TimeSpan.FromMinutes(15),
                ConfidenceLevel.Full => TimeSpan.FromHours(1),
                _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
            };
        }

        /// <summary>
        /// Gets the maximum number of tests to include for this confidence level.
        /// </summary>
        public static int GetMaxTestCount(this ConfidenceLevel level)
        {
            return level switch
            {
                ConfidenceLevel.Fast => 50,
                ConfidenceLevel.Medium => 200,
                ConfidenceLevel.High => 1000,
                ConfidenceLevel.Full => int.MaxValue,
                _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
            };
        }
    }
}