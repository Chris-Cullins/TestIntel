using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Core.Models;

namespace TestIntelligence.Core.Services
{
    /// <summary>
    /// Comprehensive service for analyzing test coverage relationships between production methods and test methods.
    /// Combines query, mapping, statistics, and cache management capabilities.
    /// For focused usage, prefer the individual interfaces: ITestCoverageQuery, ITestCoverageMapBuilder, 
    /// ITestCoverageStatistics, or ITestCoverageCacheManager.
    /// </summary>
    public interface ITestCoverageAnalyzer : 
        ITestCoverageQuery, 
        ITestCoverageMapBuilder, 
        ITestCoverageStatistics, 
        ITestCoverageCacheManager
    {
        // This interface now aggregates all focused interfaces for backward compatibility
        // New code should prefer using the focused interfaces directly
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