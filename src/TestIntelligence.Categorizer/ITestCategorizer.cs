using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Core.Models;

namespace TestIntelligence.Categorizer
{
    /// <summary>
    /// Interface for test categorization services.
    /// Provides intelligent classification of test methods based on various characteristics.
    /// </summary>
    public interface ITestCategorizer
    {
        /// <summary>
        /// Categorizes a single test method based on its characteristics.
        /// </summary>
        /// <param name="testInfo">Test information to categorize</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The determined test category</returns>
        Task<TestCategory> CategorizeAsync(
            TestCategorizationInfo testInfo,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Bulk categorization of multiple tests for improved performance.
        /// </summary>
        /// <param name="tests">Tests to categorize</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary mapping test method name to category</returns>
        Task<IReadOnlyDictionary<string, TestCategory>> CategorizeAsync(
            IEnumerable<TestCategorizationInfo> tests,
            CancellationToken cancellationToken = default);
    }
}