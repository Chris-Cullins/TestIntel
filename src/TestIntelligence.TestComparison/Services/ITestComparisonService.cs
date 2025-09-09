using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.TestComparison.Models;

namespace TestIntelligence.TestComparison.Services;

/// <summary>
/// Service for comparing tests and analyzing overlap between test methods.
/// </summary>
public interface ITestComparisonService
{
    /// <summary>
    /// Compares two test methods and generates detailed overlap analysis.
    /// </summary>
    /// <param name="test1Id">Full identifier of first test method</param>
    /// <param name="test2Id">Full identifier of second test method</param>
    /// <param name="solutionPath">Path to solution file</param>
    /// <param name="options">Comparison configuration options</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>Detailed comparison results</returns>
    Task<TestComparisonResult> CompareTestsAsync(
        string test1Id, 
        string test2Id, 
        string solutionPath, 
        ComparisonOptions options, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes multiple tests to identify clusters of similar tests.
    /// </summary>
    /// <param name="testIds">Collection of test method identifiers</param>
    /// <param name="solutionPath">Path to solution file</param>
    /// <param name="options">Clustering configuration options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Test cluster analysis with groupings and statistics</returns>
    Task<TestClusterAnalysis> AnalyzeTestClustersAsync(
        IEnumerable<string> testIds, 
        string solutionPath, 
        ClusteringOptions options, 
        CancellationToken cancellationToken = default);
}