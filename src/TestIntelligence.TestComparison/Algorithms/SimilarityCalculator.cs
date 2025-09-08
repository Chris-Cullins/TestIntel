using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.TestComparison.Models;
using TestIntelligence.TestComparison.Services;

namespace TestIntelligence.TestComparison.Algorithms;

/// <summary>
/// Calculates various similarity metrics between test methods using weighted Jaccard similarity
/// and metadata-based comparison algorithms.
/// </summary>
public class SimilarityCalculator : ISimilarityCalculator
{
    private readonly ILogger<SimilarityCalculator> _logger;

    public SimilarityCalculator(ILogger<SimilarityCalculator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Calculates overlap between method coverage sets using weighted Jaccard similarity.
    /// Accounts for call depth decay, method complexity weighting, and production vs framework code.
    /// </summary>
    public double CalculateCoverageOverlap(
        IReadOnlySet<string> methods1, 
        IReadOnlySet<string> methods2, 
        WeightingOptions? options = null)
    {
        if (methods1 == null) throw new ArgumentNullException(nameof(methods1));
        if (methods2 == null) throw new ArgumentNullException(nameof(methods2));

        options ??= new WeightingOptions();

        _logger.LogDebug("Calculating coverage overlap between {Methods1Count} and {Methods2Count} methods", 
            methods1.Count, methods2.Count);

        // Handle empty sets
        if (methods1.Count == 0 && methods2.Count == 0)
        {
            _logger.LogDebug("Both method sets are empty, returning similarity of 1.0");
            return 1.0;
        }

        if (methods1.Count == 0 || methods2.Count == 0)
        {
            _logger.LogDebug("One method set is empty, returning similarity of 0.0");
            return 0.0;
        }

        // Calculate intersection and union
        var intersection = methods1.Intersect(methods2).ToHashSet();
        var union = methods1.Union(methods2).ToHashSet();

        if (union.Count == 0)
        {
            _logger.LogDebug("Union is empty, returning similarity of 0.0");
            return 0.0;
        }

        // Apply weighting if enabled
        if (options.UseComplexityWeighting || 
            Math.Abs(options.CallDepthDecayFactor - 1.0) > 0.001 ||
            Math.Abs(options.ProductionCodeWeight - options.FrameworkCodeWeight) > 0.001)
        {
            return CalculateWeightedJaccardSimilarity(methods1, methods2, intersection, union, options);
        }

        // Simple Jaccard similarity
        var jaccardSimilarity = (double)intersection.Count / union.Count;
        
        _logger.LogDebug("Calculated simple Jaccard similarity: {Similarity:F3} " +
            "({IntersectionCount} shared / {UnionCount} total)", 
            jaccardSimilarity, intersection.Count, union.Count);

        return jaccardSimilarity;
    }

    /// <summary>
    /// Calculates metadata-based similarity between test info objects.
    /// Considers category alignment, naming patterns, and tag overlap.
    /// </summary>
    public double CalculateMetadataSimilarity(TestInfo test1, TestInfo test2)
    {
        if (test1 == null) throw new ArgumentNullException(nameof(test1));
        if (test2 == null) throw new ArgumentNullException(nameof(test2));

        _logger.LogDebug("Calculating metadata similarity between {Test1} and {Test2}", 
            test1.GetUniqueId(), test2.GetUniqueId());

        var similarityScores = new List<double>();

        // Category alignment (30% weight)
        var categoryScore = CalculateCategorySimilarity(test1, test2);
        similarityScores.Add(categoryScore * 0.3);

        // Naming pattern similarity (40% weight)
        var namingScore = CalculateNamingSimilarity(test1, test2);
        similarityScores.Add(namingScore * 0.4);

        // Tag overlap (20% weight)
        var tagScore = CalculateTagSimilarity(test1, test2);
        similarityScores.Add(tagScore * 0.2);

        // Execution time similarity (10% weight)
        var executionTimeScore = CalculateExecutionTimeSimilarity(test1, test2);
        similarityScores.Add(executionTimeScore * 0.1);

        var overallSimilarity = similarityScores.Sum();

        _logger.LogDebug("Calculated metadata similarity: {Similarity:F3} " +
            "(Category: {CategoryScore:F3}, Naming: {NamingScore:F3}, " +
            "Tags: {TagScore:F3}, ExecutionTime: {ExecutionTimeScore:F3})",
            overallSimilarity, categoryScore, namingScore, tagScore, executionTimeScore);

        return Math.Max(0.0, Math.Min(1.0, overallSimilarity));
    }

    private double CalculateWeightedJaccardSimilarity(
        IReadOnlySet<string> methods1,
        IReadOnlySet<string> methods2,
        ISet<string> intersection,
        ISet<string> union,
        WeightingOptions options)
    {
        var weights = new Dictionary<string, double>();
        
        // Calculate weights for all methods in union
        foreach (var method in union)
        {
            var weight = CalculateMethodWeight(method, options);
            weights[method] = weight;
        }

        // Calculate weighted intersection and union
        var weightedIntersection = intersection.Sum(method => weights[method]);
        var weightedUnion = union.Sum(method => weights[method]);

        if (weightedUnion <= 0)
        {
            _logger.LogDebug("Weighted union is zero or negative, returning similarity of 0.0");
            return 0.0;
        }

        var weightedSimilarity = weightedIntersection / weightedUnion;

        _logger.LogDebug("Calculated weighted Jaccard similarity: {Similarity:F3} " +
            "({WeightedIntersection:F3} / {WeightedUnion:F3})",
            weightedSimilarity, weightedIntersection, weightedUnion);

        return weightedSimilarity;
    }

    private double CalculateMethodWeight(string methodName, WeightingOptions options)
    {
        var weight = 1.0;

        // Apply production vs framework code weighting
        if (IsFrameworkMethod(methodName))
        {
            weight *= options.FrameworkCodeWeight;
        }
        else
        {
            weight *= options.ProductionCodeWeight;
        }

        // Apply complexity weighting (simplified heuristic based on method characteristics)
        if (options.UseComplexityWeighting)
        {
            weight *= CalculateComplexityWeight(methodName);
        }

        // Note: Call depth decay would be applied if we had access to call depth information
        // For now, we assume all methods are at the same depth level

        return Math.Max(0.0, weight);
    }

    private bool IsFrameworkMethod(string methodName)
    {
        // Simple heuristic to identify framework methods
        var frameworkPatterns = new[]
        {
            "System.",
            "Microsoft.",
            "NUnit.",
            "Xunit.",
            "Moq.",
            "AutoFixture.",
            "FluentAssertions."
        };

        return frameworkPatterns.Any(pattern => methodName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private double CalculateComplexityWeight(string methodName)
    {
        // Simple heuristic based on method name characteristics
        var weight = 1.0;

        // Generic methods might be more complex
        if (methodName.Contains("<") && methodName.Contains(">"))
        {
            weight *= 1.2;
        }

        // Constructor methods
        if (methodName.Contains(".ctor") || methodName.Contains(".cctor"))
        {
            weight *= 0.8;
        }

        // Property getters/setters are typically simpler
        if (methodName.Contains("get_") || methodName.Contains("set_"))
        {
            weight *= 0.6;
        }

        return Math.Max(0.1, Math.Min(2.0, weight)); // Clamp between 0.1 and 2.0
    }

    private double CalculateCategorySimilarity(TestInfo test1, TestInfo test2)
    {
        if (test1.Category == test2.Category)
        {
            return 1.0;
        }

        // Partial similarity for related categories
        var categoryRelationships = new Dictionary<(Core.Models.TestCategory, Core.Models.TestCategory), double>
        {
            { (Core.Models.TestCategory.Unit, Core.Models.TestCategory.Integration), 0.3 },
            { (Core.Models.TestCategory.Integration, Core.Models.TestCategory.Unit), 0.3 },
            { (Core.Models.TestCategory.Integration, Core.Models.TestCategory.EndToEnd), 0.4 },
            { (Core.Models.TestCategory.EndToEnd, Core.Models.TestCategory.Integration), 0.4 },
            { (Core.Models.TestCategory.Database, Core.Models.TestCategory.Integration), 0.5 },
            { (Core.Models.TestCategory.Integration, Core.Models.TestCategory.Database), 0.5 }
        };

        return categoryRelationships.TryGetValue((test1.Category, test2.Category), out var similarity) ? similarity : 0.0;
    }

    private double CalculateNamingSimilarity(TestInfo test1, TestInfo test2)
    {
        var name1 = test1.TestMethod.MethodName;
        var name2 = test2.TestMethod.MethodName;

        if (string.IsNullOrEmpty(name1) || string.IsNullOrEmpty(name2))
        {
            return 0.0;
        }

        // Extract meaningful parts from test names
        var parts1 = ExtractNameParts(name1);
        var parts2 = ExtractNameParts(name2);

        if (parts1.Count == 0 && parts2.Count == 0)
        {
            return 1.0;
        }

        if (parts1.Count == 0 || parts2.Count == 0)
        {
            return 0.0;
        }

        // Calculate Jaccard similarity of name parts
        var intersection = parts1.Intersect(parts2, StringComparer.OrdinalIgnoreCase).Count();
        var union = parts1.Union(parts2, StringComparer.OrdinalIgnoreCase).Count();

        return union > 0 ? (double)intersection / union : 0.0;
    }

    private HashSet<string> ExtractNameParts(string name)
    {
        var parts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Split on common delimiters
        var delimiters = new[] { '_', '-', '.', ' ' };
        var segments = name.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            if (segment.Length > 2) // Ignore very short parts
            {
                parts.Add(segment.ToLowerInvariant());
            }
        }

        // Extract camelCase parts
        var camelParts = ExtractCamelCaseParts(name);
        foreach (var part in camelParts)
        {
            if (part.Length > 2)
            {
                parts.Add(part.ToLowerInvariant());
            }
        }

        return parts;
    }

    private IEnumerable<string> ExtractCamelCaseParts(string name)
    {
        var parts = new List<string>();
        var currentPart = "";

        foreach (var c in name)
        {
            if (char.IsUpper(c) && currentPart.Length > 0)
            {
                if (currentPart.Length > 2)
                {
                    parts.Add(currentPart);
                }
                currentPart = c.ToString();
            }
            else
            {
                currentPart += c;
            }
        }

        if (currentPart.Length > 2)
        {
            parts.Add(currentPart);
        }

        return parts;
    }

    private double CalculateTagSimilarity(TestInfo test1, TestInfo test2)
    {
        var tags1 = test1.Tags;
        var tags2 = test2.Tags;

        if (tags1.Count == 0 && tags2.Count == 0)
        {
            return 1.0; // Both have no tags - considered similar
        }

        if (tags1.Count == 0 || tags2.Count == 0)
        {
            return 0.5; // One has tags, one doesn't - partial similarity
        }

        // Jaccard similarity of tag sets
        var intersection = tags1.Intersect(tags2, StringComparer.OrdinalIgnoreCase).Count();
        var union = tags1.Union(tags2, StringComparer.OrdinalIgnoreCase).Count();

        return union > 0 ? (double)intersection / union : 0.0;
    }

    private double CalculateExecutionTimeSimilarity(TestInfo test1, TestInfo test2)
    {
        var time1 = test1.AverageExecutionTime.TotalMilliseconds;
        var time2 = test2.AverageExecutionTime.TotalMilliseconds;

        // If both are zero or very small, consider them similar
        if (time1 < 1 && time2 < 1)
        {
            return 1.0;
        }

        if (time1 <= 0 || time2 <= 0)
        {
            return 0.0;
        }

        // Calculate similarity based on execution time ratio
        var ratio = Math.Min(time1, time2) / Math.Max(time1, time2);
        
        // Apply a curve that gives high similarity for close times
        // and lower similarity for very different times
        return Math.Pow(ratio, 0.5);
    }
}