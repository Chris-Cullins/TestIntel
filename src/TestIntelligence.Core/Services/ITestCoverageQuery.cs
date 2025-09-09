using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Core.Models;

namespace TestIntelligence.Core.Services
{
    /// <summary>
    /// Service for querying test coverage relationships between production methods and test methods.
    /// Provides lookup functionality to find which tests exercise given production methods.
    /// </summary>
    public interface ITestCoverageQuery
    {
        /// <summary>
        /// Finds all test methods that exercise (directly or indirectly call) the specified production method.
        /// </summary>
        /// <param name="methodId">Unique identifier for the production method to analyze</param>
        /// <param name="solutionPath">Path to the solution file</param>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>List of test coverage information showing which tests exercise the method</returns>
        Task<IReadOnlyList<TestCoverageInfo>> FindTestsExercisingMethodAsync(
            string methodId, 
            string solutionPath, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds tests that exercise any of the specified production methods.
        /// More efficient than calling FindTestsExercisingMethodAsync multiple times.
        /// </summary>
        /// <param name="methodIds">Collection of method identifiers to analyze</param>
        /// <param name="solutionPath">Path to the solution file</param>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>Dictionary mapping each method ID to its test coverage information</returns>
        Task<IReadOnlyDictionary<string, IReadOnlyList<TestCoverageInfo>>> FindTestsExercisingMethodsAsync(
            IEnumerable<string> methodIds,
            string solutionPath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds tests that exercise the specified methods using a scoped incremental analysis.
        /// Only the provided test IDs are considered as candidates, enabling a minimal call graph.
        /// </summary>
        /// <param name="methodIds">Changed/target method identifiers</param>
        /// <param name="providedTestIds">Test method identifiers to consider as candidates</param>
        /// <param name="solutionPath">Solution path</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<IReadOnlyDictionary<string, IReadOnlyList<TestCoverageInfo>>> FindTestsExercisingMethodsScopedAsync(
            IEnumerable<string> methodIds,
            IEnumerable<string> providedTestIds,
            string solutionPath,
            CancellationToken cancellationToken = default);
    }
}
