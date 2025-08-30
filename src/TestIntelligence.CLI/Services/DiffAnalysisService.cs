using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TestIntelligence.ImpactAnalyzer.Services;
using System.Linq;

namespace TestIntelligence.CLI.Services
{
    public interface IDiffAnalysisService
    {
        Task AnalyzeDiffAsync(string solutionPath, string? diffContent, string? diffFile, string? gitCommand, string? output, string format, bool verbose);
    }

    public class DiffAnalysisService : IDiffAnalysisService
    {
        private readonly ILogger<DiffAnalysisService> _logger;
        private readonly ISimplifiedDiffImpactAnalyzer _diffImpactAnalyzer;
        private readonly IOutputFormatter _outputFormatter;

        public DiffAnalysisService(
            ILogger<DiffAnalysisService> logger,
            ISimplifiedDiffImpactAnalyzer diffImpactAnalyzer,
            IOutputFormatter outputFormatter)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _diffImpactAnalyzer = diffImpactAnalyzer ?? throw new ArgumentNullException(nameof(diffImpactAnalyzer));
            _outputFormatter = outputFormatter ?? throw new ArgumentNullException(nameof(outputFormatter));
        }

        public async Task AnalyzeDiffAsync(string solutionPath, string? diffContent, string? diffFile, string? gitCommand, string? output, string format, bool verbose)
        {
            try
            {
                if (!File.Exists(solutionPath))
                {
                    Console.Error.WriteLine($"Solution file not found: {solutionPath}");
                    return;
                }

                if (verbose)
                    _logger.LogInformation("Starting diff analysis for solution: {SolutionPath}", solutionPath);

                // Validate that exactly one diff source is provided
                var sourceCount = (string.IsNullOrEmpty(diffContent) ? 0 : 1) + 
                                 (string.IsNullOrEmpty(diffFile) ? 0 : 1) + 
                                 (string.IsNullOrEmpty(gitCommand) ? 0 : 1);

                if (sourceCount != 1)
                {
                    Console.Error.WriteLine("Please provide exactly one diff source: --diff-content, --diff-file, or --git-command");
                    return;
                }

                // Perform the appropriate analysis
                SimplifiedTestImpactResult result;

                if (!string.IsNullOrEmpty(diffContent))
                {
                    if (verbose)
                        Console.WriteLine("Analyzing diff content...");
                    result = await _diffImpactAnalyzer.AnalyzeDiffImpactAsync(diffContent, solutionPath);
                }
                else if (!string.IsNullOrEmpty(diffFile))
                {
                    if (verbose)
                        Console.WriteLine($"Analyzing diff from file: {diffFile}");
                    result = await _diffImpactAnalyzer.AnalyzeDiffFileImpactAsync(diffFile, solutionPath);
                }
                else if (!string.IsNullOrEmpty(gitCommand))
                {
                    if (verbose)
                        Console.WriteLine($"Analyzing diff from git command: {gitCommand}");
                    result = await _diffImpactAnalyzer.AnalyzeGitDiffImpactAsync(gitCommand, solutionPath);
                }
                else
                {
                    Console.Error.WriteLine("No diff source provided");
                    return;
                }

                // Format and output the results
                await OutputResultsAsync(result, output, format, verbose);

                if (verbose)
                {
                    Console.WriteLine($"\nAnalysis completed. Found {result.TotalImpactedTests} potentially impacted tests from {result.TotalChanges} code changes.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze diff impact");
                Console.Error.WriteLine($"Error: {ex.Message}");
                if (verbose)
                    Console.Error.WriteLine($"Details: {ex}");
            }
        }

        private async Task OutputResultsAsync(SimplifiedTestImpactResult result, string? outputPath, string format, bool verbose)
        {
            var outputContent = format.ToLower() switch
            {
                "json" => FormatAsJson(result, verbose),
                "text" => FormatAsText(result, verbose),
                _ => FormatAsText(result, verbose)
            };

            if (string.IsNullOrEmpty(outputPath))
            {
                Console.WriteLine(outputContent);
            }
            else
            {
                await File.WriteAllTextAsync(outputPath, outputContent);
                if (verbose)
                    Console.WriteLine($"Results written to: {outputPath}");
            }
        }

        private string FormatAsJson(SimplifiedTestImpactResult result, bool verbose)
        {
            var output = new
            {
                Summary = new
                {
                    TotalChanges = result.TotalChanges,
                    TotalFiles = result.TotalFiles,
                    TotalMethods = result.TotalMethods,
                    TotalImpactedTests = result.TotalImpactedTests,
                    AnalyzedAt = result.AnalyzedAt
                },
                CodeChanges = result.CodeChanges.Changes.Select(change => new
                {
                    FilePath = change.FilePath,
                    ChangeType = change.ChangeType.ToString(),
                    ChangedMethods = change.ChangedMethods,
                    ChangedTypes = change.ChangedTypes,
                    DetectedAt = change.DetectedAt
                }),
                AffectedMethods = result.AffectedMethods,
                ImpactedTests = result.ImpactedTests.Select(test => new
                {
                    Id = test.GetUniqueId(),
                    MethodName = test.MethodName,
                    TypeName = test.TypeName,
                    Namespace = test.Namespace,
                    AssemblyPath = test.AssemblyName,
                    Confidence = Math.Round(test.Confidence, 3),
                    ImpactReasons = test.ImpactReasons
                })
            };

            return JsonConvert.SerializeObject(output, verbose ? Formatting.Indented : Formatting.None);
        }

        private string FormatAsText(SimplifiedTestImpactResult result, bool verbose)
        {
            var output = new System.Text.StringBuilder();
            
            output.AppendLine("=== Test Impact Analysis Results ===");
            output.AppendLine($"Analyzed at: {result.AnalyzedAt:yyyy-MM-dd HH:mm:ss} UTC");
            output.AppendLine();
            
            output.AppendLine("=== Summary ===");
            output.AppendLine($"Code Changes: {result.TotalChanges} changes across {result.TotalFiles} files");
            output.AppendLine($"Changed Methods: {result.TotalMethods}");
            output.AppendLine($"Potentially Impacted Tests: {result.TotalImpactedTests}");
            output.AppendLine();

            if (verbose && result.CodeChanges.Changes.Any())
            {
                output.AppendLine("=== Code Changes ===");
                foreach (var change in result.CodeChanges.Changes)
                {
                    output.AppendLine($"[{change.ChangeType}] {change.FilePath}");
                    if (change.ChangedMethods.Any())
                        output.AppendLine($"  Methods: {string.Join(", ", change.ChangedMethods)}");
                    if (change.ChangedTypes.Any())
                        output.AppendLine($"  Types: {string.Join(", ", change.ChangedTypes)}");
                }
                output.AppendLine();
            }

            if (result.ImpactedTests.Any())
            {
                output.AppendLine("=== Impacted Tests ===");
                
                var highConfidenceTests = result.ImpactedTests.Where(t => t.Confidence >= 0.7).ToList();
                var mediumConfidenceTests = result.ImpactedTests.Where(t => t.Confidence >= 0.4 && t.Confidence < 0.7).ToList();
                var lowConfidenceTests = result.ImpactedTests.Where(t => t.Confidence < 0.4).ToList();

                if (highConfidenceTests.Any())
                {
                    output.AppendLine("High Confidence (≥70%):");
                    foreach (var test in highConfidenceTests)
                    {
                        output.AppendLine($"  [{test.Confidence:P0}] {test.TypeName}.{test.MethodName}");
                        if (verbose)
                            output.AppendLine($"    Reasons: {test.ImpactReasons}");
                    }
                    output.AppendLine();
                }

                if (mediumConfidenceTests.Any())
                {
                    output.AppendLine("Medium Confidence (40-69%):");
                    foreach (var test in mediumConfidenceTests)
                    {
                        output.AppendLine($"  [{test.Confidence:P0}] {test.TypeName}.{test.MethodName}");
                        if (verbose)
                            output.AppendLine($"    Reasons: {test.ImpactReasons}");
                    }
                    output.AppendLine();
                }

                if (verbose && lowConfidenceTests.Any())
                {
                    output.AppendLine("Low Confidence (<40%):");
                    foreach (var test in lowConfidenceTests)
                    {
                        output.AppendLine($"  [{test.Confidence:P0}] {test.TypeName}.{test.MethodName}");
                        output.AppendLine($"    Reasons: {test.ImpactReasons}");
                    }
                    output.AppendLine();
                }
            }
            else
            {
                output.AppendLine("No potentially impacted tests found.");
                output.AppendLine("This could mean:");
                output.AppendLine("- The changes don't affect test-covered code");
                output.AppendLine("- Tests are not yet written for the changed code");
                output.AppendLine("- The analysis couldn't establish strong connections");
            }

            return output.ToString();
        }
    }
}