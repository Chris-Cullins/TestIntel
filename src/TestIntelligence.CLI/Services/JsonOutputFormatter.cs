using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using TestIntelligence.CLI.Models;

namespace TestIntelligence.CLI.Services;

/// <summary>
/// Output formatter that supports JSON and text formats.
/// </summary>
public class JsonOutputFormatter : IOutputFormatter
{
    private readonly ILogger<JsonOutputFormatter> _logger;
    private readonly JsonSerializerSettings _jsonSettings;

    public JsonOutputFormatter(ILogger<JsonOutputFormatter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatHandling = DateFormatHandling.IsoDateFormat
        };
        _jsonSettings.Converters.Add(new StringEnumConverter());
    }

    public string FormatAsJson(object data)
    {
        return JsonConvert.SerializeObject(data, _jsonSettings);
    }

    public string FormatAsText(object data)
    {
        return data switch
        {
            AnalysisResult analysisResult => FormatAnalysisResult(analysisResult),
            CategorizationResult categorizationResult => FormatCategorizationResult(categorizationResult),
            SelectionResult selectionResult => FormatSelectionResult(selectionResult),
            CallGraphReport callGraphReport => FormatCallGraphReport(callGraphReport),
            _ => data.ToString() ?? string.Empty
        };
    }

    public async Task WriteOutputAsync(object data, string format, string? outputPath)
    {
        var content = format.ToLower() switch
        {
            "json" => FormatAsJson(data),
            "text" => FormatAsText(data),
            _ => throw new ArgumentException($"Unsupported format: {format}")
        };

        if (string.IsNullOrEmpty(outputPath))
        {
            Console.WriteLine(content);
        }
        else
        {
            await File.WriteAllTextAsync(outputPath, content);
            _logger.LogInformation("Output written to: {OutputPath}", outputPath);
        }
    }

    private string FormatAnalysisResult(AnalysisResult result)
    {
        var output = new System.Text.StringBuilder();
        
        output.AppendLine("=== Test Intelligence Analysis Report ===");
        output.AppendLine($"Path: {result.AnalyzedPath}");
        output.AppendLine($"Timestamp: {result.Timestamp:yyyy-MM-dd HH:mm:ss}");
        output.AppendLine();

        if (result.Summary != null)
        {
            output.AppendLine("=== Summary ===");
            output.AppendLine($"Total Assemblies: {result.Summary.TotalAssemblies}");
            output.AppendLine($"Total Test Methods: {result.Summary.TotalTestMethods}");
            output.AppendLine($"Successfully Analyzed: {result.Summary.SuccessfullyAnalyzed}");
            output.AppendLine($"Failed Analyses: {result.Summary.FailedAnalyses}");
            output.AppendLine();

            output.AppendLine("=== Category Breakdown ===");
            foreach (var category in result.Summary.CategoryBreakdown.OrderByDescending(kv => kv.Value))
            {
                output.AppendLine($"  {category.Key}: {category.Value} tests");
            }
            output.AppendLine();
        }

        output.AppendLine("=== Assembly Details ===");
        foreach (var assembly in result.Assemblies)
        {
            output.AppendLine($"Assembly: {Path.GetFileName(assembly.AssemblyPath)}");
            output.AppendLine($"  Framework: {assembly.Framework ?? "Unknown"}");
            output.AppendLine($"  Test Methods: {assembly.TestMethods.Count}");
            
            if (!string.IsNullOrEmpty(assembly.Error))
            {
                output.AppendLine($"  Error: {assembly.Error}");
            }
            else
            {
                var categoryGroups = assembly.TestMethods.GroupBy(t => t.Category);
                foreach (var group in categoryGroups)
                {
                    output.AppendLine($"    {group.Key}: {group.Count()} tests");
                }
            }
            output.AppendLine();
        }

        return output.ToString();
    }

    private string FormatCategorizationResult(CategorizationResult result)
    {
        var output = new System.Text.StringBuilder();
        
        output.AppendLine("=== Test Categorization Report ===");
        output.AppendLine($"Path: {result.AnalyzedPath}");
        output.AppendLine($"Timestamp: {result.Timestamp:yyyy-MM-dd HH:mm:ss}");
        output.AppendLine($"Total Tests: {result.TotalTests}");
        output.AppendLine();

        foreach (var category in result.Categories.OrderByDescending(kv => kv.Value.Count))
        {
            output.AppendLine($"=== {category.Key} Tests ({category.Value.Count}) ===");
            foreach (var test in category.Value.Take(10)) // Limit to first 10 for readability
            {
                output.AppendLine($"  • {test}");
            }
            
            if (category.Value.Count > 10)
            {
                output.AppendLine($"  ... and {category.Value.Count - 10} more");
            }
            output.AppendLine();
        }

        return output.ToString();
    }

    private string FormatSelectionResult(SelectionResult result)
    {
        var output = new System.Text.StringBuilder();
        
        output.AppendLine("=== Test Selection Report ===");
        output.AppendLine($"Path: {result.AnalyzedPath}");
        output.AppendLine($"Timestamp: {result.Timestamp:yyyy-MM-dd HH:mm:ss}");
        output.AppendLine($"Confidence Level: {result.ConfidenceLevel}");
        
        if (result.ChangedFiles.Length > 0)
        {
            output.AppendLine($"Changed Files: {string.Join(", ", result.ChangedFiles.Select(Path.GetFileName))}");
        }
        output.AppendLine();

        if (result.Summary != null)
        {
            output.AppendLine("=== Selection Summary ===");
            output.AppendLine($"Selected Tests: {result.Summary.TotalSelectedTests}");
            output.AppendLine($"Estimated Duration: {FormatDuration(result.Summary.EstimatedTotalDuration)}");
            output.AppendLine($"Average Score: {result.Summary.AverageSelectionScore:F3}");
            output.AppendLine($"Optimal Parallelism: {result.Summary.OptimalParallelism}");
            output.AppendLine();

            output.AppendLine("=== Category Breakdown ===");
            foreach (var category in result.Summary.CategoryBreakdown.OrderByDescending(kv => kv.Value))
            {
                output.AppendLine($"  {category.Key}: {category.Value} tests");
            }
            output.AppendLine();
        }

        output.AppendLine("=== Selected Tests ===");
        foreach (var test in result.SelectedTests.OrderByDescending(t => t.SelectionScore))
        {
            output.AppendLine($"• {test.TestName}");
            output.AppendLine($"  Score: {test.SelectionScore:F3} | Category: {test.Category} | Duration: {FormatDuration(test.EstimatedDuration)}");
            
            if (test.Tags.Count > 0)
            {
                output.AppendLine($"  Tags: {string.Join(", ", test.Tags)}");
            }
            output.AppendLine();
        }

        return output.ToString();
    }

    private string FormatCallGraphReport(CallGraphReport report)
    {
        var output = new System.Text.StringBuilder();
        
        output.AppendLine("=== Method Call Graph Analysis Report ===");
        output.AppendLine($"Total Methods Found: {report.TotalMethods}");
        output.AppendLine($"Total Source Files: {report.TotalSourceFiles}");
        output.AppendLine($"Showing: {report.ShowingMethodCount} methods");
        if (report.ShowingMethodCount < report.TotalMethods)
        {
            output.AppendLine($"... and {report.TotalMethods - report.ShowingMethodCount} more methods");
        }
        output.AppendLine();

        // Methods making most calls
        if (report.MethodsWithMostCalls.Count > 0)
        {
            output.AppendLine("=== Methods Making Most Calls ===");
            foreach (var method in report.MethodsWithMostCalls)
            {
                var info = method.MethodInfo;
                if (info != null)
                {
                    output.AppendLine($"• {info.ContainingType}.{info.Name}: {method.CallCount} calls");
                    output.AppendLine($"  Location: {Path.GetFileName(info.FilePath)}:{info.LineNumber}");
                }
            }
            output.AppendLine();
        }

        // Most called methods
        if (report.MostCalledMethods.Count > 0)
        {
            output.AppendLine("=== Most Called Methods ===");
            foreach (var method in report.MostCalledMethods)
            {
                var info = method.MethodInfo;
                if (info != null)
                {
                    output.AppendLine($"• {info.ContainingType}.{info.Name}: called by {method.DependentCount} methods");
                    output.AppendLine($"  Location: {Path.GetFileName(info.FilePath)}:{info.LineNumber}");
                }
            }
            output.AppendLine();
        }

        // Method details (verbose mode)
        if (report.IsVerbose && report.MethodDetails.Count > 0)
        {
            output.AppendLine("=== Method Details ===");
            foreach (var method in report.MethodDetails)
            {
                var info = method.MethodInfo;
                if (info != null)
                {
                    output.AppendLine($"Method: {info.Name}");
                    output.AppendLine($"  Type: {info.ContainingType}");
                    output.AppendLine($"  Location: {Path.GetFileName(info.FilePath)}:{info.LineNumber}");
                    
                    if (method.CallCount > 0)
                    {
                        output.AppendLine($"  Calls ({method.CallCount}):");
                        foreach (var call in method.Calls.Take(5))
                        {
                            output.AppendLine($"    → {call.ContainingType}.{call.Name}");
                        }
                        if (method.Calls.Count > 5)
                        {
                            output.AppendLine($"    ... and {method.Calls.Count - 5} more");
                        }
                    }
                    
                    if (method.DependentCount > 0)
                    {
                        output.AppendLine($"  Called by ({method.DependentCount}):");
                        foreach (var dependent in method.Dependents.Take(3))
                        {
                            output.AppendLine($"    ← {dependent.ContainingType}.{dependent.Name}");
                        }
                        if (method.Dependents.Count > 3)
                        {
                            output.AppendLine($"    ... and {method.Dependents.Count - 3} more");
                        }
                    }
                    
                    output.AppendLine();
                }
            }
        }
        else if (!report.IsVerbose && report.MethodDetails.Count > 0)
        {
            output.AppendLine("=== Method Summary ===");
            foreach (var method in report.MethodDetails.Take(20))
            {
                var info = method.MethodInfo;
                if (info != null)
                {
                    output.AppendLine($"• {info.ContainingType}.{info.Name}");
                    output.AppendLine($"  Calls: {method.CallCount} | Called by: {method.DependentCount} | {Path.GetFileName(info.FilePath)}:{info.LineNumber}");
                }
            }
            
            if (report.MethodDetails.Count > 20)
            {
                output.AppendLine($"... and {report.MethodDetails.Count - 20} more methods");
            }
            output.AppendLine();
            output.AppendLine("Use --verbose for detailed method call information");
        }

        return output.ToString();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes >= 1)
        {
            return $"{duration.TotalMinutes:F1}m";
        }
        else
        {
            return $"{duration.TotalSeconds:F0}s";
        }
    }
}