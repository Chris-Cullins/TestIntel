using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Core.Services;

namespace TestIntelligence.Core.Services
{
    /// <summary>
    /// Service for calculating test coverage statistics and metrics.
    /// Provides insight into overall test coverage across a solution.
    /// </summary>
    public interface ITestCoverageStatistics
    {
        /// <summary>
        /// Gets statistics about test coverage for the solution.
        /// </summary>
        /// <param name="solutionPath">Path to the solution file</param>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>Coverage statistics including total methods, covered methods, and coverage percentages</returns>
        Task<TestCoverageStatistics> GetCoverageStatisticsAsync(
            string solutionPath,
            CancellationToken cancellationToken = default);
    }
}