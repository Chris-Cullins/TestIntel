using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TestIntelligence.CLI.Models;
using TestIntelligence.ImpactAnalyzer.Models;
using TestIntelligence.SelectionEngine.Engine;
using TestIntelligence.SelectionEngine.Interfaces;
using TestIntelligence.SelectionEngine.Models;

namespace TestIntelligence.CLI.Services;

/// <summary>
/// Service for intelligent test selection.
/// </summary>
public class SelectionService : ISelectionService
{
    private readonly ILogger<SelectionService> _logger;
    private readonly IOutputFormatter _outputFormatter;

    public SelectionService(
        ILogger<SelectionService> logger,
        IOutputFormatter outputFormatter)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _outputFormatter = outputFormatter ?? throw new ArgumentNullException(nameof(outputFormatter));
    }

    public async Task SelectAsync(string path, string[] changes, string confidence, string? outputPath, int? maxTests, string? maxTime)
    {
        try
        {
            _logger.LogInformation("Starting test selection for: {Path}", path);
            _logger.LogInformation("Confidence Level: {Confidence}", confidence);
            _logger.LogInformation("Changed Files: {Changes}", string.Join(", ", changes));

            var confidenceLevel = ParseConfidenceLevel(confidence);
            var options = CreateSelectionOptions(maxTests, maxTime);

            // Create a TestSelectionEngine instance with the solution path
            var selectionEngine = new TestSelectionEngine(_logger as ILogger<TestSelectionEngine> ?? NullLogger<TestSelectionEngine>.Instance, solutionPath: path);
            
            TestExecutionPlan executionPlan;

            if (changes.Length > 0)
            {
                var codeChangeSet = CreateCodeChangeSet(changes);
                executionPlan = await selectionEngine.GetOptimalTestPlanAsync(codeChangeSet, confidenceLevel, options);
            }
            else
            {
                executionPlan = await selectionEngine.GetTestPlanAsync(confidenceLevel, options);
            }

            var selectionResult = ConvertToSelectionResult(executionPlan, path, changes, confidence);

            await _outputFormatter.WriteOutputAsync(selectionResult, "text", outputPath);

            _logger.LogInformation("Test selection completed successfully. Selected {Count} tests", 
                selectionResult.SelectedTests.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during test selection");
            throw;
        }
    }

    private ConfidenceLevel ParseConfidenceLevel(string confidence)
    {
        return confidence.ToLower() switch
        {
            "fast" => ConfidenceLevel.Fast,
            "medium" => ConfidenceLevel.Medium,
            "high" => ConfidenceLevel.High,
            "full" => ConfidenceLevel.Full,
            _ => throw new ArgumentException($"Invalid confidence level: {confidence}. Valid values are: Fast, Medium, High, Full")
        };
    }

    private TestSelectionOptions CreateSelectionOptions(int? maxTests, string? maxTime)
    {
        var options = new TestSelectionOptions();

        if (maxTests.HasValue)
        {
            if (maxTests.Value <= 0)
            {
                throw new ArgumentException($"Max tests must be a positive number, but got: {maxTests.Value}", nameof(maxTests));
            }
            
            if (maxTests.Value > 50000)
            {
                _logger.LogWarning("Very large max-tests value ({MaxTests}) specified, this may impact performance", maxTests.Value);
            }
            
            options.MaxTestCount = maxTests.Value;
            _logger.LogInformation("Limiting selection to {MaxTests} tests", maxTests.Value);
        }

        if (!string.IsNullOrEmpty(maxTime))
        {
            try
            {
                var timeSpan = ParseTimeSpan(maxTime);
                
                if (timeSpan <= TimeSpan.Zero)
                {
                    throw new ArgumentException($"Max time must be positive, but got: {maxTime}", nameof(maxTime));
                }
                
                if (timeSpan > TimeSpan.FromHours(24))
                {
                    _logger.LogWarning("Very large max-time value ({MaxTime}) specified", maxTime);
                }
                
                options.MaxExecutionTime = timeSpan;
                _logger.LogInformation("Limiting execution time to {MaxTime}", timeSpan);
            }
            catch (Exception ex) when (!(ex is ArgumentException))
            {
                throw new ArgumentException($"Invalid time format: {maxTime}. Use formats like '30s', '5m', '1h'", nameof(maxTime), ex);
            }
        }

        return options;
    }

    private TimeSpan ParseTimeSpan(string timeString)
    {
        if (string.IsNullOrWhiteSpace(timeString))
        {
            throw new ArgumentException("Time string cannot be null or empty", nameof(timeString));
        }

        var time = timeString.ToLower().Trim();
        
        if (time.EndsWith("s"))
        {
            var valueString = time.Substring(0, time.Length - 1);
            if (!int.TryParse(valueString, out var seconds) || seconds < 0)
            {
                throw new ArgumentException($"Invalid seconds value: '{valueString}'. Must be a non-negative integer");
            }
            return TimeSpan.FromSeconds(seconds);
        }
        
        if (time.EndsWith("m"))
        {
            var valueString = time.Substring(0, time.Length - 1);
            if (!int.TryParse(valueString, out var minutes) || minutes < 0)
            {
                throw new ArgumentException($"Invalid minutes value: '{valueString}'. Must be a non-negative integer");
            }
            return TimeSpan.FromMinutes(minutes);
        }
        
        if (time.EndsWith("h"))
        {
            var valueString = time.Substring(0, time.Length - 1);
            if (!int.TryParse(valueString, out var hours) || hours < 0)
            {
                throw new ArgumentException($"Invalid hours value: '{valueString}'. Must be a non-negative integer");
            }
            return TimeSpan.FromHours(hours);
        }

        // Try to parse as seconds if no suffix
        if (int.TryParse(time, out var totalSeconds) && totalSeconds >= 0)
        {
            return TimeSpan.FromSeconds(totalSeconds);
        }

        throw new ArgumentException($"Invalid time format: '{timeString}'. Use formats like '30s', '5m', '1h', or plain seconds");
    }

    private CodeChangeSet CreateCodeChangeSet(string[] changes)
    {
        var codeChanges = new List<CodeChange>();

        foreach (var changedFile in changes)
        {
            // Simplified change detection - in production this would analyze actual changes
            var changeType = DetermineChangeType(changedFile);
            var changedMethods = new List<string>(); // Would be populated by actual analysis
            var changedTypes = new List<string>(); // Would be populated by actual analysis

            codeChanges.Add(new CodeChange(changedFile, changeType, changedMethods, changedTypes));
        }

        return new CodeChangeSet(codeChanges);
    }

    private CodeChangeType DetermineChangeType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLower();
        var fileName = Path.GetFileName(filePath).ToLower();

        if (fileName.Contains("config") || extension == ".json" || extension == ".xml")
        {
            return CodeChangeType.Configuration;
        }

        if (extension == ".cs" || extension == ".vb" || extension == ".fs")
        {
            return CodeChangeType.Modified; // Default to modified
        }

        return CodeChangeType.Modified;
    }

    private SelectionResult ConvertToSelectionResult(TestExecutionPlan plan, string path, string[] changes, string confidence)
    {
        var result = new SelectionResult
        {
            AnalyzedPath = path,
            Timestamp = DateTimeOffset.UtcNow,
            ConfidenceLevel = confidence,
            ChangedFiles = changes,
            SelectedTests = new List<SelectedTest>()
        };

        foreach (var test in plan.Tests)
        {
            result.SelectedTests.Add(new SelectedTest
            {
                TestName = test.GetDisplayName(),
                Category = test.Category,
                SelectionScore = test.SelectionScore,
                EstimatedDuration = test.AverageExecutionTime,
                Assembly = Path.GetFileName(test.TestMethod.AssemblyPath),
                Tags = test.Tags.ToList()
            });
        }

        result.Summary = new SelectionSummary
        {
            TotalSelectedTests = result.SelectedTests.Count,
            EstimatedTotalDuration = plan.EstimatedDuration,
            AverageSelectionScore = result.SelectedTests.Count > 0 ? 
                result.SelectedTests.Average(t => t.SelectionScore) : 0.0,
            CategoryBreakdown = result.SelectedTests
                .GroupBy(t => t.Category)
                .ToDictionary(g => g.Key, g => g.Count()),
            OptimalParallelism = plan.ParallelismDegree
        };

        return result;
    }
}