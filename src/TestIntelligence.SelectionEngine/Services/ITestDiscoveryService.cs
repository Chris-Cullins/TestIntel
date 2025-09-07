using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.ImpactAnalyzer.Models;

namespace TestIntelligence.SelectionEngine.Services
{
    /// <summary>
    /// Service responsible for discovering tests from various sources (solutions, directories, etc.).
    /// </summary>
    public interface ITestDiscoveryService
    {
        /// <summary>
        /// Discovers candidate tests based on code changes and options.
        /// </summary>
        Task<List<TestInfo>> GetCandidateTestsAsync(
            CodeChangeSet? changes, 
            TestSelectionOptions options, 
            string? solutionPath = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Discovers all tests from a specific solution.
        /// </summary>
        Task<List<TestInfo>> DiscoverTestsFromSolutionAsync(
            string solutionPath, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds solution file based on a file path.
        /// </summary>
        string? FindSolutionFile(string filePath);
    }
}