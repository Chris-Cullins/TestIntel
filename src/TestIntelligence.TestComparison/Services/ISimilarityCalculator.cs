using System.Collections.Generic;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.TestComparison.Models;

namespace TestIntelligence.TestComparison.Services;

/// <summary>
/// Calculates various similarity metrics between test methods.
/// </summary>
public interface ISimilarityCalculator
{
    /// <summary>
    /// Calculates overlap between method coverage sets using Jaccard similarity.
    /// </summary>
    /// <param name="methods1">Production method names covered by first test</param>
    /// <param name="methods2">Production method names covered by second test</param>
    /// <param name="options">Optional weighting options for advanced scoring</param>
    /// <returns>Similarity score from 0.0 (no overlap) to 1.0 (identical coverage)</returns>
    double CalculateCoverageOverlap(
        IReadOnlySet<string> methods1, 
        IReadOnlySet<string> methods2, 
        WeightingOptions? options = null);

    /// <summary>
    /// Calculates metadata-based similarity between test info objects.
    /// </summary>
    /// <param name="test1">Information about the first test</param>
    /// <param name="test2">Information about the second test</param>
    /// <returns>Similarity score from 0.0 (completely different) to 1.0 (identical metadata)</returns>
    double CalculateMetadataSimilarity(TestInfo test1, TestInfo test2);
}