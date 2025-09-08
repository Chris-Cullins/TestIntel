using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using TestIntelligence.TestComparison.Models;

namespace TestIntelligence.TestComparison.Algorithms;

/// <summary>
/// Generates optimization recommendations based on test comparison results.
/// Provides logic for merge, extract common, maintain separate recommendations
/// with confidence scores and effort level estimations.
/// </summary>
public class OptimizationRecommendationEngine
{
    private readonly ILogger<OptimizationRecommendationEngine> _logger;

    public OptimizationRecommendationEngine(ILogger<OptimizationRecommendationEngine> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates a list of optimization recommendations based on comparison results.
    /// </summary>
    /// <param name="comparisonResult">The test comparison result to analyze</param>
    /// <returns>Ordered list of recommendations by confidence score</returns>
    public IReadOnlyList<OptimizationRecommendation> GenerateRecommendations(
        TestComparisonResult comparisonResult)
    {
        if (comparisonResult == null) throw new ArgumentNullException(nameof(comparisonResult));

        _logger.LogDebug("Generating recommendations for tests {Test1} and {Test2} with {OverallSimilarity:F3} similarity",
            comparisonResult.Test1Id, comparisonResult.Test2Id, comparisonResult.OverallSimilarity);

        var recommendations = new List<OptimizationRecommendation>();

        // Generate different types of recommendations based on similarity levels
        AddMergeRecommendations(recommendations, comparisonResult);
        AddExtractCommonRecommendations(recommendations, comparisonResult);
        AddMaintainSeparateRecommendations(recommendations, comparisonResult);
        AddRefactoringRecommendations(recommendations, comparisonResult);

        // Filter by minimum confidence threshold
        var filteredRecommendations = recommendations
            .Where(r => r.ConfidenceScore >= comparisonResult.Options.MinimumConfidenceThreshold)
            .OrderByDescending(r => r.ConfidenceScore)
            .ToList();

        _logger.LogDebug("Generated {TotalCount} recommendations, {FilteredCount} above confidence threshold of {Threshold:F2}",
            recommendations.Count, filteredRecommendations.Count, comparisonResult.Options.MinimumConfidenceThreshold);

        return filteredRecommendations.AsReadOnly();
    }

    private void AddMergeRecommendations(List<OptimizationRecommendation> recommendations, TestComparisonResult result)
    {
        var overlapPercentage = result.CoverageOverlap.OverlapPercentage;
        var metadataSimilarity = result.MetadataSimilarity.OverallScore;

        // High overlap suggests merge opportunity
        if (overlapPercentage >= 70.0 && metadataSimilarity >= 0.6)
        {
            var confidence = CalculateMergeConfidence(result);
            var effort = EstimateMergeEffort(result);

            recommendations.Add(new OptimizationRecommendation
            {
                Type = "merge",
                Description = $"These tests have {overlapPercentage:F1}% coverage overlap and similar metadata. " +
                             "Consider merging them into a single comprehensive test to reduce redundancy.",
                ConfidenceScore = confidence,
                EstimatedEffortLevel = effort,
                Rationale = GenerateMergeRationale(result),
                ImpactDescription = "Reduced test execution time, simplified test maintenance, elimination of redundant coverage",
                RisksAndConsiderations = "Loss of specific test scenarios, increased test complexity, potential for reduced test clarity"
            });
        }
        else if (overlapPercentage >= 50.0 && metadataSimilarity >= 0.4)
        {
            // Partial merge suggestion for moderate overlap
            var confidence = CalculateMergeConfidence(result) * 0.7; // Reduce confidence for partial merge
            var effort = EstimateMergeEffort(result);

            recommendations.Add(new OptimizationRecommendation
            {
                Type = "partial_merge",
                Description = $"These tests have {overlapPercentage:F1}% coverage overlap. " +
                             "Consider merging common scenarios while maintaining unique test cases.",
                ConfidenceScore = confidence,
                EstimatedEffortLevel = effort,
                Rationale = GeneratePartialMergeRationale(result),
                ImpactDescription = "Reduced redundancy in common scenarios, maintained specific test coverage, improved test organization",
                RisksAndConsiderations = "Increased complexity in test design, potential for incomplete coverage"
            });
        }
    }

    private void AddExtractCommonRecommendations(List<OptimizationRecommendation> recommendations, TestComparisonResult result)
    {
        var overlapPercentage = result.CoverageOverlap.OverlapPercentage;

        // Moderate overlap suggests extracting common functionality
        if (overlapPercentage >= 30.0 && overlapPercentage < 70.0)
        {
            var confidence = CalculateExtractCommonConfidence(result);
            var effort = EstimateExtractCommonEffort(result);

            recommendations.Add(new OptimizationRecommendation
            {
                Type = "extract_common",
                Description = $"These tests share {overlapPercentage:F1}% of their coverage. " +
                             "Consider extracting common setup, helper methods, or base test classes.",
                ConfidenceScore = confidence,
                EstimatedEffortLevel = effort,
                Rationale = GenerateExtractCommonRationale(result),
                ImpactDescription = "Reduced code duplication, improved test maintainability, consistent test patterns, easier test updates",
                RisksAndConsiderations = "Increased abstraction complexity, potential coupling between tests"
            });
        }
    }

    private void AddMaintainSeparateRecommendations(List<OptimizationRecommendation> recommendations, TestComparisonResult result)
    {
        var overlapPercentage = result.CoverageOverlap.OverlapPercentage;
        var metadataSimilarity = result.MetadataSimilarity.OverallScore;

        // Low overlap suggests maintaining separate tests
        if (overlapPercentage < 30.0 || metadataSimilarity < 0.3)
        {
            var confidence = CalculateMaintainSeparateConfidence(result);

            recommendations.Add(new OptimizationRecommendation
            {
                Type = "maintain_separate",
                Description = $"These tests have only {overlapPercentage:F1}% coverage overlap and serve different purposes. " +
                             "They should remain as separate, focused test cases.",
                ConfidenceScore = confidence,
                EstimatedEffortLevel = EstimatedEffortLevel.Low,
                Rationale = GenerateMaintainSeparateRationale(result),
                ImpactDescription = "Clear test separation of concerns, focused test coverage, independent test execution, easier debugging and maintenance",
                RisksAndConsiderations = "Potential for minor code duplication, multiple tests to maintain"
            });
        }
    }

    private void AddRefactoringRecommendations(List<OptimizationRecommendation> recommendations, TestComparisonResult result)
    {
        // Check for naming inconsistencies
        if (result.MetadataSimilarity.NamingPatternScore < 0.3)
        {
            recommendations.Add(new OptimizationRecommendation
            {
                Type = "improve_naming",
                Description = "These tests have inconsistent naming patterns. Consider standardizing test names " +
                             "to improve readability and maintainability.",
                ConfidenceScore = 0.8,
                EstimatedEffortLevel = EstimatedEffortLevel.Low,
                Rationale = "Consistent naming patterns improve test discoverability and maintenance.",
                ImpactDescription = "Improved test discoverability, consistent naming conventions, better test organization",
                RisksAndConsiderations = "Minimal risks associated with renaming"
            });
        }
    }

    private double CalculateMergeConfidence(TestComparisonResult result)
    {
        var factors = new[]
        {
            result.CoverageOverlap.OverlapPercentage / 100.0,
            result.MetadataSimilarity.OverallScore,
            result.MetadataSimilarity.CategoryAlignmentScore,
            result.MetadataSimilarity.NamingPatternScore
        };

        // Weighted average with emphasis on coverage overlap
        var weights = new[] { 0.4, 0.3, 0.2, 0.1 };
        var weightedScore = factors.Zip(weights, (f, w) => f * w).Sum();

        // Apply confidence penalty for very different execution times  
        if (result.MetadataSimilarity.ExecutionTimeSimilarity < 0.5)
        {
            weightedScore *= 0.9;
        }

        return Math.Max(0.0, Math.Min(1.0, weightedScore));
    }

    private double CalculateExtractCommonConfidence(TestComparisonResult result)
    {
        var overlapNormalized = result.CoverageOverlap.OverlapPercentage / 100.0;
        
        // Higher confidence for moderate overlap (sweet spot for extraction)
        var confidenceCurve = 4 * overlapNormalized * (1 - overlapNormalized); // Peak at 0.5
        
        return Math.Max(0.0, Math.Min(1.0, confidenceCurve));
    }

    private double CalculateMaintainSeparateConfidence(TestComparisonResult result)
    {
        var overlapNormalized = result.CoverageOverlap.OverlapPercentage / 100.0;
        var metadataSimilarity = result.MetadataSimilarity.OverallScore;

        // High confidence when overlap is low and metadata is different
        var separationScore = (1 - overlapNormalized) * (1 - metadataSimilarity);
        
        return Math.Max(0.0, Math.Min(1.0, separationScore));
    }

    private EstimatedEffortLevel EstimateMergeEffort(TestComparisonResult result)
    {
        var sharedMethods = result.CoverageOverlap.SharedProductionMethods;
        var uniqueMethods = result.CoverageOverlap.UniqueToTest1 + result.CoverageOverlap.UniqueToTest2;
        var totalMethods = sharedMethods + uniqueMethods;

        // Base effort on method count and complexity
        if (totalMethods < 10 && result.MetadataSimilarity.CategoryAlignmentScore > 0.8)
            return EstimatedEffortLevel.Low;
        
        if (totalMethods < 25 && result.MetadataSimilarity.OverallScore > 0.6)
            return EstimatedEffortLevel.Medium;
        
        return EstimatedEffortLevel.High;
    }

    private EstimatedEffortLevel EstimateExtractCommonEffort(TestComparisonResult result)
    {
        var sharedMethods = result.CoverageOverlap.SharedProductionMethods;
        
        // Effort based on amount of shared functionality
        if (sharedMethods < 5)
            return EstimatedEffortLevel.Low;
        
        if (sharedMethods < 15)
            return EstimatedEffortLevel.Medium;
        
        return EstimatedEffortLevel.High;
    }

    private string GenerateMergeRationale(TestComparisonResult result)
    {
        return $"High coverage overlap ({result.CoverageOverlap.OverlapPercentage:F1}%) and metadata similarity " +
               $"({result.MetadataSimilarity.OverallScore:F2}) indicate these tests are testing very similar scenarios. " +
               $"Merging them would eliminate redundancy while maintaining test coverage.";
    }

    private string GeneratePartialMergeRationale(TestComparisonResult result)
    {
        return $"Moderate coverage overlap ({result.CoverageOverlap.OverlapPercentage:F1}%) suggests partial redundancy. " +
               $"Common scenarios can be merged while preserving unique test cases for complete coverage.";
    }

    private string GenerateExtractCommonRationale(TestComparisonResult result)
    {
        return $"Moderate coverage overlap ({result.CoverageOverlap.OverlapPercentage:F1}%) with " +
               $"{result.CoverageOverlap.SharedProductionMethods} shared methods indicates common setup patterns. " +
               $"Extracting common utilities would reduce duplication without losing test focus.";
    }

    private string GenerateMaintainSeparateRationale(TestComparisonResult result)
    {
        return $"Low coverage overlap ({result.CoverageOverlap.OverlapPercentage:F1}%) and different metadata " +
               $"indicate these tests serve distinct purposes. Maintaining separation ensures focused, " +
               $"easy-to-understand test coverage.";
    }
}