using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.SelectionEngine.Interfaces;
using TestIntelligence.ImpactAnalyzer.Models;
using TestIntelligence.SelectionEngine.Algorithms;

namespace TestIntelligence.SelectionEngine.Services
{
    /// <summary>
    /// Service responsible for scoring tests using various algorithms.
    /// </summary>
    public interface ITestScoringService
    {
        /// <summary>
        /// Scores tests using configured algorithms and returns them ordered by score.
        /// </summary>
        Task<IReadOnlyList<TestInfo>> ScoreTestsAsync(
            IEnumerable<TestInfo> candidateTests, 
            CodeChangeSet? changes = null, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Calculates combined score for a single test using all available algorithms.
        /// </summary>
        Task<double> CalculateCombinedScoreAsync(
            TestInfo testInfo, 
            TestScoringContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the configured scoring algorithms.
        /// </summary>
        IReadOnlyList<ITestScoringAlgorithm> ScoringAlgorithms { get; }
    }
}