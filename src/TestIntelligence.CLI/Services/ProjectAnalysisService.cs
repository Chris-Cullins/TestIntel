using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Utilities;

namespace TestIntelligence.CLI.Services;

/// <summary>
/// Service responsible for analyzing and parsing project files and solutions.
/// </summary>
public interface IProjectAnalysisService
{
    /// <summary>
    /// Finds all test projects in a solution.
    /// </summary>
    Task<IReadOnlyList<string>> FindTestProjectsInSolutionAsync(string solutionPath);

    /// <summary>
    /// Finds all projects in a solution.
    /// </summary>
    Task<IReadOnlyList<string>> FindAllProjectsInSolutionAsync(string solutionPath);

    /// <summary>
    /// Determines if a project file represents a test project.
    /// </summary>
    Task<bool> IsTestProjectAsync(string projectPath);

    /// <summary>
    /// Extracts target framework versions from a project file.
    /// </summary>
    Task<IReadOnlyList<string>> GetTargetFrameworksAsync(string projectPath);
}

/// <summary>
/// Implementation of project analysis that handles solution and project file parsing.
/// </summary>
public class ProjectAnalysisService : IProjectAnalysisService
{
    private readonly ILogger<ProjectAnalysisService> _logger;

    // Common test framework indicators
    private static readonly string[] TestIndicators = new[]
    {
        "Microsoft.NET.Test.Sdk",
        "xunit", "nunit", "mstest",
        "FluentAssertions", "Shouldly",
        "Moq", "NSubstitute", "FakeItEasy"
    };

    public ProjectAnalysisService(ILogger<ProjectAnalysisService> logger)
    {
        _logger = ExceptionHelper.ThrowIfNull(logger, nameof(logger));
    }

    public async Task<IReadOnlyList<string>> FindTestProjectsInSolutionAsync(string solutionPath)
    {
        ExceptionHelper.ThrowIfNullOrWhiteSpace(solutionPath, nameof(solutionPath));

        var allProjects = await FindAllProjectsInSolutionAsync(solutionPath);
        var testProjects = new List<string>();

        foreach (var projectPath in allProjects)
        {
            if (await IsTestProjectAsync(projectPath))
            {
                _logger.LogDebug("Found test project: {ProjectPath}", projectPath);
                testProjects.Add(projectPath);
            }
        }

        _logger.LogInformation("Found {Count} test projects in solution", testProjects.Count);
        return testProjects;
    }

    public async Task<IReadOnlyList<string>> FindAllProjectsInSolutionAsync(string solutionPath)
    {
        ExceptionHelper.ThrowIfNullOrWhiteSpace(solutionPath, nameof(solutionPath));

        if (!File.Exists(solutionPath))
        {
            _logger.LogWarning("Solution file not found: {SolutionPath}", solutionPath);
            return Array.Empty<string>();
        }

        var projects = new List<string>();

        try
        {
            _logger.LogDebug("Parsing solution file: {SolutionPath}", solutionPath);
            var solutionContent = await File.ReadAllTextAsync(solutionPath);
            var lines = solutionContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var solutionDir = Path.GetDirectoryName(solutionPath)!;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("Project(") && 
                    (trimmedLine.Contains(".csproj", StringComparison.OrdinalIgnoreCase) ||
                     trimmedLine.Contains(".vbproj", StringComparison.OrdinalIgnoreCase) ||
                     trimmedLine.Contains(".fsproj", StringComparison.OrdinalIgnoreCase)))
                {
                    var projectPath = ExtractProjectPathFromSolutionLine(trimmedLine, solutionDir);
                    if (!string.IsNullOrEmpty(projectPath) && File.Exists(projectPath))
                    {
                        _logger.LogDebug("Found project: {ProjectPath}", projectPath);
                        projects.Add(projectPath);
                    }
                }
            }

            _logger.LogInformation("Found {Count} total projects in solution", projects.Count);
        }
        catch (Exception ex)
        {
            ExceptionHelper.LogException(_logger, ex, "parsing solution file", new { solutionPath });
        }

        return projects;
    }

    public async Task<bool> IsTestProjectAsync(string projectPath)
    {
        ExceptionHelper.ThrowIfNullOrWhiteSpace(projectPath, nameof(projectPath));

        try
        {
            // Quick check by project name/path
            var projectName = Path.GetFileNameWithoutExtension(projectPath).ToLowerInvariant();
            if (projectName.Contains("test") || projectName.Contains("spec"))
            {
                return true;
            }

            // Check project content for test framework references
            var projectContent = await File.ReadAllTextAsync(projectPath);
            return TestIndicators.Any(indicator =>
                projectContent.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            ExceptionHelper.LogException(_logger, ex, "analyzing project file", new { projectPath });
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> GetTargetFrameworksAsync(string projectPath)
    {
        ExceptionHelper.ThrowIfNullOrWhiteSpace(projectPath, nameof(projectPath));

        var frameworks = new List<string>();

        try
        {
            if (!File.Exists(projectPath))
            {
                return frameworks;
            }

            var projectContent = await File.ReadAllTextAsync(projectPath);
            var lines = projectContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                if (trimmed.StartsWith("<TargetFramework>", StringComparison.OrdinalIgnoreCase))
                {
                    var framework = ExtractXmlElementContent(trimmed, "TargetFramework");
                    if (!string.IsNullOrEmpty(framework))
                    {
                        frameworks.Add(framework);
                    }
                }
                else if (trimmed.StartsWith("<TargetFrameworks>", StringComparison.OrdinalIgnoreCase))
                {
                    var frameworksString = ExtractXmlElementContent(trimmed, "TargetFrameworks");
                    if (!string.IsNullOrEmpty(frameworksString))
                    {
                        var multipleFrameworks = frameworksString.Split(';', StringSplitOptions.RemoveEmptyEntries)
                            .Select(f => f.Trim())
                            .Where(f => !string.IsNullOrEmpty(f));
                        frameworks.AddRange(multipleFrameworks);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ExceptionHelper.LogException(_logger, ex, "parsing target frameworks", new { projectPath });
        }

        return frameworks.Distinct().ToList();
    }

    private string? ExtractProjectPathFromSolutionLine(string solutionLine, string solutionDir)
    {
        try
        {
            // Parse: Project("{GUID}") = "ProjectName", "relative\path\Project.csproj", "{GUID}"
            var parts = solutionLine.Split(',');
            if (parts.Length >= 2)
            {
                var projectRelativePath = parts[1].Trim().Trim('"');
                var fullProjectPath = Path.Combine(solutionDir, projectRelativePath.Replace('\\', Path.DirectorySeparatorChar));
                return fullProjectPath;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract project path from solution line: {Line}", solutionLine);
        }

        return null;
    }

    private string? ExtractXmlElementContent(string line, string elementName)
    {
        var startTag = $"<{elementName}>";
        var endTag = $"</{elementName}>";

        var startIndex = line.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        var endIndex = line.IndexOf(endTag, StringComparison.OrdinalIgnoreCase);

        if (startIndex >= 0 && endIndex > startIndex)
        {
            var contentStart = startIndex + startTag.Length;
            var contentLength = endIndex - contentStart;
            return line.Substring(contentStart, contentLength).Trim();
        }

        return null;
    }
}
