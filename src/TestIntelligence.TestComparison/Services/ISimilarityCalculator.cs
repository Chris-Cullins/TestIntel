using System.Collections.Generic;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.TestComparison.Models;
using TestIntelligence.Core.Models;

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

    /// <summary>
    /// Calculates similarity between execution paths using graph topology analysis.
    /// </summary>
    /// <param name="trace1">Execution trace from first test</param>
    /// <param name="trace2">Execution trace from second test</param>
    /// <param name="options">Path comparison configuration</param>
    /// <returns>Similarity score between 0.0 and 1.0</returns>
    double CalculateExecutionPathSimilarity(
        ExecutionTrace trace1, 
        ExecutionTrace trace2, 
        PathComparisonOptions? options = null);
}