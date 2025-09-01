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