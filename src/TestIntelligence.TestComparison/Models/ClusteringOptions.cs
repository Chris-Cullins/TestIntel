namespace TestIntelligence.TestComparison.Models;

/// <summary>
/// Configuration options for test clustering analysis.
/// </summary>
public class ClusteringOptions
{
    /// <summary>
    /// Minimum similarity threshold for grouping tests into clusters (0.0 to 1.0).
    /// </summary>
    public double SimilarityThreshold { get; init; } = 0.6;

    /// <summary>
    /// Clustering algorithm to use for grouping tests.
    /// </summary>
    public ClusteringAlgorithm Algorithm { get; init; } = ClusteringAlgorithm.Hierarchical;

    /// <summary>
    /// Maximum number of clusters to create.
    /// </summary>
    public int MaxClusters { get; init; } = 20;

    /// <summary>
    /// Minimum number of tests required to form a cluster.
    /// </summary>
    public int MinClusterSize { get; init; } = 2;

    /// <summary>
    /// Minimum average similarity within a cluster to be considered valid.
    /// </summary>
    public double MinIntraClusterSimilarity { get; init; } = 0.5;

    /// <summary>
    /// Comparison options to use for pairwise test similarity calculations.
    /// </summary>
    public ComparisonOptions ComparisonOptions { get; init; } = new();

    /// <summary>
    /// Whether to include execution path analysis in clustering (requires execution tracer).
    /// </summary>
    public bool IncludeExecutionPaths { get; init; } = true;

    /// <summary>
    /// Whether to generate characteristics and recommendations for each cluster.
    /// </summary>
    public bool GenerateClusterCharacteristics { get; init; } = true;

    /// <summary>
    /// Maximum time to spend on clustering analysis (in seconds). 0 means no limit.
    /// </summary>
    public int MaxAnalysisTimeSeconds { get; init; } = 300; // 5 minutes default

    /// <summary>
    /// Whether to parallelize similarity calculations for better performance.
    /// </summary>
    public bool EnableParallelProcessing { get; init; } = true;

    /// <summary>
    /// Linkage criteria for hierarchical clustering.
    /// </summary>
    public LinkageCriteria LinkageCriteria { get; init; } = LinkageCriteria.Complete;
}

/// <summary>
/// Available clustering algorithms.
/// </summary>
public enum ClusteringAlgorithm
{
    /// <summary>
    /// Hierarchical clustering using linkage criteria.
    /// </summary>
    Hierarchical,

    /// <summary>
    /// K-means clustering algorithm.
    /// </summary>
    KMeans,

    /// <summary>
    /// Density-based spatial clustering (DBSCAN).
    /// </summary>
    DBSCAN
}

/// <summary>
/// Linkage criteria for hierarchical clustering.
/// </summary>
public enum LinkageCriteria
{
    /// <summary>
    /// Single linkage - minimum distance between clusters.
    /// </summary>
    Single,

    /// <summary>
    /// Complete linkage - maximum distance between clusters.
    /// </summary>
    Complete,

    /// <summary>
    /// Average linkage - average distance between all pairs.
    /// </summary>
    Average,

    /// <summary>
    /// Ward linkage - minimize within-cluster variance.
    /// </summary>
    Ward
}