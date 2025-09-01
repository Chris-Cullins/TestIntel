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

    public async Task<ProjectFilterAnalysisResult> AnalyzeProjectFilteringAsync(string solutionPath, TestIntelConfiguration configuration)
    {
        try
        {
            _logger.LogInformation("Analyzing project filtering for solution: {SolutionPath}", solutionPath);

            var result = new ProjectFilterAnalysisResult
            {
                SolutionPath = solutionPath,
                Configuration = configuration
            };

            // Find all projects in the solution
            var allProjectPaths = await FindAllProjectsInSolutionAsync(solutionPath);
            
            _logger.LogDebug("Found {Count} projects in solution", allProjectPaths.Count);

            // Analyze each project
            foreach (var projectPath in allProjectPaths)
            {
                var detail = await AnalyzeProjectAsync(projectPath, configuration);
                result.Projects.Add(detail);
            }

            // Calculate summary statistics
            result.Summary = new ProjectFilterSummary
            {
                TotalProjects = result.Projects.Count,
                IncludedProjects = result.Projects.Count(p => p.IsIncluded),
                ExcludedProjects = result.Projects.Count(p => !p.IsIncluded),
                TestProjects = result.Projects.Count(p => p.IsTestProject),
                ProductionProjects = result.Projects.Count(p => !p.IsTestProject)
            };

            _logger.LogInformation("Project analysis complete: {Total} total, {Included} included, {Excluded} excluded",
                result.Summary.TotalProjects, result.Summary.IncludedProjects, result.Summary.ExcludedProjects);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze project filtering");
            throw;
        }
    }

    private async Task<ProjectAnalysisDetail> AnalyzeProjectAsync(string projectPath, TestIntelConfiguration configuration)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var isTestProject = await IsTestProjectAsync(projectPath);
        var detectedType = DetectProjectType(projectPath);
        
        var detail = new ProjectAnalysisDetail
        {
            ProjectPath = projectPath,
            ProjectName = projectName,
            IsTestProject = isTestProject,
            DetectedType = detectedType
        };

        // Determine if this project would be included based on configuration
        var reasons = new List<string>();
        var wouldBeIncluded = WouldProjectBeIncluded(projectPath, configuration, isTestProject, detectedType, reasons);
        
        detail.IsIncluded = wouldBeIncluded;
        detail.FilteringReasons = reasons;

        return detail;
    }

    private bool WouldProjectBeIncluded(string projectPath, TestIntelConfiguration configuration, 
        bool isTestProject, string? detectedType, List<string> reasons)
    {
        var projectConfig = configuration.Projects;

        // Check testProjectsOnly setting
        if (projectConfig.TestProjectsOnly && !isTestProject)
        {
            reasons.Add("Excluded: Not a test project (testProjectsOnly=true)");
            return false;
        }

        if (projectConfig.TestProjectsOnly && isTestProject)
        {
            reasons.Add("Included: Test project and testProjectsOnly=true");
        }
        else if (!projectConfig.TestProjectsOnly)
        {
            reasons.Add(isTestProject ? "Included: Test project" : "Included: Production project");
        }

        // Apply include patterns (if any specified)
        if (projectConfig.Include.Any())
        {
            var matchesInclude = projectConfig.Include.Any(pattern => MatchesPattern(projectPath, pattern));
            if (!matchesInclude)
            {
                reasons.Add($"Excluded: Does not match include patterns [{string.Join(", ", projectConfig.Include)}]");
                return false;
            }
            else
            {
                reasons.Add($"Included: Matches include pattern");
            }
        }

        // Apply exclude patterns
        if (projectConfig.Exclude.Any())
        {
            var matchingExcludePattern = projectConfig.Exclude.FirstOrDefault(pattern => MatchesPattern(projectPath, pattern));
            if (matchingExcludePattern != null)
            {
                reasons.Add($"Excluded: Matches exclude pattern '{matchingExcludePattern}'");
                return false;
            }
        }

        // Apply type-based exclusions
        if (projectConfig.ExcludeTypes.Any() && detectedType != null)
        {
            var matchingExcludeType = projectConfig.ExcludeTypes.FirstOrDefault(type => 
                string.Equals(type, detectedType, StringComparison.OrdinalIgnoreCase));
            if (matchingExcludeType != null)
            {
                reasons.Add($"Excluded: Project type '{detectedType}' matches excluded type '{matchingExcludeType}'");
                return false;
            }
        }

        return true;
    }

    private string? DetectProjectType(string projectPath)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectPath).ToLowerInvariant();

        if (projectName.Contains("orm") || projectName.Contains("entityframework") || projectName.Contains("ef") || projectName.Contains("dapper"))
            return "orm";
        
        if (projectName.Contains("database") || projectName.Contains("db") || projectName.Contains("sql"))
            return "database";
        
        if (projectName.Contains("migration") || projectName.Contains("migrations"))
            return "migration";
        
        if (projectName.Contains("integration"))
            return "integration";
        
        if (projectName.Contains("api") || projectName.Contains("webapi") || projectName.Contains("rest"))
            return "api";
        
        if (projectName.Contains("ui") || projectName.Contains("web") || projectName.Contains("client"))
            return "ui";

        return null;
    }

    private async Task<List<string>> FindAllProjectsInSolutionAsync(string solutionPath)
    {
        var projects = new List<string>();
        
        if (!File.Exists(solutionPath))
        {
            _logger.LogWarning("Solution file not found: {SolutionPath}", solutionPath);
            return projects;
        }

        _logger.LogDebug("Parsing solution file: {SolutionPath}", solutionPath);
        var solutionContent = await File.ReadAllTextAsync(solutionPath);
        var lines = solutionContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var solutionDir = Path.GetDirectoryName(solutionPath)!;
        
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("Project(") && trimmedLine.Contains(".csproj"))
            {
                // Parse: Project("{GUID}") = "ProjectName", "relative\path\Project.csproj", "{GUID}"
                var parts = trimmedLine.Split(',');
                if (parts.Length >= 2)
                {
                    var projectRelativePath = parts[1].Trim().Trim('"');
                    var fullProjectPath = Path.Combine(solutionDir, projectRelativePath.Replace('\\', Path.DirectorySeparatorChar));
                    
                    if (File.Exists(fullProjectPath))
                    {
                        _logger.LogDebug("Found project: {ProjectPath}", fullProjectPath);
                        projects.Add(fullProjectPath);
                    }
                }
            }
        }

        _logger.LogInformation("Found {Count} total projects in solution", projects.Count);
        return projects;
    }

    private async Task<bool> IsTestProjectAsync(string projectPath)
    {
        try
        {
            if (!File.Exists(projectPath))
                return false;

            var projectContent = await File.ReadAllTextAsync(projectPath);
            
            // Check for direct test framework package references (more specific)
            var primaryTestFrameworks = new[]
            {
                "<PackageReference Include=\"Microsoft.NET.Test.Sdk\"",
                "<PackageReference Include=\"xunit\"",
                "<PackageReference Include=\"NUnit\"", 
                "<PackageReference Include=\"MSTest\"",
                "<IsTestProject>true</IsTestProject>"
            };

            foreach (var framework in primaryTestFrameworks)
            {
                if (projectContent.Contains(framework, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Check for secondary test-specific packages (less common in production code)
            var secondaryTestFrameworks = new[]
            {
                "FluentAssertions",
                "Moq", "NSubstitute", 
                "AutoFixture",
                "Shouldly"
            };

            foreach (var framework in secondaryTestFrameworks)
            {
                if (projectContent.Contains($"<PackageReference Include=\"{framework}\"", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Check project name patterns (be more specific to avoid false positives)
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            if (projectName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase) ||
                projectName.EndsWith("Test", StringComparison.OrdinalIgnoreCase) ||
                projectName.EndsWith("Specs", StringComparison.OrdinalIgnoreCase) ||
                projectName.EndsWith("Spec", StringComparison.OrdinalIgnoreCase) ||
                projectName.Contains(".Tests.", StringComparison.OrdinalIgnoreCase) ||
                projectName.Contains(".Test.", StringComparison.OrdinalIgnoreCase) ||
                projectName.Contains(".Specs.", StringComparison.OrdinalIgnoreCase) ||
                projectName.Contains(".Spec.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Check for IsTestProject property
            if (projectContent.Contains("<IsTestProject>true</IsTestProject>", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to determine if project is a test project: {ProjectPath}", projectPath);
            return false;
        }
    }
}