using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Models;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Models;
using TestIntelligence.Core.Utilities;
using TestIntelligence.SelectionEngine.Models;

namespace TestIntelligence.CLI.Services;

/// <summary>
/// Coordinator service that orchestrates the overall analysis workflow by coordinating between
/// assembly discovery, test analysis, and result formatting.
/// </summary>
public interface IAnalysisCoordinatorService
{
    /// <summary>
    /// Performs a complete analysis workflow for the given path.
    /// </summary>
    Task<AnalysisResult> PerformAnalysisAsync(string path, bool verbose, TestIntelConfiguration configuration);
}

/// <summary>
/// Implementation of analysis coordination that manages the overall analysis workflow.
/// </summary>
public class AnalysisCoordinatorService : IAnalysisCoordinatorService
{
    private readonly ILogger<AnalysisCoordinatorService> _logger;
    private readonly IAssemblyDiscoveryService _assemblyDiscoveryService;
    private readonly ITestAnalysisService _testAnalysisService;

    public AnalysisCoordinatorService(
        ILogger<AnalysisCoordinatorService> logger,
        IAssemblyDiscoveryService assemblyDiscoveryService,
        ITestAnalysisService testAnalysisService)
    {
        _logger = ExceptionHelper.ThrowIfNull(logger, nameof(logger));
        _assemblyDiscoveryService = ExceptionHelper.ThrowIfNull(assemblyDiscoveryService, nameof(assemblyDiscoveryService));
        _testAnalysisService = ExceptionHelper.ThrowIfNull(testAnalysisService, nameof(testAnalysisService));
    }

    public async Task<AnalysisResult> PerformAnalysisAsync(string path, bool verbose, TestIntelConfiguration configuration)
    {
        ExceptionHelper.ThrowIfNullOrWhiteSpace(path, nameof(path));
        ExceptionHelper.ThrowIfNull(configuration, nameof(configuration));

        _logger.LogInformation("Starting coordinated analysis of: {Path}", path);

        var result = new AnalysisResult
        {
            AnalyzedPath = path,
            Timestamp = DateTimeOffset.UtcNow,
            Assemblies = new List<AssemblyAnalysis>()
        };

        try
        {
            // Phase 1: Discover assemblies
            var assemblyPaths = await _assemblyDiscoveryService.DiscoverAssembliesAsync(path, configuration);
            _logger.LogInformation("Found {Count} assemblies to analyze", assemblyPaths.Count);

            if (!assemblyPaths.Any())
            {
                _logger.LogWarning("No assemblies found for analysis at: {Path}", path);
                return result;
            }

            // Phase 2: Analyze assemblies
            await AnalyzeAssembliesAsync(assemblyPaths, verbose, result);

            // Phase 3: Generate summary
            result.Summary = GenerateAnalysisSummary(result.Assemblies);

            _logger.LogInformation("Analysis coordination completed: {TotalTests} tests across {TotalAssemblies} assemblies",
                result.Summary.TotalTestMethods, result.Summary.TotalAssemblies);
        }
        catch (Exception ex)
        {
            ExceptionHelper.LogException(_logger, ex, "coordinating analysis", new { path, verbose });
            throw;
        }

        return result;
    }

    private async Task AnalyzeAssembliesAsync(IReadOnlyList<string> assemblyPaths, bool verbose, AnalysisResult result)
    {
        // Use a shared loader to avoid assembly resolution conflicts
        using var sharedLoader = new CrossFrameworkAssemblyLoader();

        foreach (var assemblyPath in assemblyPaths)
        {
            try
            {
                _logger.LogDebug("Starting analysis of assembly: {AssemblyPath}", assemblyPath);
                
                var assemblyAnalysis = await _testAnalysisService.AnalyzeAssemblyAsync(assemblyPath, verbose, sharedLoader);
                
                _logger.LogDebug("Assembly {AssemblyName} analysis completed: {TestCount} tests found",
                    Path.GetFileName(assemblyPath), assemblyAnalysis.TestMethods.Count);
                
                result.Assemblies.Add(assemblyAnalysis);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze assembly: {Assembly}", assemblyPath);

                // Add failed analysis to results for transparency
                result.Assemblies.Add(new AssemblyAnalysis
                {
                    AssemblyPath = assemblyPath,
                    Error = ex.Message,
                    TestMethods = new List<TestMethodAnalysis>()
                });
            }
        }
    }

    private AnalysisSummary GenerateAnalysisSummary(IReadOnlyList<AssemblyAnalysis> assemblies)
    {
        var summary = new AnalysisSummary
        {
            TotalAssemblies = assemblies.Count,
            TotalTestMethods = assemblies.Sum(a => a.TestMethods.Count),
            SuccessfullyAnalyzed = assemblies.Count(a => string.IsNullOrEmpty(a.Error)),
            FailedAnalyses = assemblies.Count(a => !string.IsNullOrEmpty(a.Error)),
            CategoryBreakdown = CalculateCategoryBreakdown(assemblies)
        };

        _logger.LogDebug("Generated analysis summary: {Summary}", new
        {
            summary.TotalAssemblies,
            summary.TotalTestMethods,
            summary.SuccessfullyAnalyzed,
            summary.FailedAnalyses
        });

        return summary;
    }

    private Dictionary<TestCategory, int> CalculateCategoryBreakdown(IReadOnlyList<AssemblyAnalysis> assemblies)
    {
        var breakdown = new Dictionary<TestCategory, int>();

        foreach (var assembly in assemblies)
        {
            foreach (var testMethod in assembly.TestMethods)
            {
                breakdown[testMethod.Category] = breakdown.GetValueOrDefault(testMethod.Category, 0) + 1;
            }
        }

        return breakdown;
    }
}