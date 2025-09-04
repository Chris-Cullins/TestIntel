using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.ImpactAnalyzer.Models;
using TestIntelligence.SelectionEngine.Models;

namespace TestIntelligence.SelectionEngine.Interfaces
{
    /// <summary>
    /// Main interface for intelligent test selection.
    /// </summary>
    public interface ITestSelectionEngine
    {
        /// <summary>
        /// Gets an optimal test execution plan based on code changes and confidence level.
        /// </summary>
        Task<TestExecutionPlan> GetOptimalTestPlanAsync(
            CodeChangeSet changes,
            ConfidenceLevel confidenceLevel,
            TestSelectionOptions? options = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a test execution plan without specific code changes (e.g., for scheduled runs).
        /// </summary>
        Task<TestExecutionPlan> GetTestPlanAsync(
            ConfidenceLevel confidenceLevel,
            TestSelectionOptions? options = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Scores and ranks tests based on their likelihood to catch regressions.
        /// </summary>
        Task<IReadOnlyList<TestInfo>> ScoreTestsAsync(
            IEnumerable<TestInfo> candidateTests,
            CodeChangeSet? changes = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates test execution history and performance metrics.
        /// </summary>
        Task UpdateTestExecutionHistoryAsync(
            IEnumerable<TestExecutionResult> results,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets historical test execution data for analysis.
        /// </summary>
        Task<IReadOnlyList<TestInfo>> GetTestHistoryAsync(
            string? testFilter = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Interface for test scoring algorithms.
    /// </summary>
    public interface ITestScoringAlgorithm
    {
        /// <summary>
        /// Calculates a selection score for a test based on various factors.
        /// </summary>
        Task<double> CalculateScoreAsync(
            TestInfo testInfo,
            TestScoringContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Name of this scoring algorithm.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Weight of this algorithm in combined scoring (0.0 to 1.0).
        /// </summary>
        double Weight { get; }
    }

    /// <summary>
    /// Interface for test categorization services.
    /// </summary>
    public interface ITestCategorizer
    {
        /// <summary>
        /// Categorizes a test method based on its characteristics.
        /// </summary>
        Task<TestCategory> CategorizeAsync(
            TestInfo testInfo,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Bulk categorization of multiple tests.
        /// </summary>
        Task<IReadOnlyDictionary<string, TestCategory>> CategorizeAsync(
            IEnumerable<TestInfo> tests,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Interface for impact analysis services.
    /// </summary>
    public interface IImpactAnalyzer
    {
        /// <summary>
        /// Gets tests that are likely affected by code changes.
        /// </summary>
        Task<IReadOnlyList<TestInfo>> GetAffectedTestsAsync(
            CodeChangeSet changes,
            IEnumerable<TestInfo> availableTests,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Calculates impact score for a test given specific changes.
        /// </summary>
        Task<double> CalculateImpactScoreAsync(
            TestInfo testInfo,
            CodeChangeSet changes,
            CancellationToken cancellationToken = default);
    }
}

/// <summary>
/// Options for test selection behavior.
/// </summary>
public class TestSelectionOptions
{
    /// <summary>
    /// Maximum number of tests to select.
    /// </summary>
    public int? MaxTestCount { get; set; }

    /// <summary>
    /// Maximum execution time budget.
    /// </summary>
    public System.TimeSpan? MaxExecutionTime { get; set; }

    /// <summary>
    /// Include only specific test categories.
    /// </summary>
    public HashSet<TestCategory>? IncludedCategories { get; set; }

    /// <summary>
    /// Exclude specific test categories.
    /// </summary>
    public HashSet<TestCategory>? ExcludedCategories { get; set; }

    /// <summary>
    /// Include only tests matching these tags.
    /// </summary>
    public HashSet<string>? RequiredTags { get; set; }

    /// <summary>
    /// Exclude tests with these tags.
    /// </summary>
    public HashSet<string>? ExcludedTags { get; set; }

    /// <summary>
    /// Minimum selection score threshold (0.0 to 1.0).
    /// </summary>
    public double? MinSelectionScore { get; set; }

    /// <summary>
    /// Whether to include flaky tests.
    /// </summary>
    public bool IncludeFlakyTests { get; set; } = true;

    /// <summary>
    /// Whether to prioritize recently failing tests.
    /// </summary>
    public bool PrioritizeFailingTests { get; set; } = true;

    /// <summary>
    /// Maximum parallelism degree for execution planning.
    /// </summary>
    public int MaxParallelism { get; set; } = 4;
}

/// <summary>
/// Context information for test scoring.
/// </summary>
public class TestScoringContext
{
    public TestScoringContext(
        ConfidenceLevel confidenceLevel,
        CodeChangeSet? codeChanges = null,
        TestSelectionOptions? options = null)
    {
        ConfidenceLevel = confidenceLevel;
        CodeChanges = codeChanges;
        Options = options ?? new TestSelectionOptions();
        ScoringTimestamp = System.DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Target confidence level for selection.
    /// </summary>
    public ConfidenceLevel ConfidenceLevel { get; }

    /// <summary>
    /// Code changes that triggered this scoring request.
    /// </summary>
    public CodeChangeSet? CodeChanges { get; }

    /// <summary>
    /// Selection options and constraints.
    /// </summary>
    public TestSelectionOptions Options { get; }

    /// <summary>
    /// When this scoring context was created.
    /// </summary>
    public System.DateTimeOffset ScoringTimestamp { get; }

    /// <summary>
    /// Additional metadata for scoring algorithms.
    /// </summary>
    public Dictionary<string, object> Metadata { get; } = new Dictionary<string, object>();
}