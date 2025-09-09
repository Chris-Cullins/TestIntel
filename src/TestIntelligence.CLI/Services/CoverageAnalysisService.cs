using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.ImpactAnalyzer.Services;

namespace TestIntelligence.CLI.Services
{
    /// <summary>
    /// Service for analyzing test coverage of code changes via CLI.
    /// </summary>
    public interface ICoverageAnalysisService
    {
        Task AnalyzeCoverageAsync(
            string solutionPath,
            IEnumerable<string> testMethodIds,
            string? diffContent = null,
            string? diffFile = null,
            string? gitCommand = null,
            string? output = null,
            string format = "text",
            bool verbose = false);
    }

    public class CoverageAnalysisService : ICoverageAnalysisService
    {
        private readonly ICodeChangeCoverageAnalyzer _coverageAnalyzer;
        private readonly ILogger<CoverageAnalysisService> _logger;

        public CoverageAnalysisService(
            ICodeChangeCoverageAnalyzer coverageAnalyzer,
            ILogger<CoverageAnalysisService> logger)
        {
            _coverageAnalyzer = coverageAnalyzer ?? throw new ArgumentNullException(nameof(coverageAnalyzer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task AnalyzeCoverageAsync(
            string solutionPath,
            IEnumerable<string> testMethodIds,
            string? diffContent = null,
            string? diffFile = null,
            string? gitCommand = null,
            string? output = null,
            string format = "text",
            bool verbose = false)
        {
            if (string.IsNullOrWhiteSpace(solutionPath))
                throw new ArgumentException("Solution path is required", nameof(solutionPath));

            var testIds = testMethodIds?.ToList() ?? new List<string>();
            if (!testIds.Any())
                throw new ArgumentException("At least one test method ID is required", nameof(testMethodIds));

            // Validate that exactly one diff source is provided
            var diffSources = new[] { diffContent, diffFile, gitCommand }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (diffSources.Count != 1)
                throw new ArgumentException("Exactly one diff source must be provided (--diff-content, --diff-file, or --git-command)");

            try
            {
                Console.WriteLine("TestIntelligence - Code Change Coverage Analysis");
                Console.WriteLine("=".PadRight(50, '='));
                Console.WriteLine($"Solution: {solutionPath}");
                Console.WriteLine($"Tests to analyze: {testIds.Count}");
                
                Console.WriteLine("Test methods:");
                foreach (var testId in testIds)
                {
                    Console.WriteLine($"  • {testId}");
                }
                
                Console.WriteLine();

                // Perform the analysis based on the diff source
                var result = await PerformAnalysisAsync(diffContent, diffFile, gitCommand, testIds, solutionPath);

                // Format and display results
                var formattedOutput = FormatResults(result, format, verbose);
                
                if (!string.IsNullOrWhiteSpace(output))
                {
                    await File.WriteAllTextAsync(output, formattedOutput);
                    Console.WriteLine($"Results written to: {output}");
                }
                else
                {
                    Console.WriteLine(formattedOutput);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing coverage");
                Console.WriteLine($"Error: {ex.Message}");
                if (verbose)
                {
                    Console.WriteLine($"Details: {ex}");
                }
                throw;
            }
        }

        private async Task<ImpactAnalyzer.Analysis.CodeChangeCoverageResult> PerformAnalysisAsync(
            string? diffContent,
            string? diffFile,
            string? gitCommand,
            List<string> testIds,
            string solutionPath)
        {
            Console.WriteLine("Analyzing code changes and test coverage using optimized incremental analysis...");

            // Create progress reporter
            var progress = new Progress<string>(message =>
            {
                Console.WriteLine($"[Progress] {message}");
            });

            if (!string.IsNullOrWhiteSpace(diffContent))
            {
                Console.WriteLine("Using provided diff content with incremental analysis");
                return await _coverageAnalyzer.AnalyzeCoverageIncrementalAsync(diffContent, testIds, solutionPath, progress);
            }
            else if (!string.IsNullOrWhiteSpace(diffFile))
            {
                Console.WriteLine($"Reading diff from file: {diffFile}");
                return await _coverageAnalyzer.AnalyzeCoverageFromFileAsync(diffFile, testIds, solutionPath);
            }
            else if (!string.IsNullOrWhiteSpace(gitCommand))
            {
                Console.WriteLine($"Executing git command: {gitCommand}");
                return await _coverageAnalyzer.AnalyzeCoverageFromGitCommandAsync(gitCommand, testIds, solutionPath);
            }
            else
            {
                throw new InvalidOperationException("No diff source provided");
            }
        }

        private string FormatResults(
            ImpactAnalyzer.Analysis.CodeChangeCoverageResult result,
            string format,
            bool verbose)
        {
            if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                return FormatAsJson(result);
            }
            else
            {
                return FormatAsText(result, verbose);
            }
        }

        private string FormatAsJson(ImpactAnalyzer.Analysis.CodeChangeCoverageResult result)
        {
            var jsonObject = new
            {
                summary = new
                {
                    coveragePercentage = result.CoveragePercentage,
                    totalChangedMethods = result.TotalChangedMethods,
                    coveredChangedMethods = result.CoveredChangedMethods,
                    uncoveredChangedMethods = result.UncoveredChangedMethods,
                    analyzedAt = result.AnalyzedAt,
                    solutionPath = result.SolutionPath
                },
                codeChanges = new
                {
                    totalChanges = result.CodeChanges.Changes.Count,
                    changedFiles = result.CodeChanges.GetChangedFiles().ToList(),
                    changedMethods = result.CodeChanges.GetChangedMethods().ToList(),
                    changedTypes = result.CodeChanges.GetChangedTypes().ToList()
                },
                testCoverage = new
                {
                    providedTestCount = result.ProvidedTests.Count,
                    coverageByTestType = result.CoverageByTestType,
                    confidenceBreakdown = new
                    {
                        high = result.ConfidenceBreakdown.HighConfidence,
                        medium = result.ConfidenceBreakdown.MediumConfidence,
                        low = result.ConfidenceBreakdown.LowConfidence,
                        average = result.ConfidenceBreakdown.AverageConfidence
                    }
                },
                uncovered = new
                {
                    methods = result.UncoveredMethods.ToList(),
                    files = result.UncoveredFiles.ToList()
                },
                recommendations = result.Recommendations.Select(r => new
                {
                    type = r.Type.ToString(),
                    description = r.Description,
                    priority = r.Priority.ToString(),
                    affectedItemCount = r.AffectedItems.Count
                }).ToList()
            };

            return JsonSerializer.Serialize(jsonObject, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }

        private string FormatAsText(ImpactAnalyzer.Analysis.CodeChangeCoverageResult result, bool verbose)
        {
            var output = new System.Text.StringBuilder();

            // Summary
            output.AppendLine("COVERAGE ANALYSIS RESULTS");
            output.AppendLine("=".PadRight(50, '='));
            output.AppendLine($"Overall Coverage: {result.CoveragePercentage:F1}%");
            output.AppendLine($"Changed Methods: {result.CoveredChangedMethods}/{result.TotalChangedMethods} covered");
            output.AppendLine($"Provided Tests: {result.ProvidedTests.Count}");
            output.AppendLine();

            // Hint when coverage is 0% but changes and tests are present
            if (result.TotalChangedMethods > 0 && result.ProvidedTests.Count > 0 && Math.Abs(result.CoveragePercentage) < 0.0001)
            {
                output.AppendLine("Note: 0% coverage likely indicates the selected tests do not exercise the changed areas.");
                output.AppendLine("- Pick CLI integration tests for CLI changes");
                output.AppendLine("- Pick TestCoverageAnalyzer tests for analyzer changes");
                output.AppendLine("- Pick TestValidationService tests for validation changes");
                output.AppendLine();
            }

            // Confidence breakdown
            if (result.ConfidenceBreakdown.HighConfidence + result.ConfidenceBreakdown.MediumConfidence + result.ConfidenceBreakdown.LowConfidence > 0)
            {
                output.AppendLine("CONFIDENCE BREAKDOWN");
                output.AppendLine("-".PadRight(30, '-'));
                output.AppendLine($"High (≥0.8):   {result.ConfidenceBreakdown.HighConfidence} coverage relationships");
                output.AppendLine($"Medium (0.5-0.8): {result.ConfidenceBreakdown.MediumConfidence} coverage relationships");
                output.AppendLine($"Low (<0.5):    {result.ConfidenceBreakdown.LowConfidence} coverage relationships");
                output.AppendLine($"Average:       {result.ConfidenceBreakdown.AverageConfidence:F2}");
                output.AppendLine();
            }

            // Test type breakdown
            if (result.CoverageByTestType.Any())
            {
                output.AppendLine("COVERAGE BY TEST TYPE");
                output.AppendLine("-".PadRight(30, '-'));
                foreach (var kvp in result.CoverageByTestType.OrderByDescending(x => x.Value))
                {
                    output.AppendLine($"{kvp.Key}: {kvp.Value} coverage relationships");
                }
                output.AppendLine();
            }

            // Uncovered methods
            if (result.UncoveredMethods.Any())
            {
                output.AppendLine("UNCOVERED METHODS");
                output.AppendLine("-".PadRight(30, '-'));
                foreach (var method in result.UncoveredMethods)
                {
                    output.AppendLine($"⚠️  {method}");
                }
                output.AppendLine();
            }

            // Uncovered files
            if (result.UncoveredFiles.Any())
            {
                output.AppendLine("FILES WITH NO TEST COVERAGE");
                output.AppendLine("-".PadRight(30, '-'));
                foreach (var file in result.UncoveredFiles)
                {
                    output.AppendLine($"📁 {file}");
                }
                output.AppendLine();
            }

            // Recommendations
            if (result.Recommendations.Any())
            {
                output.AppendLine("RECOMMENDATIONS");
                output.AppendLine("-".PadRight(30, '-'));
                foreach (var recommendation in result.Recommendations.OrderByDescending(r => r.Priority))
                {
                    var priorityIcon = recommendation.Priority switch
                    {
                        ImpactAnalyzer.Analysis.CoverageRecommendationPriority.High => "🔴",
                        ImpactAnalyzer.Analysis.CoverageRecommendationPriority.Medium => "🟡",
                        ImpactAnalyzer.Analysis.CoverageRecommendationPriority.Low => "🟢",
                        _ => "ℹ️"
                    };
                    output.AppendLine($"{priorityIcon} {recommendation.Description}");
                    
                    if (verbose && recommendation.AffectedItems.Any())
                    {
                        foreach (var item in recommendation.AffectedItems.Take(5))
                        {
                            output.AppendLine($"   • {item}");
                        }
                        if (recommendation.AffectedItems.Count > 5)
                        {
                            output.AppendLine($"   ... and {recommendation.AffectedItems.Count - 5} more");
                        }
                    }
                }
                output.AppendLine();
            }

            // Detailed method coverage (verbose mode)
            if (verbose && result.MethodCoverage.Any())
            {
                output.AppendLine("DETAILED METHOD COVERAGE");
                output.AppendLine("-".PadRight(50, '-'));
                foreach (var kvp in result.MethodCoverage)
                {
                    output.AppendLine($"Method: {kvp.Key}");
                    foreach (var test in kvp.Value)
                    {
                        output.AppendLine($"  ✅ {test.TestClassName}.{test.TestMethodName}");
                        output.AppendLine($"     Confidence: {test.Confidence:F2}, Depth: {test.CallDepth}, Type: {test.TestType}");
                        if (test.CallDepth > 1)
                        {
                            output.AppendLine($"     Path: {test.GetCallPathDisplay()}");
                        }
                    }
                    output.AppendLine();
                }
            }

            return output.ToString();
        }
    }
}
