using System.Text.Json;
using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Models;

namespace TestIntelligence.CLI.Services;

/// <summary>
/// Implementation of configuration service for TestIntelligence CLI
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private readonly ILogger<ConfigurationService> _logger;
    private const string DefaultConfigFileName = "testintel.config";

    public ConfigurationService(ILogger<ConfigurationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TestIntelConfiguration> LoadConfigurationAsync(string solutionPath)
    {
        try
        {
            // Determine the directory where to look for the config file
            var searchDirectory = File.Exists(solutionPath) 
                ? Path.GetDirectoryName(solutionPath) 
                : solutionPath;
            
            if (string.IsNullOrEmpty(searchDirectory))
            {
                _logger.LogWarning("Could not determine directory from solution path: {SolutionPath}", solutionPath);
                return new TestIntelConfiguration();
            }

            var configPath = Path.Combine(searchDirectory, DefaultConfigFileName);
            
            if (File.Exists(configPath))
            {
                _logger.LogInformation("Loading configuration from: {ConfigPath}", configPath);
                return await LoadConfigurationFromFileAsync(configPath);
            }
            else
            {
                _logger.LogDebug("No configuration file found at {ConfigPath}, using defaults", configPath);
                return new TestIntelConfiguration();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load configuration, using defaults");
            return new TestIntelConfiguration();
        }
    }

    public async Task<TestIntelConfiguration> LoadConfigurationFromFileAsync(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"Configuration file not found: {configPath}");
            }

            var jsonContent = await File.ReadAllTextAsync(configPath);
            
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                _logger.LogWarning("Configuration file is empty, using defaults: {ConfigPath}", configPath);
                return new TestIntelConfiguration();
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            var configuration = JsonSerializer.Deserialize<TestIntelConfiguration>(jsonContent, options);
            
            if (configuration == null)
            {
                _logger.LogWarning("Failed to deserialize configuration, using defaults");
                return new TestIntelConfiguration();
            }

            _logger.LogInformation("Successfully loaded configuration from: {ConfigPath}", configPath);
            LogConfigurationSummary(configuration);
            
            return configuration;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid JSON in configuration file: {ConfigPath}", configPath);
            throw new InvalidOperationException($"Configuration file contains invalid JSON: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load configuration from: {ConfigPath}", configPath);
            throw;
        }
    }

    public async Task<bool> CreateDefaultConfigurationAsync(string configPath)
    {
        try
        {
            var defaultConfig = new TestIntelConfiguration
            {
                Projects = new ProjectFilterConfiguration
                {
                    Include = new List<string>(),
                    Exclude = new List<string> 
                    { 
                        "**/obj/**", 
                        "**/bin/**",
                        "*.Integration.Tests*",
                        "*ORM*",
                        "*Database*" 
                    },
                    ExcludeTypes = new List<string> 
                    { 
                        "orm", 
                        "database", 
                        "migration" 
                    },
                    TestProjectsOnly = true
                },
                Analysis = new AnalysisConfiguration
                {
                    Verbose = false,
                    MaxParallelism = Environment.ProcessorCount,
                    TimeoutSeconds = 300
                },
                Output = new OutputConfiguration
                {
                    Format = "text",
                    OutputDirectory = null
                }
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var jsonContent = JsonSerializer.Serialize(defaultConfig, options);
            
            // Add comments to make the file more user-friendly
            var commentedContent = $@"{{
  // TestIntelligence Configuration
  // This file controls which projects are analyzed and how analysis is performed
  
  ""projects"": {{
    // Include specific project patterns (empty = include all)
    ""include"": [],
    
    // Exclude specific project patterns (wildcards supported)
    ""exclude"": [
      ""**/obj/**"",
      ""**/bin/**"",
      ""*.Integration.Tests*"",
      ""*ORM*"",
      ""*Database*""
    ],
    
    // Exclude projects by type/purpose
    ""excludeTypes"": [
      ""orm"",
      ""database"",
      ""migration""
    ],
    
    // Only analyze test projects (recommended)
    ""testProjectsOnly"": true
  }},
  
  ""analysis"": {{
    // Enable verbose logging by default
    ""verbose"": false,
    
    // Maximum parallel analysis operations
    ""maxParallelism"": {Environment.ProcessorCount},
    
    // Timeout for individual project analysis (seconds)
    ""timeoutSeconds"": 300
  }},
  
  ""output"": {{
    // Default output format (text or json)
    ""format"": ""text"",
    
    // Default output directory (null = current directory)
    ""outputDirectory"": null
  }}
}}";

            await File.WriteAllTextAsync(configPath, commentedContent);
            _logger.LogInformation("Created default configuration file: {ConfigPath}", configPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create default configuration file: {ConfigPath}", configPath);
            return false;
        }
    }

    public List<string> FilterProjects(List<string> projectPaths, TestIntelConfiguration configuration)
    {
        if (projectPaths == null || !projectPaths.Any())
        {
            return new List<string>();
        }

        var filteredProjects = new List<string>(projectPaths);
        var projectConfig = configuration.Projects;

        _logger.LogDebug("Filtering {Count} projects with configuration", filteredProjects.Count);

        // Apply include patterns (if any specified)
        if (projectConfig.Include.Any())
        {
            filteredProjects = filteredProjects
                .Where(project => projectConfig.Include.Any(pattern => MatchesPattern(project, pattern)))
                .ToList();
            
            _logger.LogDebug("After include filter: {Count} projects", filteredProjects.Count);
        }

        // Apply exclude patterns
        if (projectConfig.Exclude.Any())
        {
            filteredProjects = filteredProjects
                .Where(project => !projectConfig.Exclude.Any(pattern => MatchesPattern(project, pattern)))
                .ToList();
            
            _logger.LogDebug("After exclude filter: {Count} projects", filteredProjects.Count);
        }

        // Apply type-based exclusions
        if (projectConfig.ExcludeTypes.Any())
        {
            filteredProjects = filteredProjects
                .Where(project => !projectConfig.ExcludeTypes.Any(type => ProjectMatchesType(project, type)))
                .ToList();
            
            _logger.LogDebug("After type exclusion filter: {Count} projects", filteredProjects.Count);
        }

        _logger.LogInformation("Project filtering complete: {Original} → {Filtered} projects", 
            projectPaths.Count, filteredProjects.Count);

        return filteredProjects;
    }

    private bool MatchesPattern(string projectPath, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return false;

        // Simple wildcard matching
        if (pattern.Contains('*'))
        {
            var regex = "^" + pattern
                .Replace("*", ".*")
                .Replace("?", ".")
                .Replace("/", Path.DirectorySeparatorChar.ToString())
                .Replace("\\", Path.DirectorySeparatorChar.ToString()) + "$";
            
            return System.Text.RegularExpressions.Regex.IsMatch(
                projectPath, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // Exact or contains match
        return projectPath.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private bool ProjectMatchesType(string projectPath, string type)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectPath).ToLowerInvariant();
        var typePattern = type.ToLowerInvariant();

        return typePattern switch
        {
            "orm" => projectName.Contains("orm") || 
                    projectName.Contains("entityframework") || 
                    projectName.Contains("ef") ||
                    projectName.Contains("dapper"),
                    
            "database" => projectName.Contains("database") || 
                         projectName.Contains("db") || 
                         projectName.Contains("sql"),
                         
            "migration" => projectName.Contains("migration") || 
                          projectName.Contains("migrations"),
                          
            "integration" => projectName.Contains("integration"),
            
            "api" => projectName.Contains("api") || 
                    projectName.Contains("webapi") || 
                    projectName.Contains("rest"),
                    
            "ui" => projectName.Contains("ui") || 
                   projectName.Contains("web") || 
                   projectName.Contains("client"),
                   
            _ => projectName.Contains(typePattern)
        };
    }

    private void LogConfigurationSummary(TestIntelConfiguration configuration)
    {
        if (!_logger.IsEnabled(LogLevel.Information))
            return;

        _logger.LogInformation("Configuration loaded:");
        
        if (configuration.Projects.Include.Any())
        {
            _logger.LogInformation("  Include patterns: {Patterns}", string.Join(", ", configuration.Projects.Include));
        }
        
        if (configuration.Projects.Exclude.Any())
        {
            _logger.LogInformation("  Exclude patterns: {Patterns}", string.Join(", ", configuration.Projects.Exclude));
        }
        
        if (configuration.Projects.ExcludeTypes.Any())
        {
            _logger.LogInformation("  Exclude types: {Types}", string.Join(", ", configuration.Projects.ExcludeTypes));
        }
        
        _logger.LogInformation("  Test projects only: {TestProjectsOnly}", configuration.Projects.TestProjectsOnly);
        _logger.LogInformation("  Max parallelism: {MaxParallelism}", configuration.Analysis.MaxParallelism);
        _logger.LogInformation("  Default format: {Format}", configuration.Output.Format);
    }
}