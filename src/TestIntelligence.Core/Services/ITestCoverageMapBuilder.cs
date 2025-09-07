using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Core.Models;

namespace TestIntelligence.Core.Services
{
    /// <summary>
    /// Service for building comprehensive test coverage maps for solutions.
    /// Creates reusable mappings from production methods to test methods that exercise them.
    /// </summary>
    public interface ITestCoverageMapBuilder
    {
        /// <summary>
        /// Builds a complete test coverage map for all production methods in the solution.
        /// This map can be cached and reused for multiple lookup operations.
        /// </summary>
        /// <param name="solutionPath">Path to the solution file</param>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>Complete mapping from production methods to tests that exercise them</returns>
        Task<TestCoverageMap> BuildTestCoverageMapAsync(
            string solutionPath, 
            CancellationToken cancellationToken = default);
    }
}