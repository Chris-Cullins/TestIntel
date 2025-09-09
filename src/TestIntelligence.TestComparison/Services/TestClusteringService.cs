using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.TestComparison.Models;

namespace TestIntelligence.TestComparison.Services;

/// <summary>
/// Service for clustering test methods based on similarity analysis.
/// Implements hierarchical clustering with various linkage criteria.
/// </summary>
public class TestClusteringService
{
    private readonly ITestComparisonService _comparisonService;
    private readonly ILogger<TestClusteringService> _logger;

    public TestClusteringService(
        ITestComparisonService comparisonService,
        ILogger<TestClusteringService> logger)
    {
        _comparisonService = comparisonService ?? throw new ArgumentNullException(nameof(comparisonService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Performs hierarchical clustering on a collection of tests.
    /// </summary>
    /// <param name="testIds">Collection of test method identifiers</param>
    /// <param name="solutionPath">Path to solution file</param>
    /// <param name="options">Clustering configuration options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Clustering analysis results</returns>
    public async Task<TestClusterAnalysis> PerformHierarchicalClusteringAsync(
        IEnumerable<string> testIds,
        string solutionPath,
        ClusteringOptions options,
        CancellationToken cancellationToken)
    {
        if (testIds == null) throw new ArgumentNullException(nameof(testIds));
        if (string.IsNullOrEmpty(solutionPath)) throw new ArgumentException("Solution path cannot be null or empty", nameof(solutionPath));
        if (options == null) throw new ArgumentNullException(nameof(options));

        var testIdList = testIds.ToList();
        if (testIdList.Count < 2)
        {
            throw new ArgumentException("At least 2 tests are required for clustering analysis", nameof(testIds));
        }

        _logger.LogInformation("Starting hierarchical clustering analysis for {TestCount} tests with {Algorithm} algorithm",
            testIdList.Count, options.Algorithm);

        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<string>();

        try
        {
            // Step 1: Build similarity matrix for all test pairs
            var similarityMatrix = await BuildSimilarityMatrixAsync(testIdList, solutionPath, options, cancellationToken);

            // Step 2: Perform hierarchical clustering
            var clusters = PerformHierarchicalClustering(similarityMatrix, options);

            // Step 3: Calculate cluster quality metrics
            var statistics = CalculateClusteringStatistics(clusters, similarityMatrix);

            // Step 4: Generate characteristics and recommendations for each cluster
            var enrichedClusters = options.GenerateClusterCharacteristics 
                ? await EnrichClustersWithCharacteristicsAsync(clusters, solutionPath, cancellationToken)
                : clusters;

            // Step 5: Identify unclustered tests
            var unclusteredTests = FindUnclusteredTests(testIdList, enrichedClusters);

            var result = new TestClusterAnalysis
            {
                Clusters = enrichedClusters,
                UnclusteredTests = unclusteredTests,
                Statistics = statistics,
                Options = options,
                AnalysisTimestamp = DateTime.UtcNow,
                AnalysisDuration = stopwatch.Elapsed,
                Warnings = warnings.Count > 0 ? warnings.AsReadOnly() : null
            };

            _logger.LogInformation("Clustering analysis completed in {Duration:F2}s. Found {ClusterCount} clusters, " +
                "{UnclusteredCount} unclustered tests. Quality score: {SilhouetteScore:F2}",
                stopwatch.Elapsed.TotalSeconds, enrichedClusters.Count, unclusteredTests.Count, statistics.SilhouetteScore);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform clustering analysis after {Duration:F2}s", stopwatch.Elapsed.TotalSeconds);
            throw;
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    /// <summary>
    /// Builds a similarity matrix for all test pairs.
    /// </summary>
    private async Task<SimilarityMatrix> BuildSimilarityMatrixAsync(
        IReadOnlyList<string> testIds,
        string solutionPath,
        ClusteringOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Building similarity matrix for {TestCount} tests ({PairCount} comparisons)",
            testIds.Count, (testIds.Count * (testIds.Count - 1)) / 2);

        var matrix = new SimilarityMatrix(testIds);
        var comparisonTasks = new List<Task>();

        for (int i = 0; i < testIds.Count; i++)
        {
            for (int j = i + 1; j < testIds.Count; j++)
            {
                var task = CalculateAndStoreSimilarityAsync(i, j, testIds, solutionPath, matrix, options, cancellationToken);
                comparisonTasks.Add(task);

                // Limit concurrent operations for memory management
                if (comparisonTasks.Count >= 10 || !options.EnableParallelProcessing)
                {
                    await Task.WhenAll(comparisonTasks);
                    comparisonTasks.Clear();
                }
            }
        }

        if (comparisonTasks.Any())
        {
            await Task.WhenAll(comparisonTasks);
        }

        _logger.LogDebug("Similarity matrix completed. Statistics: {Statistics}",
            matrix.GetStatistics());

        return matrix;
    }

    /// <summary>
    /// Calculates and stores similarity between two tests in the matrix.
    /// </summary>
    private async Task CalculateAndStoreSimilarityAsync(
        int index1,
        int index2,
        IReadOnlyList<string> testIds,
        string solutionPath,
        SimilarityMatrix matrix,
        ClusteringOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _comparisonService.CompareTestsAsync(
                testIds[index1], testIds[index2], solutionPath, options.ComparisonOptions, cancellationToken);

            matrix.SetSimilarity(index1, index2, result.OverallSimilarity);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate similarity between {Test1} and {Test2}, using default value 0.0",
                testIds[index1], testIds[index2]);
            matrix.SetSimilarity(index1, index2, 0.0);
        }
    }

    /// <summary>
    /// Performs hierarchical clustering using the specified linkage criteria.
    /// </summary>
    private IReadOnlyList<TestCluster> PerformHierarchicalClustering(
        SimilarityMatrix similarityMatrix,
        ClusteringOptions options)
    {
        _logger.LogDebug("Performing hierarchical clustering with {LinkageCriteria} linkage, threshold: {Threshold}",
            options.LinkageCriteria, options.SimilarityThreshold);

        var clusters = new List<TestCluster>();
        var testIds = similarityMatrix.TestIds.ToList();
        
        // Initialize each test as its own cluster
        var currentClusters = testIds.Select((testId, index) => new List<int> { index }).ToList();

        // Build cluster hierarchy using agglomerative clustering
        while (currentClusters.Count > 1 && clusters.Count < options.MaxClusters)
        {
            // Find the pair of clusters with highest similarity
            var (cluster1Index, cluster2Index, similarity) = FindMostSimilarClusters(
                currentClusters, similarityMatrix, options.LinkageCriteria);

            // Stop if similarity is below threshold
            if (similarity < options.SimilarityThreshold)
                break;

            // Merge the two most similar clusters
            var mergedCluster = currentClusters[cluster1Index].Concat(currentClusters[cluster2Index]).ToList();
            
            // Remove the original clusters (remove higher index first to maintain indices)
            var indexToRemoveFirst = Math.Max(cluster1Index, cluster2Index);
            var indexToRemoveSecond = Math.Min(cluster1Index, cluster2Index);
            
            currentClusters.RemoveAt(indexToRemoveFirst);
            currentClusters.RemoveAt(indexToRemoveSecond);
            
            // Add the merged cluster
            currentClusters.Add(mergedCluster);
        }

        // Convert remaining clusters to TestCluster objects
        var clusterId = 1;
        foreach (var cluster in currentClusters)
        {
            if (cluster.Count >= options.MinClusterSize)
            {
                var testIdsInCluster = cluster.Select(index => testIds[index]).ToList().AsReadOnly();
                var intraClusterSimilarity = CalculateIntraClusterSimilarity(cluster, similarityMatrix);
                
                if (intraClusterSimilarity >= options.MinIntraClusterSimilarity)
                {
                    clusters.Add(new TestCluster
                    {
                        ClusterId = clusterId++,
                        TestIds = testIdsInCluster,
                        IntraClusterSimilarity = intraClusterSimilarity,
                        CohesionScore = CalculateCohesionScore(cluster, similarityMatrix),
                        Recommendations = new List<OptimizationRecommendation>().AsReadOnly(),
                        Characteristics = new ClusterCharacteristics
                        {
                            CommonMethods = new HashSet<string>(),
                            Categories = new HashSet<Core.Models.TestCategory>(),
                            CommonTags = new HashSet<string>(),
                            CommonNamespaces = new HashSet<string>()
                        }
                    });
                }
            }
        }

        _logger.LogDebug("Hierarchical clustering completed. Found {ClusterCount} valid clusters", clusters.Count);

        return clusters.AsReadOnly();
    }

    /// <summary>
    /// Finds the pair of clusters with the highest similarity according to linkage criteria.
    /// </summary>
    private (int Cluster1Index, int Cluster2Index, double Similarity) FindMostSimilarClusters(
        List<List<int>> clusters,
        SimilarityMatrix similarityMatrix,
        LinkageCriteria linkageCriteria)
    {
        var maxSimilarity = -1.0;
        var bestCluster1 = -1;
        var bestCluster2 = -1;

        for (int i = 0; i < clusters.Count; i++)
        {
            for (int j = i + 1; j < clusters.Count; j++)
            {
                var similarity = CalculateClusterSimilarity(clusters[i], clusters[j], similarityMatrix, linkageCriteria);
                
                if (similarity > maxSimilarity)
                {
                    maxSimilarity = similarity;
                    bestCluster1 = i;
                    bestCluster2 = j;
                }
            }
        }

        return (bestCluster1, bestCluster2, maxSimilarity);
    }

    /// <summary>
    /// Calculates similarity between two clusters based on linkage criteria.
    /// </summary>
    private double CalculateClusterSimilarity(
        List<int> cluster1,
        List<int> cluster2,
        SimilarityMatrix similarityMatrix,
        LinkageCriteria linkageCriteria)
    {
        var similarities = new List<double>();

        foreach (var test1 in cluster1)
        {
            foreach (var test2 in cluster2)
            {
                similarities.Add(similarityMatrix.GetSimilarity(test1, test2));
            }
        }

        return linkageCriteria switch
        {
            LinkageCriteria.Single => similarities.Max(),     // Maximum similarity (single linkage)
            LinkageCriteria.Complete => similarities.Min(),   // Minimum similarity (complete linkage)
            LinkageCriteria.Average => similarities.Average(), // Average similarity
            LinkageCriteria.Ward => similarities.Average(),   // Simplified to average for now
            _ => similarities.Average()
        };
    }

    /// <summary>
    /// Calculates the average intra-cluster similarity.
    /// </summary>
    private double CalculateIntraClusterSimilarity(List<int> cluster, SimilarityMatrix similarityMatrix)
    {
        if (cluster.Count < 2) return 1.0;

        var similarities = new List<double>();
        for (int i = 0; i < cluster.Count; i++)
        {
            for (int j = i + 1; j < cluster.Count; j++)
            {
                similarities.Add(similarityMatrix.GetSimilarity(cluster[i], cluster[j]));
            }
        }

        return similarities.Any() ? similarities.Average() : 0.0;
    }

    /// <summary>
    /// Calculates the cohesion score for a cluster.
    /// </summary>
    private double CalculateCohesionScore(List<int> cluster, SimilarityMatrix similarityMatrix)
    {
        if (cluster.Count < 2) return 1.0;

        // Calculate both within-cluster similarity and separation from other clusters
        var withinClusterSimilarity = CalculateIntraClusterSimilarity(cluster, similarityMatrix);
        
        // For now, use within-cluster similarity as cohesion score
        // In a more sophisticated implementation, this could include separation metrics
        return withinClusterSimilarity;
    }

    /// <summary>
    /// Calculates clustering quality statistics.
    /// </summary>
    private ClusteringStatistics CalculateClusteringStatistics(
        IReadOnlyList<TestCluster> clusters,
        SimilarityMatrix similarityMatrix)
    {
        var totalTests = similarityMatrix.Size;
        var clusteredTests = clusters.Sum(c => c.Size);
        
        var avgIntraClusterSimilarity = clusters.Any() 
            ? clusters.Average(c => c.IntraClusterSimilarity) 
            : 0.0;

        // Calculate silhouette score (simplified version)
        var silhouetteScore = CalculateSilhouetteScore(clusters, similarityMatrix);

        return new ClusteringStatistics
        {
            TotalTests = totalTests,
            NumberOfClusters = clusters.Count,
            SilhouetteScore = silhouetteScore,
            AverageIntraClusterSimilarity = avgIntraClusterSimilarity,
            AverageInterClusterSimilarity = 0.0, // Could be calculated if needed
            ClusteringRate = totalTests > 0 ? (double)clusteredTests / totalTests : 0.0,
            LargestClusterSize = clusters.Any() ? clusters.Max(c => c.Size) : 0,
            SmallestClusterSize = clusters.Any() ? clusters.Min(c => c.Size) : 0,
            ClusterSizeVariance = CalculateClusterSizeVariance(clusters)
        };
    }

    /// <summary>
    /// Calculates a simplified silhouette score for cluster quality assessment.
    /// </summary>
    private double CalculateSilhouetteScore(IReadOnlyList<TestCluster> clusters, SimilarityMatrix similarityMatrix)
    {
        if (!clusters.Any()) return 0.0;

        var silhouetteValues = new List<double>();

        foreach (var cluster in clusters)
        {
            if (cluster.Size < 2) continue;

            // For each test in the cluster, calculate silhouette value
            foreach (var testId in cluster.TestIds)
            {
                var testIndex = similarityMatrix.TestIds.ToList().IndexOf(testId);
                if (testIndex == -1) continue;

                // Calculate average similarity within cluster (a)
                var withinClusterSimilarities = cluster.TestIds
                    .Where(id => id != testId)
                    .Select(id => similarityMatrix.GetSimilarity(testId, id))
                    .ToList();

                var a = withinClusterSimilarities.Any() ? withinClusterSimilarities.Average() : 0.0;

                // Calculate average similarity to nearest cluster (b)
                var b = double.MinValue;
                foreach (var otherCluster in clusters)
                {
                    if (otherCluster.ClusterId == cluster.ClusterId) continue;

                    var interClusterSimilarities = otherCluster.TestIds
                        .Select(id => similarityMatrix.GetSimilarity(testId, id))
                        .ToList();

                    if (interClusterSimilarities.Any())
                    {
                        var avgSimilarity = interClusterSimilarities.Average();
                        if (avgSimilarity > b)
                        {
                            b = avgSimilarity;
                        }
                    }
                }

                if (b != double.MinValue)
                {
                    var silhouette = (a - b) / Math.Max(a, b);
                    silhouetteValues.Add(silhouette);
                }
            }
        }

        return silhouetteValues.Any() ? silhouetteValues.Average() : 0.0;
    }

    /// <summary>
    /// Calculates variance in cluster sizes.
    /// </summary>
    private double CalculateClusterSizeVariance(IReadOnlyList<TestCluster> clusters)
    {
        if (!clusters.Any()) return 0.0;

        var sizes = clusters.Select(c => (double)c.Size).ToList();
        var mean = sizes.Average();
        var variance = sizes.Sum(size => Math.Pow(size - mean, 2)) / sizes.Count;
        
        return Math.Sqrt(variance);
    }

    /// <summary>
    /// Enriches clusters with characteristics and recommendations.
    /// </summary>
    private async Task<IReadOnlyList<TestCluster>> EnrichClustersWithCharacteristicsAsync(
        IReadOnlyList<TestCluster> clusters,
        string solutionPath,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Enriching {ClusterCount} clusters with characteristics and recommendations", clusters.Count);

        var enrichedClusters = new List<TestCluster>();

        foreach (var cluster in clusters)
        {
            try
            {
                var characteristics = await AnalyzeClusterCharacteristicsAsync(cluster, solutionPath, cancellationToken);
                var recommendations = GenerateClusterRecommendations(cluster, characteristics);

                var enrichedCluster = new TestCluster
                {
                    ClusterId = cluster.ClusterId,
                    TestIds = cluster.TestIds,
                    IntraClusterSimilarity = cluster.IntraClusterSimilarity,
                    CohesionScore = cluster.CohesionScore,
                    Recommendations = recommendations,
                    Characteristics = characteristics
                };

                enrichedClusters.Add(enrichedCluster);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enrich cluster {ClusterId}, using basic cluster", cluster.ClusterId);
                enrichedClusters.Add(cluster);
            }
        }

        return enrichedClusters.AsReadOnly();
    }

    /// <summary>
    /// Analyzes common characteristics within a cluster.
    /// </summary>
    private Task<ClusterCharacteristics> AnalyzeClusterCharacteristicsAsync(
        TestCluster cluster,
        string solutionPath,
        CancellationToken cancellationToken)
    {
        // For now, return basic characteristics
        // In a full implementation, this would analyze test metadata, namespaces, etc.
        
        var namespaces = cluster.TestIds
            .Select(ExtractNamespace)
            .Where(ns => !string.IsNullOrEmpty(ns))
            .ToHashSet();

        var suggestedName = GenerateClusterName(cluster, namespaces);

        return Task.FromResult(new ClusterCharacteristics
        {
            CommonMethods = new HashSet<string>(),
            Categories = new HashSet<Core.Models.TestCategory>(),
            CommonTags = new HashSet<string>(),
            CommonNamespaces = namespaces,
            SuggestedName = suggestedName,
            DominantPattern = InferDominantPattern(cluster.TestIds)
        });
    }

    /// <summary>
    /// Generates optimization recommendations for a cluster.
    /// </summary>
    private IReadOnlyList<OptimizationRecommendation> GenerateClusterRecommendations(
        TestCluster cluster,
        ClusterCharacteristics characteristics)
    {
        var recommendations = new List<OptimizationRecommendation>();

        // Generate recommendations based on cluster size and characteristics
        if (cluster.Size >= 5 && cluster.IntraClusterSimilarity >= 0.8)
        {
            recommendations.Add(new OptimizationRecommendation
            {
                Type = "TestOrganization",
                Description = $"Consider consolidating {cluster.Size} highly similar tests in cluster {cluster.ClusterId}",
                ConfidenceScore = cluster.IntraClusterSimilarity,
                EstimatedEffortLevel = cluster.Size <= 10 ? EstimatedEffortLevel.Low : EstimatedEffortLevel.Medium,
                Rationale = $"Cluster has high similarity ({cluster.IntraClusterSimilarity:F2}) with {cluster.Size} tests"
            });
        }

        return recommendations.AsReadOnly();
    }

    /// <summary>
    /// Finds tests that were not assigned to any cluster.
    /// </summary>
    private IReadOnlyList<string> FindUnclusteredTests(
        IReadOnlyList<string> allTestIds,
        IReadOnlyList<TestCluster> clusters)
    {
        var clusteredTests = clusters.SelectMany(c => c.TestIds).ToHashSet();
        return allTestIds.Where(testId => !clusteredTests.Contains(testId)).ToList().AsReadOnly();
    }

    #region Helper Methods

    private string ExtractNamespace(string testId)
    {
        var parts = testId.Split('.');
        return parts.Length > 2 ? string.Join(".", parts.Take(parts.Length - 2)) : string.Empty;
    }

    private string GenerateClusterName(TestCluster cluster, ISet<string> namespaces)
    {
        if (namespaces.Count == 1)
        {
            return $"{namespaces.First()}_Cluster_{cluster.ClusterId}";
        }
        
        return $"TestCluster_{cluster.ClusterId}";
    }

    private string InferDominantPattern(IReadOnlyList<string> testIds)
    {
        var patterns = new Dictionary<string, int>();

        foreach (var testId in testIds)
        {
            var parts = testId.Split('.');
            if (parts.Length >= 2)
            {
                var className = parts[^2];
                if (className.Contains("Test"))
                {
                    var pattern = className.Replace("Test", "").Replace("Tests", "");
                    patterns[pattern] = patterns.GetValueOrDefault(pattern, 0) + 1;
                }
            }
        }

        return patterns.Any() ? patterns.OrderByDescending(kvp => kvp.Value).First().Key : "Unknown";
    }

    #endregion
}