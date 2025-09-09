using System;
using System.Collections.Generic;
using TestIntelligence.Core.Models;

namespace TestIntelligence.TestComparison.Models;

/// <summary>
/// Results of clustering analysis on multiple test methods.
/// </summary>
public class TestClusterAnalysis
{
    /// <summary>
    /// Collection of identified test clusters.
    /// </summary>
    public required IReadOnlyList<TestCluster> Clusters { get; init; }

    /// <summary>
    /// Tests that did not meet clustering criteria and remain unclustered.
    /// </summary>
    public required IReadOnlyList<string> UnclusteredTests { get; init; }

    /// <summary>
    /// Statistical information about the clustering analysis.
    /// </summary>
    public ClusteringStatistics Statistics { get; init; } = new();

    /// <summary>
    /// Configuration options used for this clustering analysis.
    /// </summary>
    public ClusteringOptions Options { get; init; } = new();

    /// <summary>
    /// Timestamp when this analysis was performed.
    /// </summary>
    public DateTime AnalysisTimestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Duration of the clustering analysis operation.
    /// </summary>
    public TimeSpan AnalysisDuration { get; init; }

    /// <summary>
    /// Any warnings or issues encountered during analysis.
    /// </summary>
    public IReadOnlyList<string>? Warnings { get; init; }

    /// <summary>
    /// Gets a summary of the clustering results.
    /// </summary>
    public string GetSummary()
    {
        var quality = Statistics.SilhouetteScore switch
        {
            >= 0.7 => "excellent",
            >= 0.5 => "good",
            >= 0.3 => "fair",
            _ => "poor"
        };

        return $"Analyzed {Statistics.TotalTests} tests, found {Statistics.NumberOfClusters} clusters " +
               $"with {quality} quality (silhouette score: {Statistics.SilhouetteScore:F2}). " +
               $"{UnclusteredTests.Count} tests remained unclustered.";
    }
}

/// <summary>
/// Represents a cluster of similar test methods.
/// </summary>
public class TestCluster
{
    /// <summary>
    /// Unique identifier for this cluster.
    /// </summary>
    public int ClusterId { get; init; }

    /// <summary>
    /// Test method identifiers belonging to this cluster.
    /// </summary>
    public required IReadOnlyList<string> TestIds { get; init; }

    /// <summary>
    /// Average similarity score within this cluster.
    /// </summary>
    public double IntraClusterSimilarity { get; init; }

    /// <summary>
    /// Cohesion score indicating how tightly grouped the cluster is.
    /// </summary>
    public double CohesionScore { get; init; }

    /// <summary>
    /// Optimization recommendations specific to this cluster.
    /// </summary>
    public required IReadOnlyList<OptimizationRecommendation> Recommendations { get; init; }

    /// <summary>
    /// Common characteristics shared by tests in this cluster.
    /// </summary>
    public ClusterCharacteristics Characteristics { get; init; } = new();

    /// <summary>
    /// Gets the size of this cluster.
    /// </summary>
    public int Size => TestIds.Count;

    /// <summary>
    /// Gets a human-readable description of this cluster.
    /// </summary>
    public string GetDescription()
    {
        var coherence = CohesionScore switch
        {
            >= 0.8 => "highly coherent",
            >= 0.6 => "coherent",
            >= 0.4 => "moderately coherent",
            _ => "loosely coherent"
        };

        return $"Cluster {ClusterId}: {Size} tests, {coherence} " +
               $"(similarity: {IntraClusterSimilarity:F2}, cohesion: {CohesionScore:F2})";
    }
}

/// <summary>
/// Common characteristics shared by tests within a cluster.
/// </summary>
public class ClusterCharacteristics
{
    /// <summary>
    /// Production methods commonly executed by tests in this cluster.
    /// </summary>
    public IReadOnlySet<string> CommonMethods { get; init; } = new HashSet<string>();

    /// <summary>
    /// Test categories represented in this cluster.
    /// </summary>
    public IReadOnlySet<TestCategory> Categories { get; init; } = new HashSet<TestCategory>();

    /// <summary>
    /// Tags shared across tests in this cluster.
    /// </summary>
    public IReadOnlySet<string> CommonTags { get; init; } = new HashSet<string>();

    /// <summary>
    /// Average execution time for tests in this cluster.
    /// </summary>
    public double AverageExecutionTime { get; init; }

    /// <summary>
    /// Suggested name for this cluster based on common patterns.
    /// </summary>
    public string SuggestedName { get; init; } = string.Empty;

    /// <summary>
    /// Common namespace or class patterns within the cluster.
    /// </summary>
    public IReadOnlySet<string> CommonNamespaces { get; init; } = new HashSet<string>();

    /// <summary>
    /// Dominant test pattern type (e.g., "Unit", "Integration", "Repository").
    /// </summary>
    public string DominantPattern { get; init; } = string.Empty;
}

/// <summary>
/// Statistical information about the clustering analysis.
/// </summary>
public class ClusteringStatistics
{
    /// <summary>
    /// Total number of tests analyzed.
    /// </summary>
    public int TotalTests { get; init; }

    /// <summary>
    /// Number of clusters identified.
    /// </summary>
    public int NumberOfClusters { get; init; }

    /// <summary>
    /// Silhouette score indicating overall clustering quality (-1 to 1).
    /// Higher values indicate better clustering.
    /// </summary>
    public double SilhouetteScore { get; init; }

    /// <summary>
    /// Average similarity within clusters.
    /// </summary>
    public double AverageIntraClusterSimilarity { get; init; }

    /// <summary>
    /// Average similarity between different clusters.
    /// </summary>
    public double AverageInterClusterSimilarity { get; init; }

    /// <summary>
    /// Percentage of tests that were successfully clustered.
    /// </summary>
    public double ClusteringRate { get; init; }

    /// <summary>
    /// Largest cluster size.
    /// </summary>
    public int LargestClusterSize { get; init; }

    /// <summary>
    /// Smallest cluster size.
    /// </summary>
    public int SmallestClusterSize { get; init; }

    /// <summary>
    /// Standard deviation of cluster sizes.
    /// </summary>
    public double ClusterSizeVariance { get; init; }
}