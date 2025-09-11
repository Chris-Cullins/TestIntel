using System.Text.Json.Serialization;

namespace TestIntelligence.CLI.Models;

/// <summary>
/// Configuration model for TestIntelligence CLI operations
/// </summary>
public class TestIntelConfiguration
{
    /// <summary>
    /// Project filtering configuration
    /// </summary>
    [JsonPropertyName("projects")]
    public ProjectFilterConfiguration Projects { get; set; } = new();

    /// <summary>
    /// Analysis configuration options
    /// </summary>
    [JsonPropertyName("analysis")]
    public AnalysisConfiguration Analysis { get; set; } = new();
    
    /// <summary>
    /// Default output configuration
    /// </summary>
    [JsonPropertyName("output")]
    public OutputConfiguration Output { get; set; } = new();
}

/// <summary>
/// Configuration for filtering which projects to include/exclude from analysis
/// </summary>
public class ProjectFilterConfiguration
{
    /// <summary>
    /// List of project name patterns to include (supports wildcards)
    /// If empty, all projects are included by default
    /// </summary>
    [JsonPropertyName("include")]
    public List<string> Include { get; set; } = new();

    /// <summary>
    /// List of project name patterns to exclude (supports wildcards)
    /// Takes precedence over include patterns
    /// </summary>
    [JsonPropertyName("exclude")]
    public List<string> Exclude { get; set; } = new();

    /// <summary>
    /// List of project types to exclude from analysis
    /// </summary>
    [JsonPropertyName("excludeTypes")]
    public List<string> ExcludeTypes { get; set; } = new();

    /// <summary>
    /// Whether to only analyze test projects (default: true)
    /// Set to false to include all projects
    /// </summary>
    [JsonPropertyName("testProjectsOnly")]
    public bool TestProjectsOnly { get; set; } = true;
}

/// <summary>
/// Configuration for analysis behavior
/// </summary>
public class AnalysisConfiguration
{
    /// <summary>
    /// Default verbosity level for analysis
    /// </summary>
    [JsonPropertyName("verbose")]
    public bool Verbose { get; set; } = false;

    /// <summary>
    /// Maximum number of parallel analysis operations
    /// </summary>
    [JsonPropertyName("maxParallelism")]
    public int MaxParallelism { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Timeout for individual project analysis in seconds
    /// </summary>
    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 300; // 5 minutes

    /// <summary>
    /// Maximum BFS path depth for find-tests traversal (defaults to 12)
    /// </summary>
    [JsonPropertyName("findTestsMaxPathDepth")]
    public int FindTestsMaxPathDepth { get; set; } = 12;

    /// <summary>
    /// Maximum visited nodes limit for find-tests traversal (defaults to 2000)
    /// </summary>
    [JsonPropertyName("findTestsMaxVisitedNodes")]
    public int FindTestsMaxVisitedNodes { get; set; } = 2000;
}

/// <summary>
/// Configuration for output formatting and destinations
/// </summary>
public class OutputConfiguration
{
    /// <summary>
    /// Default output format (json, text)
    /// </summary>
    [JsonPropertyName("format")]
    public string Format { get; set; } = "text";

    /// <summary>
    /// Default output directory for generated files
    /// </summary>
    [JsonPropertyName("outputDirectory")]
    public string? OutputDirectory { get; set; }
}

/// <summary>
/// Result of project filtering analysis showing which projects are included/excluded and why
/// </summary>
public class ProjectFilterAnalysisResult
{
    /// <summary>
    /// Path to the analyzed solution
    /// </summary>
    public string SolutionPath { get; set; } = string.Empty;

    /// <summary>
    /// Configuration that was applied
    /// </summary>
    public TestIntelConfiguration Configuration { get; set; } = new();

    /// <summary>
    /// All projects found in the solution with their analysis details
    /// </summary>
    public List<ProjectAnalysisDetail> Projects { get; set; } = new();

    /// <summary>
    /// Summary statistics
    /// </summary>
    public ProjectFilterSummary Summary { get; set; } = new();
}

/// <summary>
/// Detailed analysis of a single project's filtering status
/// </summary>
public class ProjectAnalysisDetail
{
    /// <summary>
    /// Full path to the project file
    /// </summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>
    /// Project name (without path)
    /// </summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>
    /// Whether this project would be included in analysis
    /// </summary>
    public bool IsIncluded { get; set; }

    /// <summary>
    /// Whether this project was identified as a test project
    /// </summary>
    public bool IsTestProject { get; set; }

    /// <summary>
    /// Detected project type (if any)
    /// </summary>
    public string? DetectedType { get; set; }

    /// <summary>
    /// Reasons why this project was included or excluded
    /// </summary>
    public List<string> FilteringReasons { get; set; } = new();
}

/// <summary>
/// Summary statistics for project filtering analysis
/// </summary>
public class ProjectFilterSummary
{
    /// <summary>
    /// Total number of projects found
    /// </summary>
    public int TotalProjects { get; set; }

    /// <summary>
    /// Number of projects that would be included
    /// </summary>
    public int IncludedProjects { get; set; }

    /// <summary>
    /// Number of projects that would be excluded
    /// </summary>
    public int ExcludedProjects { get; set; }

    /// <summary>
    /// Number of projects identified as test projects
    /// </summary>
    public int TestProjects { get; set; }

    /// <summary>
    /// Number of projects identified as production projects
    /// </summary>
    public int ProductionProjects { get; set; }
}
