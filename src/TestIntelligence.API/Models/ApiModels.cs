using System;
using System.Collections.Generic;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.ImpactAnalyzer.Models;
using TestIntelligence.Core.Models;
using TestIntelligence.Core.Services;

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

// Test Coverage API Models

/// <summary>
/// Request model for finding tests exercising a single method.
/// </summary>
public class TestCoverageRequest
{
    /// <summary>
    /// Path to the solution file or directory.
    /// </summary>
    public string SolutionPath { get; set; } = string.Empty;
}

/// <summary>
/// Response model for test coverage analysis.
/// </summary>
public class TestCoverageResponse
{
    /// <summary>
    /// Method identifier that was analyzed.
    /// </summary>
    public string MethodId { get; set; } = string.Empty;

    /// <summary>
    /// Path to the solution that was analyzed.
    /// </summary>
    public string SolutionPath { get; set; } = string.Empty;

    /// <summary>
    /// Tests that exercise the method.
    /// </summary>
    public List<TestCoverageInfo> Tests { get; set; } = new();

    /// <summary>
    /// Total number of tests found.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// When the analysis was performed.
    /// </summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Request model for bulk test coverage analysis.
/// </summary>
public class BulkTestCoverageRequest
{
    /// <summary>
    /// Method identifiers to analyze.
    /// </summary>
    public IEnumerable<string> MethodIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Path to the solution file or directory.
    /// </summary>
    public string SolutionPath { get; set; } = string.Empty;
}

/// <summary>
/// Response model for bulk test coverage analysis.
/// </summary>
public class BulkTestCoverageResponse
{
    /// <summary>
    /// Path to the solution that was analyzed.
    /// </summary>
    public string SolutionPath { get; set; } = string.Empty;

    /// <summary>
    /// Results for each method.
    /// </summary>
    public Dictionary<string, TestCoverageResponse> Results { get; set; } = new();

    /// <summary>
    /// Total number of methods analyzed.
    /// </summary>
    public int TotalMethods { get; set; }

    /// <summary>
    /// When the analysis was performed.
    /// </summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Request model for building test coverage map.
/// </summary>
public class TestCoverageMapRequest
{
    /// <summary>
    /// Path to the solution file or directory.
    /// </summary>
    public string SolutionPath { get; set; } = string.Empty;
}

/// <summary>
/// Response model for test coverage map.
/// </summary>
public class TestCoverageMapResponse
{
    /// <summary>
    /// Path to the solution that was analyzed.
    /// </summary>
    public string SolutionPath { get; set; } = string.Empty;

    /// <summary>
    /// When the coverage map was built.
    /// </summary>
    public DateTime BuildTimestamp { get; set; }

    /// <summary>
    /// Number of methods with test coverage.
    /// </summary>
    public int CoveredMethodCount { get; set; }

    /// <summary>
    /// Total number of coverage relationships.
    /// </summary>
    public int TotalCoverageRelationships { get; set; }

    /// <summary>
    /// List of method IDs that have test coverage.
    /// </summary>
    public List<string> CoveredMethods { get; set; } = new();

    /// <summary>
    /// When the response was generated.
    /// </summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Request model for test coverage statistics.
/// </summary>
public class TestCoverageStatisticsRequest
{
    /// <summary>
    /// Path to the solution file or directory.
    /// </summary>
    public string SolutionPath { get; set; } = string.Empty;
}

/// <summary>
/// Response model for test coverage statistics.
/// </summary>
public class TestCoverageStatisticsResponse
{
    /// <summary>
    /// Path to the solution that was analyzed.
    /// </summary>
    public string SolutionPath { get; set; } = string.Empty;

    /// <summary>
    /// Test coverage statistics.
    /// </summary>
    public TestCoverageStatistics Statistics { get; set; } = null!;

    /// <summary>
    /// When the statistics were calculated.
    /// </summary>
    public DateTime Timestamp { get; set; }
}