using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Models;
using TestIntelligence.Core.Utilities;

namespace TestIntelligence.CLI.Services;

/// <summary>
/// Refactored implementation of analysis service that coordinates focused services.
/// This replaces the monolithic AnalysisService with a clean coordinator pattern.
/// </summary>
public class RefactoredAnalysisService : IAnalysisService
{
    private readonly ILogger<RefactoredAnalysisService> _logger;
    private readonly IOutputFormatter _outputFormatter;
    private readonly IConfigurationService _configurationService;
    private readonly IAnalysisCoordinatorService _analysisCoordinator;

    public RefactoredAnalysisService(
        ILogger<RefactoredAnalysisService> logger,
        IOutputFormatter outputFormatter,
        IConfigurationService configurationService,
        IAnalysisCoordinatorService analysisCoordinator)
    {
        _logger = ExceptionHelper.ThrowIfNull(logger, nameof(logger));
        _outputFormatter = ExceptionHelper.ThrowIfNull(outputFormatter, nameof(outputFormatter));
        _configurationService = ExceptionHelper.ThrowIfNull(configurationService, nameof(configurationService));
        _analysisCoordinator = ExceptionHelper.ThrowIfNull(analysisCoordinator, nameof(analysisCoordinator));
    }

    public async Task AnalyzeAsync(string path, string? outputPath, string format, bool verbose)
    {
        try
        {
            _logger.LogInformation("Starting analysis of: {Path}", path);

            // Validate input path
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new FileNotFoundException($"Path not found: {path}");
            }

            // Load and apply configuration
            var configuration = await _configurationService.LoadConfigurationAsync(path);
            var effectiveOptions = ApplyConfigurationOverrides(verbose, format, outputPath, configuration);

            if (effectiveOptions.Verbose)
            {
                _logger.LogInformation("Verbose mode enabled");
            }

            // Perform the analysis using the coordinator
            var analysisResult = await _analysisCoordinator.PerformAnalysisAsync(
                path, effectiveOptions.Verbose, configuration);

            // Format and output results
            await _outputFormatter.WriteOutputAsync(
                analysisResult, effectiveOptions.Format, effectiveOptions.OutputPath);

            _logger.LogInformation("Analysis completed successfully");
        }
        catch (Exception ex)
        {
            ExceptionHelper.LogException(_logger, ex, "analysis workflow", new { path, outputPath, format, verbose });
            throw;
        }
    }

    private EffectiveAnalysisOptions ApplyConfigurationOverrides(
        bool verbose, string format, string? outputPath, TestIntelConfiguration configuration)
    {
        // Apply configuration overrides with command-line precedence
        var effectiveVerbose = verbose || configuration.Analysis.Verbose;
        var effectiveFormat = string.IsNullOrEmpty(format) || format == "text"
            ? configuration.Output.Format
            : format;
        var effectiveOutputPath = outputPath ??
            (configuration.Output.OutputDirectory != null
                ? Path.Combine(configuration.Output.OutputDirectory, 
                    $"analysis_{DateTime.Now:yyyyMMdd_HHmmss}.{(effectiveFormat == "json" ? "json" : "txt")}")
                : null);

        return new EffectiveAnalysisOptions(effectiveVerbose, effectiveFormat, effectiveOutputPath);
    }

    private record EffectiveAnalysisOptions(bool Verbose, string Format, string? OutputPath);
}