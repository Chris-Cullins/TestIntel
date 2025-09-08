using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Models;
using TestIntelligence.Core.Models;
using TestIntelligence.SelectionEngine.Models;

namespace TestIntelligence.CLI.Services;

/// <summary>
/// Service for categorizing tests.
/// </summary>
public class CategorizationService : ICategorizationService
{
    private readonly ILogger<CategorizationService> _logger;
    private readonly IAnalysisService _analysisService;
    private readonly IOutputFormatter _outputFormatter;

    public CategorizationService(
        ILogger<CategorizationService> logger, 
        IAnalysisService analysisService,
        IOutputFormatter outputFormatter)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
        _outputFormatter = outputFormatter ?? throw new ArgumentNullException(nameof(outputFormatter));
    }

    public async Task CategorizeAsync(string path, string? outputPath)
    {
        try
        {
            _logger.LogInformation("Starting categorization of: {Path}", path);

            // Use analysis service to get test information
            var tempAnalysisPath = Path.GetTempFileName();
            await _analysisService.AnalyzeAsync(path, tempAnalysisPath, "json", false);
            
            // Read analysis result and convert to categorization result
            var analysisJson = await File.ReadAllTextAsync(tempAnalysisPath);
            var analysisResult = Newtonsoft.Json.JsonConvert.DeserializeObject<AnalysisResult>(analysisJson);
            File.Delete(tempAnalysisPath);

            if (analysisResult == null)
            {
                throw new InvalidOperationException("Failed to analyze assemblies");
            }

            var categorizationResult = ConvertToCategorizationResult(analysisResult);

            await _outputFormatter.WriteOutputAsync(categorizationResult, "text", outputPath);

            _logger.LogInformation("Categorization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during categorization");
            throw;
        }
    }

    private CategorizationResult ConvertToCategorizationResult(AnalysisResult analysisResult)
    {
        var result = new CategorizationResult
        {
            AnalyzedPath = analysisResult.AnalyzedPath,
            Timestamp = analysisResult.Timestamp,
            Categories = new Dictionary<TestCategory, List<string>>()
        };

        // Group tests by category
        var allTests = analysisResult.Assemblies
            .SelectMany(a => a.TestMethods)
            .ToList();

        foreach (var categoryGroup in allTests.GroupBy(t => t.Category))
        {
            result.Categories[categoryGroup.Key] = categoryGroup
                .Select(t => t.MethodName)
                .OrderBy(name => name)
                .ToList();
        }

        result.TotalTests = allTests.Count;

        return result;
    }
}