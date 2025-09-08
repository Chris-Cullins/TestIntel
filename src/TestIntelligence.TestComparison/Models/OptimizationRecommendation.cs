using System.Text.Json.Serialization;

namespace TestIntelligence.TestComparison.Models;

/// <summary>
/// Represents an optimization suggestion based on test comparison analysis.
/// </summary>
public class OptimizationRecommendation
{
    /// <summary>
    /// Gets or sets the type of recommendation (e.g., "merge", "extract_common", "maintain_separate").
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets or sets a human-readable description of the recommendation.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets or sets the confidence score for this recommendation (0.0 to 1.0).
    /// Higher scores indicate stronger confidence in the recommendation.
    /// </summary>
    public double ConfidenceScore { get; init; }

    /// <summary>
    /// Gets or sets the estimated effort level required to implement this recommendation.
    /// </summary>
    public EstimatedEffortLevel EstimatedEffortLevel { get; init; }

    /// <summary>
    /// Gets or sets the rationale explaining why this recommendation was made.
    /// </summary>
    public required string Rationale { get; init; }

    /// <summary>
    /// Gets or sets potential impact of implementing this recommendation.
    /// </summary>
    public string? ImpactDescription { get; init; }

    /// <summary>
    /// Gets or sets potential risks or considerations when implementing this recommendation.
    /// </summary>
    public string? RisksAndConsiderations { get; init; }
}

/// <summary>
/// Represents the estimated effort required to implement a recommendation.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstimatedEffortLevel
{
    /// <summary>
    /// Minimal effort required, can be done in minutes.
    /// </summary>
    Low,

    /// <summary>
    /// Moderate effort required, may take hours.
    /// </summary>
    Medium,

    /// <summary>
    /// Significant effort required, may take days or involve risk.
    /// </summary>
    High
}