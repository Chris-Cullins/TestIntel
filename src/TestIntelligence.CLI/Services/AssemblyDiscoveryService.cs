using System.Linq;
using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Models;
using TestIntelligence.Core.Services;
using TestIntelligence.Core.Utilities;

namespace TestIntelligence.CLI.Services;

/// <summary>
/// Service responsible for discovering assemblies from various sources (solutions, directories, single files).
/// </summary>
public interface IAssemblyDiscoveryService
{
    /// <summary>
    /// Discovers assemblies to analyze based on the input path and configuration.
    /// </summary>
    Task<IReadOnlyList<string>> DiscoverAssembliesAsync(string path, TestIntelConfiguration configuration);
}

/// <summary>
/// Implementation of assembly discovery that handles solution files, directories, and individual assemblies.
/// </summary>
public class AssemblyDiscoveryService : IAssemblyDiscoveryService
{
    private readonly ILogger<AssemblyDiscoveryService> _logger;
    private readonly IAssemblyPathResolver _assemblyPathResolver;
    private readonly IProjectAnalysisService _projectAnalysisService;
    private readonly IConfigurationService _configurationService;

    public AssemblyDiscoveryService(
        ILogger<AssemblyDiscoveryService> logger,
        IAssemblyPathResolver assemblyPathResolver,
        IProjectAnalysisService projectAnalysisService,
        IConfigurationService configurationService)
    {
        _logger = ExceptionHelper.ThrowIfNull(logger, nameof(logger));
        _assemblyPathResolver = ExceptionHelper.ThrowIfNull(assemblyPathResolver, nameof(assemblyPathResolver));
        _projectAnalysisService = ExceptionHelper.ThrowIfNull(projectAnalysisService, nameof(projectAnalysisService));
        _configurationService = ExceptionHelper.ThrowIfNull(configurationService, nameof(configurationService));
    }

    public async Task<IReadOnlyList<string>> DiscoverAssembliesAsync(string path, TestIntelConfiguration configuration)
    {
        ExceptionHelper.ThrowIfNullOrWhiteSpace(path, nameof(path));
        ExceptionHelper.ThrowIfNull(configuration, nameof(configuration));

        var assemblies = new List<string>();

        if (File.Exists(path))
        {
            assemblies.AddRange(await DiscoverFromFileAsync(path, configuration));
        }
        else if (Directory.Exists(path))
        {
            assemblies.AddRange(DiscoverFromDirectory(path, configuration));
        }
        else
        {
            throw new FileNotFoundException($"Path not found: {path}");
        }

        return assemblies.Distinct().ToList();
    }

    private async Task<IEnumerable<string>> DiscoverFromFileAsync(string filePath, TestIntelConfiguration configuration)
    {
        if (filePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { filePath };
        }

        if (filePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return await DiscoverFromSolutionAsync(filePath, configuration);
        }

        _logger.LogWarning("Unsupported file type for analysis: {FilePath}", filePath);
        return Array.Empty<string>();
    }

    private async Task<IEnumerable<string>> DiscoverFromSolutionAsync(string solutionPath, TestIntelConfiguration configuration)
    {
        _logger.LogDebug("Discovering assemblies from solution: {SolutionPath}", solutionPath);

        var allProjectPaths = configuration.Projects.TestProjectsOnly
            ? await _projectAnalysisService.FindTestProjectsInSolutionAsync(solutionPath)
            : await _projectAnalysisService.FindAllProjectsInSolutionAsync(solutionPath);

        if (!allProjectPaths.Any())
        {
            _logger.LogWarning("No projects found in solution: {SolutionPath}", solutionPath);
            return Array.Empty<string>();
        }

        // Apply configuration-based project filtering
        var filteredProjectPaths = _configurationService.FilterProjects(allProjectPaths.ToList(), configuration)?.ToList() ?? new List<string>();
        
        var assemblies = new List<string>();
        foreach (var projectPath in filteredProjectPaths)
        {
            var assemblyPath = _assemblyPathResolver.ResolveAssemblyPath(projectPath);
            if (File.Exists(assemblyPath))
            {
                _logger.LogDebug("Found assembly: {AssemblyPath}", assemblyPath);
                assemblies.Add(assemblyPath);
            }
            else
            {
                _logger.LogWarning("Assembly not found for project {ProjectPath}, expected at {AssemblyPath}", 
                    projectPath, assemblyPath);
            }
        }

        _logger.LogInformation("Discovered {AssemblyCount} assemblies from solution", assemblies.Count);
        return assemblies;
    }

    private IEnumerable<string> DiscoverFromDirectory(string directoryPath, TestIntelConfiguration configuration)
    {
        _logger.LogDebug("Discovering assemblies from directory: {DirectoryPath}", directoryPath);

        var dllFiles = Directory.GetFiles(directoryPath, "*.dll", SearchOption.AllDirectories)
            .Where(f => !f.Contains("obj", StringComparison.OrdinalIgnoreCase));

        // Filter by test assemblies if configured
        if (configuration.Projects.TestProjectsOnly)
        {
            dllFiles = dllFiles.Where(f =>
                f.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("spec", StringComparison.OrdinalIgnoreCase));
        }

        var assemblies = dllFiles.ToList();
        _logger.LogInformation("Discovered {AssemblyCount} assemblies from directory", assemblies.Count);
        return assemblies;
    }
}