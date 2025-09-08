using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Discovery;
using TestIntelligence.Core.Services;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.TestComparison.Algorithms;
using TestIntelligence.TestComparison.Models;

namespace TestIntelligence.TestComparison.Services;

/// <summary>
/// Main service implementation for comparing tests and analyzing overlap between test methods.
/// Coordinates all comparison operations and integrates with similarity calculators and coverage analysis.
/// </summary>
public class TestComparisonService : ITestComparisonService
{
    private readonly TestCoverageComparisonService _coverageComparisonService;
    private readonly ISimilarityCalculator _similarityCalculator;
    private readonly OptimizationRecommendationEngine _recommendationEngine;
    private readonly ILogger<TestComparisonService> _logger;

    // Dependencies that might need to be injected for test info retrieval
    private readonly ITestDiscovery? _testDiscovery;

    public TestComparisonService(
        TestCoverageComparisonService coverageComparisonService,
        ISimilarityCalculator similarityCalculator,
        OptimizationRecommendationEngine recommendationEngine,
        ILogger<TestComparisonService> logger,
        ITestDiscovery? testDiscovery = null)
    {
        _coverageComparisonService = coverageComparisonService ?? throw new ArgumentNullException(nameof(coverageComparisonService));
        _similarityCalculator = similarityCalculator ?? throw new ArgumentNullException(nameof(similarityCalculator));
        _recommendationEngine = recommendationEngine ?? throw new ArgumentNullException(nameof(recommendationEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _testDiscovery = testDiscovery; // Optional for test info enrichment
    }

    /// <summary>
    /// Compares two test methods and generates detailed overlap analysis.
    /// </summary>
    /// <param name="test1Id">Full identifier of first test method</param>
    /// <param name="test2Id">Full identifier of second test method</param>
    /// <param name="solutionPath">Path to solution file</param>
    /// <param name="options">Comparison configuration options</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>Detailed comparison results</returns>
    public async Task<TestComparisonResult> CompareTestsAsync(
        string test1Id, 
        string test2Id, 
        string solutionPath, 
        ComparisonOptions options, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(test1Id)) throw new ArgumentException("Test 1 ID cannot be null or empty", nameof(test1Id));
        if (string.IsNullOrEmpty(test2Id)) throw new ArgumentException("Test 2 ID cannot be null or empty", nameof(test2Id));
        if (string.IsNullOrEmpty(solutionPath)) throw new ArgumentException("Solution path cannot be null or empty", nameof(solutionPath));
        if (options == null) throw new ArgumentNullException(nameof(options));

        _logger.LogInformation("Starting comparison between tests {Test1Id} and {Test2Id} with {Depth} analysis depth", 
            test1Id, test2Id, options.Depth);

        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<string>();

        try
        {
            // Step 1: Analyze coverage overlap
            var coverageOverlap = await AnalyzeCoverageOverlapAsync(test1Id, test2Id, solutionPath, options, cancellationToken);

            // Step 2: Calculate metadata similarity (if analysis depth allows)
            var metadataSimilarity = await CalculateMetadataSimilarityAsync(test1Id, test2Id, solutionPath, options, cancellationToken);

            // Step 3: Calculate overall similarity score
            var overallSimilarity = CalculateOverallSimilarity(coverageOverlap, metadataSimilarity, options);

            // Step 4: Create preliminary comparison result
            var preliminaryResult = new TestComparisonResult
            {
                Test1Id = test1Id,
                Test2Id = test2Id,
                OverallSimilarity = overallSimilarity,
                CoverageOverlap = coverageOverlap,
                MetadataSimilarity = metadataSimilarity,
                Recommendations = new List<OptimizationRecommendation>().AsReadOnly(),
                AnalysisTimestamp = DateTime.UtcNow,
                Options = options,
                AnalysisDuration = stopwatch.Elapsed,
                Warnings = warnings.Count > 0 ? warnings.AsReadOnly() : null
            };

            // Step 5: Generate optimization recommendations
            var recommendations = _recommendationEngine.GenerateRecommendations(preliminaryResult);

            // Step 6: Create final result with recommendations
            var finalResult = new TestComparisonResult
            {
                Test1Id = test1Id,
                Test2Id = test2Id,
                OverallSimilarity = overallSimilarity,
                CoverageOverlap = coverageOverlap,
                MetadataSimilarity = metadataSimilarity,
                Recommendations = recommendations,
                AnalysisTimestamp = DateTime.UtcNow,
                Options = options,
                AnalysisDuration = stopwatch.Elapsed,
                Warnings = warnings.Count > 0 ? warnings.AsReadOnly() : null
            };

            _logger.LogInformation("Test comparison completed in {Duration:F2}s. Overall similarity: {Similarity:F3}, " +
                "Coverage overlap: {CoverageOverlap:F1}%, {RecommendationCount} recommendations generated",
                stopwatch.Elapsed.TotalSeconds, overallSimilarity, coverageOverlap.OverlapPercentage, recommendations.Count);

            return finalResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compare tests {Test1Id} and {Test2Id} after {Duration:F2}s", 
                test1Id, test2Id, stopwatch.Elapsed.TotalSeconds);
            throw;
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    /// <summary>
    /// Analyzes coverage overlap between two tests.
    /// </summary>
    private async Task<CoverageOverlapAnalysis> AnalyzeCoverageOverlapAsync(
        string test1Id, 
        string test2Id, 
        string solutionPath, 
        ComparisonOptions options, 
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Analyzing coverage overlap between {Test1Id} and {Test2Id}", test1Id, test2Id);

        try
        {
            return await _coverageComparisonService.AnalyzeCoverageOverlapAsync(
                test1Id, test2Id, solutionPath, options.Weighting, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze coverage overlap between {Test1Id} and {Test2Id}", test1Id, test2Id);
            
            // Return empty analysis instead of failing completely
            return new CoverageOverlapAnalysis
            {
                SharedProductionMethods = 0,
                UniqueToTest1 = 0,
                UniqueToTest2 = 0,
                OverlapPercentage = 0.0,
                SharedMethods = new List<SharedMethodInfo>().AsReadOnly(),
                UniqueMethodsTest1 = new List<string>().AsReadOnly(),
                UniqueMethodsTest2 = new List<string>().AsReadOnly()
            };
        }
    }

    /// <summary>
    /// Calculates metadata-based similarity between tests.
    /// </summary>
    private async Task<MetadataSimilarity> CalculateMetadataSimilarityAsync(
        string test1Id, 
        string test2Id, 
        string solutionPath, 
        ComparisonOptions options, 
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Calculating metadata similarity between {Test1Id} and {Test2Id}", test1Id, test2Id);

        try
        {
            // Try to get detailed test information if discovery service is available
            TestInfo? test1Info = null;
            TestInfo? test2Info = null;

            if (_testDiscovery != null && options.Depth != AnalysisDepth.Shallow)
            {
                try
                {
                    test1Info = await GetTestInfoAsync(test1Id, solutionPath, cancellationToken);
                    test2Info = await GetTestInfoAsync(test2Id, solutionPath, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not retrieve detailed test information, using basic analysis");
                }
            }

            if (test1Info != null && test2Info != null)
            {
                // Use detailed metadata similarity calculation
                var overallScore = _similarityCalculator.CalculateMetadataSimilarity(test1Info, test2Info);
                
                return new MetadataSimilarity
                {
                    OverallScore = overallScore,
                    CategoryAlignmentScore = CalculateCategoryAlignment(test1Info, test2Info),
                    NamingPatternScore = CalculateNamingPatternSimilarity(test1Info, test2Info),
                    TagOverlapScore = CalculateTagOverlapScore(test1Info, test2Info),
                    SharedTags = GetSharedTags(test1Info, test2Info),
                    UniqueToTest1 = GetUniqueTagsToTest1(test1Info, test2Info),
                    UniqueToTest2 = GetUniqueTagsToTest2(test1Info, test2Info),
                    ExecutionTimeSimilarity = CalculateExecutionTimeSimilarity(test1Info, test2Info)
                };
            }
            else
            {
                // Use basic name-based similarity calculation
                return CalculateBasicMetadataSimilarity(test1Id, test2Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate metadata similarity between {Test1Id} and {Test2Id}", test1Id, test2Id);
            
            // Return basic similarity based on names
            return CalculateBasicMetadataSimilarity(test1Id, test2Id);
        }
    }

    /// <summary>
    /// Attempts to retrieve detailed test information for a given test ID.
    /// </summary>
    private Task<TestInfo?> GetTestInfoAsync(string testId, string solutionPath, CancellationToken cancellationToken)
    {
        if (_testDiscovery == null)
            return Task.FromResult<TestInfo?>(null);

        try
        {
            // This would require a method to get specific test info by ID
            // Since this might not exist in the current interface, we'll return null for now
            // In a real implementation, this might involve:
            // 1. Discovering all tests
            // 2. Finding the specific test by ID
            // 3. Enriching with selection engine metadata
            return Task.FromResult<TestInfo?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve test info for {TestId}", testId);
            return Task.FromResult<TestInfo?>(null);
        }
    }

    /// <summary>
    /// Calculates basic metadata similarity based only on test names.
    /// </summary>
    private MetadataSimilarity CalculateBasicMetadataSimilarity(string test1Id, string test2Id)
    {
        var namingSimilarity = CalculateBasicNamingSimilarity(test1Id, test2Id);
        
        return new MetadataSimilarity
        {
            OverallScore = namingSimilarity,
            CategoryAlignmentScore = 0.5, // Unknown categories, assume moderate alignment
            NamingPatternScore = namingSimilarity,
            TagOverlapScore = 0.0, // No tag information available
            SharedTags = new List<string>().AsReadOnly(),
            UniqueToTest1 = new List<string>().AsReadOnly(),
            UniqueToTest2 = new List<string>().AsReadOnly(),
            ExecutionTimeSimilarity = 0.5 // Unknown execution times
        };
    }

    /// <summary>
    /// Calculates basic naming similarity between two test IDs.
    /// </summary>
    private double CalculateBasicNamingSimilarity(string test1Id, string test2Id)
    {
        var name1Parts = ExtractNameParts(test1Id);
        var name2Parts = ExtractNameParts(test2Id);

        if (name1Parts.Count == 0 && name2Parts.Count == 0)
            return 1.0;

        if (name1Parts.Count == 0 || name2Parts.Count == 0)
            return 0.0;

        var intersection = name1Parts.Intersect(name2Parts, StringComparer.OrdinalIgnoreCase).Count();
        var union = name1Parts.Union(name2Parts, StringComparer.OrdinalIgnoreCase).Count();

        return union > 0 ? (double)intersection / union : 0.0;
    }

    private HashSet<string> ExtractNameParts(string name)
    {
        var parts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Split on common delimiters
        var delimiters = new[] { '.', '_', '-', ' ' };
        var segments = name.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            if (segment.Length > 2) // Ignore very short parts
            {
                parts.Add(segment);
            }
        }

        return parts;
    }

    /// <summary>
    /// Calculates overall similarity score combining coverage and metadata factors.
    /// </summary>
    private double CalculateOverallSimilarity(
        CoverageOverlapAnalysis coverageOverlap, 
        MetadataSimilarity metadataSimilarity, 
        ComparisonOptions options)
    {
        // Weight factors based on analysis depth
        var (coverageWeight, metadataWeight) = options.Depth switch
        {
            AnalysisDepth.Shallow => (1.0, 0.0),     // Coverage only
            AnalysisDepth.Medium => (0.7, 0.3),      // Mostly coverage with some metadata
            AnalysisDepth.Deep => (0.6, 0.4),        // Balanced coverage and metadata
            _ => (0.7, 0.3)
        };

        var coverageScore = coverageOverlap.OverlapPercentage / 100.0;
        var metadataScore = metadataSimilarity.OverallScore;

        var overallSimilarity = (coverageScore * coverageWeight) + (metadataScore * metadataWeight);
        
        return Math.Max(0.0, Math.Min(1.0, overallSimilarity));
    }

    #region Metadata Similarity Helper Methods

    private double CalculateCategoryAlignment(TestInfo test1, TestInfo test2)
    {
        return test1.Category == test2.Category ? 1.0 : 0.0;
    }

    private double CalculateNamingPatternSimilarity(TestInfo test1, TestInfo test2)
    {
        return CalculateBasicNamingSimilarity(test1.GetUniqueId(), test2.GetUniqueId());
    }

    private double CalculateTagOverlapScore(TestInfo test1, TestInfo test2)
    {
        if (test1.Tags.Count == 0 && test2.Tags.Count == 0)
            return 1.0;

        if (test1.Tags.Count == 0 || test2.Tags.Count == 0)
            return 0.0;

        var intersection = test1.Tags.Intersect(test2.Tags, StringComparer.OrdinalIgnoreCase).Count();
        var union = test1.Tags.Union(test2.Tags, StringComparer.OrdinalIgnoreCase).Count();

        return union > 0 ? (double)intersection / union : 0.0;
    }

    private IReadOnlyList<string> GetSharedTags(TestInfo test1, TestInfo test2)
    {
        return test1.Tags.Intersect(test2.Tags, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
    }

    private IReadOnlyList<string> GetUniqueTagsToTest1(TestInfo test1, TestInfo test2)
    {
        return test1.Tags.Except(test2.Tags, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
    }

    private IReadOnlyList<string> GetUniqueTagsToTest2(TestInfo test1, TestInfo test2)
    {
        return test2.Tags.Except(test1.Tags, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
    }

    private double CalculateExecutionTimeSimilarity(TestInfo test1, TestInfo test2)
    {
        var time1 = test1.AverageExecutionTime.TotalMilliseconds;
        var time2 = test2.AverageExecutionTime.TotalMilliseconds;

        if (time1 <= 0 && time2 <= 0)
            return 1.0;

        if (time1 <= 0 || time2 <= 0)
            return 0.0;

        var ratio = Math.Min(time1, time2) / Math.Max(time1, time2);
        return Math.Pow(ratio, 0.5); // Apply curve for better similarity distribution
    }

    #endregion
}