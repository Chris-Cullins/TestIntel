using System;
using System.Collections.Generic;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.ImpactAnalyzer.Models;

namespace TestIntelligence.API.Models;

/// <summary>
/// Request model for creating a test execution plan.
/// </summary>
public class TestPlanRequest
{
    /// <summary>
    /// Code changes to analyze for test impact.
    /// </summary>
    public CodeChangeSet? CodeChanges { get; set; }

    /// <summary>
    /// Confidence level for test selection.
    /// </summary>
    public ConfidenceLevel ConfidenceLevel { get; set; } = ConfidenceLevel.Medium;

    /// <summary>
    /// Maximum number of tests to include in the plan.
    /// </summary>
    public int? MaxTests { get; set; }

    /// <summary>
    /// Maximum execution time for the test plan.
    /// </summary>
    public TimeSpan? MaxExecutionTime { get; set; }

    /// <summary>
    /// Test categories to exclude from selection.
    /// </summary>
    public List<TestCategory>? ExcludedCategories { get; set; }

    /// <summary>
    /// Test categories to include in selection (if specified, only these categories will be included).
    /// </summary>
    public List<TestCategory>? IncludedCategories { get; set; }
}

/// <summary>
/// Request model for analyzing git diff impact.
/// </summary>
public class DiffAnalysisRequest
{
    /// <summary>
    /// Path to the solution file for context.
    /// </summary>
    public string SolutionPath { get; set; } = string.Empty;

    /// <summary>
    /// Git diff content to analyze.
    /// </summary>
    public string DiffContent { get; set; } = string.Empty;

    /// <summary>
    /// Confidence level for test selection.
    /// </summary>
    public ConfidenceLevel ConfidenceLevel { get; set; } = ConfidenceLevel.Medium;
}

/// <summary>
/// Response model for diff analysis results.
/// </summary>
public class DiffAnalysisResult
{
    /// <summary>
    /// Analyzed code changes.
    /// </summary>
    public CodeChangeSet ChangeSet { get; set; } = new(new List<CodeChange>());

    /// <summary>
    /// Recommended test execution plan.
    /// </summary>
    public TestExecutionPlan RecommendedTests { get; set; } = new(Array.Empty<TestInfo>(), ConfidenceLevel.Medium, TimeSpan.Zero);

    /// <summary>
    /// When the analysis was performed.
    /// </summary>
    public DateTimeOffset AnalysisTimestamp { get; set; }

    /// <summary>
    /// Total number of code changes detected.
    /// </summary>
    public int TotalChanges { get; set; }

    /// <summary>
    /// Overall impact score (0.0 to 1.0).
    /// </summary>
    public double ImpactScore { get; set; }
}

/// <summary>
/// Request model for test discovery operations.
/// </summary>
public class TestDiscoveryRequest
{
    /// <summary>
    /// Path to solution file or test assembly.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Whether to include detailed method analysis.
    /// </summary>
    public bool IncludeDetailedAnalysis { get; set; } = true;

    /// <summary>
    /// Categories to filter by (empty = all categories).
    /// </summary>
    public List<TestCategory>? CategoryFilter { get; set; }
}

/// <summary>
/// Response model for test discovery results.
/// </summary>
public class TestDiscoveryResponse
{
    /// <summary>
    /// Discovered test information.
    /// </summary>
    public List<TestInfo> Tests { get; set; } = new();

    /// <summary>
    /// Summary statistics.
    /// </summary>
    public TestDiscoverySummary Summary { get; set; } = new();

    /// <summary>
    /// Any errors encountered during discovery.
    /// </summary>
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Summary statistics for test discovery.
/// </summary>
public class TestDiscoverySummary
{
    /// <summary>
    /// Total number of tests discovered.
    /// </summary>
    public int TotalTests { get; set; }

    /// <summary>
    /// Breakdown by test category.
    /// </summary>
    public Dictionary<TestCategory, int> CategoryBreakdown { get; set; } = new();

    /// <summary>
    /// Total estimated execution time.
    /// </summary>
    public TimeSpan EstimatedTotalDuration { get; set; }

    /// <summary>
    /// Number of assemblies analyzed.
    /// </summary>
    public int AssembliesAnalyzed { get; set; }
}