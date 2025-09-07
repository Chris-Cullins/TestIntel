using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.SelectionEngine.Models;

namespace TestIntelligence.SelectionEngine.Services
{
    /// <summary>
    /// Service responsible for managing test execution history and repository.
    /// </summary>
    public interface ITestHistoryService
    {
        /// <summary>
        /// Updates test execution history with new results.
        /// </summary>
        Task UpdateTestExecutionHistoryAsync(
            IEnumerable<TestExecutionResult> results, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves test history with optional filtering.
        /// </summary>
        Task<IReadOnlyList<TestInfo>> GetTestHistoryAsync(
            string? testFilter = null, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all tests from the repository.
        /// </summary>
        IReadOnlyDictionary<string, TestInfo> GetAllTests();

        /// <summary>
        /// Adds or updates a test in the repository.
        /// </summary>
        Task AddOrUpdateTestAsync(TestInfo testInfo, CancellationToken cancellationToken = default);
    }
}