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
}