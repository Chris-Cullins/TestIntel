using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Core.Models;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Models;

namespace TestIntelligence.ImpactAnalyzer.Services
{
    /// <summary>
    /// Service for analyzing how well specific tests cover code changes from git diffs.
    /// </summary>
    public interface ICodeChangeCoverageAnalyzer
    {
        /// <summary>
        /// Analyzes how well the specified tests cover the changes in a git diff.
        /// </summary>
        /// <param name="diffContent">Git diff content to analyze</param>
        /// <param name="testMethodIds">Collection of test method IDs to check for coverage</param>
        /// <param name="solutionPath">Path to the solution file</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Coverage analysis result with percentage and detailed breakdown</returns>
        Task<CodeChangeCoverageResult> AnalyzeCoverageAsync(
            string diffContent,
            IEnumerable<string> testMethodIds,
            string solutionPath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Analyzes how well the specified tests cover changes from a git diff file.
        /// </summary>
        /// <param name="diffFilePath">Path to git diff file</param>
        /// <param name="testMethodIds">Collection of test method IDs to check for coverage</param>
        /// <param name="solutionPath">Path to the solution file</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Coverage analysis result with percentage and detailed breakdown</returns>
        Task<CodeChangeCoverageResult> AnalyzeCoverageFromFileAsync(
            string diffFilePath,
            IEnumerable<string> testMethodIds,
            string solutionPath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Analyzes how well the specified tests cover changes from a git command.
        /// </summary>
        /// <param name="gitCommand">Git command to generate diff (e.g., "diff HEAD~1")</param>
        /// <param name="testMethodIds">Collection of test method IDs to check for coverage</param>
        /// <param name="solutionPath">Path to the solution file</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Coverage analysis result with percentage and detailed breakdown</returns>
        Task<CodeChangeCoverageResult> AnalyzeCoverageFromGitCommandAsync(
            string gitCommand,
            IEnumerable<string> testMethodIds,
            string solutionPath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Analyzes coverage for a specific test method against code changes.
        /// </summary>
        /// <param name="codeChanges">Code changes to analyze</param>
        /// <param name="testMethodId">Single test method ID to check</param>
        /// <param name="solutionPath">Path to the solution file</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Coverage analysis result focusing on the single test</returns>
        Task<CodeChangeCoverageResult> AnalyzeSingleTestCoverageAsync(
            CodeChangeSet codeChanges,
            string testMethodId,
            string solutionPath,
            CancellationToken cancellationToken = default);
    }
}