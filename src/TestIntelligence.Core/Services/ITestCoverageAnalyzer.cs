using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Core.Models;

namespace TestIntelligence.Core.Services
{
    /// <summary>
    /// Service for analyzing test coverage relationships between production methods and test methods.
    /// Provides reverse lookup functionality to find which tests exercise a given production method.
    /// </summary>
    public interface ITestCoverageAnalyzer
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
        /// Builds a complete test coverage map for all production methods in the solution.
        /// This map can be cached and reused for multiple lookup operations.
        /// </summary>
        /// <param name="solutionPath">Path to the solution file</param>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>Complete mapping from production methods to tests that exercise them</returns>
        Task<TestCoverageMap> BuildTestCoverageMapAsync(
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
        /// Gets statistics about test coverage for the solution.
        /// </summary>
        /// <param name="solutionPath">Path to the solution file</param>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>Coverage statistics including total methods, covered methods, and coverage percentages</returns>
        Task<TestCoverageStatistics> GetCoverageStatisticsAsync(
            string solutionPath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Clears all cached data (call graphs and path calculations).
        /// Call this when source files or solution structure changes.
        /// </summary>
        void ClearCaches();
    }

    /// <summary>
    /// Statistics about test coverage in a solution.
    /// </summary>
    public class TestCoverageStatistics
    {
        public TestCoverageStatistics(
            int totalMethods,
            int coveredMethods,
            int totalTests,
            int totalCoverageRelationships,
            Dictionary<TestType, int> coverageByTestType)
        {
            TotalMethods = totalMethods;
            CoveredMethods = coveredMethods;
            TotalTests = totalTests;
            TotalCoverageRelationships = totalCoverageRelationships;
            CoverageByTestType = coverageByTestType ?? throw new ArgumentNullException(nameof(coverageByTestType));
        }

        /// <summary>
        /// Total number of production methods in the solution.
        /// </summary>
        public int TotalMethods { get; }

        /// <summary>
        /// Number of production methods that have at least one test exercising them.
        /// </summary>
        public int CoveredMethods { get; }

        /// <summary>
        /// Total number of test methods in the solution.
        /// </summary>
        public int TotalTests { get; }

        /// <summary>
        /// Total number of coverage relationships (one test may cover multiple methods).
        /// </summary>
        public int TotalCoverageRelationships { get; }

        /// <summary>
        /// Breakdown of coverage relationships by test type.
        /// </summary>
        public Dictionary<TestType, int> CoverageByTestType { get; }

        /// <summary>
        /// Percentage of methods that have test coverage (0.0 - 100.0).
        /// </summary>
        public double CoveragePercentage => 
            TotalMethods == 0 ? 0.0 : (double)CoveredMethods / TotalMethods * 100.0;

        /// <summary>
        /// Number of methods without any test coverage.
        /// </summary>
        public int UncoveredMethods => TotalMethods - CoveredMethods;
    }
}