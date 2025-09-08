namespace TestIntelligence.TestComparison.Models;

/// <summary>
/// Configuration options for test comparison analysis.
/// </summary>
public class ComparisonOptions
{
    /// <summary>
    /// Depth of analysis to perform when comparing tests.
    /// </summary>
    public AnalysisDepth Depth { get; init; } = AnalysisDepth.Medium;

    /// <summary>
    /// Weighting options for coverage overlap calculations.
    /// </summary>
    public WeightingOptions Weighting { get; init; } = new();

    /// <summary>
    /// Minimum confidence threshold for generating recommendations (0.0 to 1.0).
    /// Recommendations with lower confidence will be filtered out.
    /// </summary>
    public double MinimumConfidenceThreshold { get; init; } = 0.5;
}

/// <summary>
/// Defines the depth of analysis to perform during test comparison.
/// </summary>
public enum AnalysisDepth
{
    /// <summary>
    /// Basic coverage overlap analysis only.
    /// </summary>
    Shallow,

    /// <summary>
    /// Coverage overlap with basic metadata comparison.
    /// </summary>
    Medium,

    /// <summary>
    /// Comprehensive analysis including detailed metadata, call depth, and complexity weighting.
    /// </summary>
    Deep
}

/// <summary>
/// Options for weighting different aspects of coverage overlap analysis.
/// </summary>
public class WeightingOptions
{
    /// <summary>
    /// Factor by which method weights decay with increasing call depth (0.0 to 1.0).
    /// Higher values preserve weight at deeper call levels.
    /// </summary>
    public double CallDepthDecayFactor { get; init; } = 0.8;

    /// <summary>
    /// Weight multiplier for production code methods (typically 1.0).
    /// </summary>
    public double ProductionCodeWeight { get; init; } = 1.0;

    /// <summary>
    /// Weight multiplier for framework/library code methods (typically lower than production code).
    /// </summary>
    public double FrameworkCodeWeight { get; init; } = 0.3;

    /// <summary>
    /// Whether to adjust weights based on method complexity metrics.
    /// </summary>
    public bool UseComplexityWeighting { get; init; } = true;
}