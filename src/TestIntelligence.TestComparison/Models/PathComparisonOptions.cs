namespace TestIntelligence.TestComparison.Models;

/// <summary>
/// Configuration options for execution path comparison analysis.
/// </summary>
public class PathComparisonOptions
{
    /// <summary>
    /// Whether to include structural similarity analysis (graph topology).
    /// </summary>
    public bool IncludeStructuralSimilarity { get; init; } = true;

    /// <summary>
    /// Whether to include sequential similarity analysis (call order).
    /// </summary>
    public bool IncludeSequentialSimilarity { get; init; } = true;

    /// <summary>
    /// Weight given to sequential similarity in overall calculation (0.0 to 1.0).
    /// </summary>
    public double SequentialWeight { get; init; } = 0.6;

    /// <summary>
    /// Weight given to structural similarity in overall calculation (0.0 to 1.0).
    /// </summary>
    public double StructuralWeight { get; init; } = 0.4;

    /// <summary>
    /// Maximum call depth to analyze for performance optimization.
    /// </summary>
    public int MaxAnalysisDepth { get; init; } = 20;

    /// <summary>
    /// Whether to ignore framework and infrastructure method calls in analysis.
    /// </summary>
    public bool IgnoreFrameworkCalls { get; init; } = true;

    /// <summary>
    /// Minimum similarity threshold for considering paths as related.
    /// </summary>
    public double MinimumSimilarityThreshold { get; init; } = 0.1;

    /// <summary>
    /// Whether to perform deep analysis including call frequency patterns.
    /// </summary>
    public bool EnableDeepAnalysis { get; init; } = false;

    /// <summary>
    /// Maximum number of divergence points to track for performance.
    /// </summary>
    public int MaxDivergencePoints { get; init; } = 50;
}