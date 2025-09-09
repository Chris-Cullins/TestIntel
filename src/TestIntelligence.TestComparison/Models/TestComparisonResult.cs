using System;
using System.Collections.Generic;

namespace TestIntelligence.TestComparison.Models;

/// <summary>
/// Complete results of comparing two test methods.
/// </summary>
public class TestComparisonResult
{
    /// <summary>
    /// Gets or sets the unique identifier of the first test method.
    /// </summary>
    public required string Test1Id { get; init; }

    /// <summary>
    /// Gets or sets the unique identifier of the second test method.
    /// </summary>
    public required string Test2Id { get; init; }

    /// <summary>
    /// Gets or sets the overall similarity score (0.0 to 1.0).
    /// Combines coverage overlap, metadata similarity, and other factors.
    /// </summary>
    public double OverallSimilarity { get; init; }

    /// <summary>
    /// Gets or sets the detailed analysis of coverage overlap between the tests.
    /// </summary>
    public required CoverageOverlapAnalysis CoverageOverlap { get; init; }

    /// <summary>
    /// Gets or sets the metadata-based similarity analysis.
    /// </summary>
    public required MetadataSimilarity MetadataSimilarity { get; init; }

    /// <summary>
    /// Gets or sets the execution path similarity analysis.
    /// Available when analysis depth is Medium or Deep.
    /// </summary>
    public ExecutionPathSimilarity? ExecutionPathSimilarity { get; init; }

    /// <summary>
    /// Gets or sets the optimization recommendations based on the comparison analysis.
    /// </summary>
    public required IReadOnlyList<OptimizationRecommendation> Recommendations { get; init; }

    /// <summary>
    /// Gets or sets the timestamp when this analysis was performed.
    /// </summary>
    public DateTime AnalysisTimestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the comparison options that were used for this analysis.
    /// </summary>
    public ComparisonOptions Options { get; init; } = new();

    /// <summary>
    /// Gets or sets the duration of the analysis operation.
    /// </summary>
    public TimeSpan AnalysisDuration { get; init; }

    /// <summary>
    /// Gets or sets any warnings or issues encountered during analysis.
    /// </summary>
    public IReadOnlyList<string>? Warnings { get; init; }

    /// <summary>
    /// Gets a human-readable summary of the comparison results.
    /// </summary>
    public string GetSummary()
    {
        var similarityDescription = OverallSimilarity switch
        {
            >= 0.8 => "very high",
            >= 0.6 => "high", 
            >= 0.4 => "moderate",
            >= 0.2 => "low",
            _ => "very low"
        };

        return $"Tests {Test1Id} and {Test2Id} have {similarityDescription} similarity " +
               $"({OverallSimilarity:P1}) with {CoverageOverlap.OverlapPercentage:F1}% coverage overlap " +
               $"({CoverageOverlap.SharedProductionMethods} shared methods).";
    }

    /// <summary>
    /// Gets the primary recommendation with the highest confidence score.
    /// </summary>
    /// <returns>The recommendation with the highest confidence, or null if no recommendations exist.</returns>
    public OptimizationRecommendation? GetPrimaryRecommendation()
    {
        if (Recommendations.Count == 0) return null;

        OptimizationRecommendation? best = null;
        foreach (var recommendation in Recommendations)
        {
            if (best == null || recommendation.ConfidenceScore > best.ConfidenceScore)
                best = recommendation;
        }

        return best;
    }
}