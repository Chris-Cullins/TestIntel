using TestIntelligence.CLI.Models;

namespace TestIntelligence.CLI.Services;

/// <summary>
/// Service for loading and managing TestIntelligence configuration
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Load configuration from the default location (testintel.config in solution directory)
    /// </summary>
    /// <param name="solutionPath">Path to solution file or directory</param>
    /// <returns>Configuration, or default configuration if no config file found</returns>
    Task<TestIntelConfiguration> LoadConfigurationAsync(string solutionPath);

    /// <summary>
    /// Load configuration from a specific file path
    /// </summary>
    /// <param name="configPath">Path to configuration file</param>
    /// <returns>Configuration loaded from file</returns>
    Task<TestIntelConfiguration> LoadConfigurationFromFileAsync(string configPath);

    /// <summary>
    /// Create a default configuration file at the specified location
    /// </summary>
    /// <param name="configPath">Path where to create the configuration file</param>
    /// <returns>True if created successfully</returns>
    Task<bool> CreateDefaultConfigurationAsync(string configPath);

    /// <summary>
    /// Filter project paths based on configuration rules
    /// </summary>
    /// <param name="projectPaths">List of all project paths</param>
    /// <param name="configuration">Configuration with filtering rules</param>
    /// <returns>Filtered list of project paths</returns>
    List<string> FilterProjects(List<string> projectPaths, TestIntelConfiguration configuration);

    /// <summary>
    /// Analyze all projects in a solution and return detailed information about inclusion/exclusion
    /// </summary>
    /// <param name="solutionPath">Path to solution file or directory</param>
    /// <param name="configuration">Configuration with filtering rules</param>
    /// <returns>Detailed project analysis results</returns>
    Task<ProjectFilterAnalysisResult> AnalyzeProjectFilteringAsync(string solutionPath, TestIntelConfiguration configuration);
}