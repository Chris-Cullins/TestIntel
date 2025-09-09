using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Progress;
using TestIntelligence.TestComparison.Models;
using TestIntelligence.TestComparison.Services;

namespace TestIntelligence.CLI.Commands
{
    /// <summary>
    /// Command handler for comparing two test methods and analyzing their overlap.
    /// Integrates with ITestComparisonService to perform detailed comparison analysis.
    /// </summary>
    public class CompareTestsCommandHandler : BaseCommandHandler
    {
        private readonly ITestComparisonService _comparisonService;
        private readonly IProgressReporter _progressReporter;

        public CompareTestsCommandHandler(
            ILogger<CompareTestsCommandHandler> logger,
            ITestComparisonService comparisonService,
            IProgressReporter progressReporter) : base(logger)
        {
            _comparisonService = comparisonService ?? throw new ArgumentNullException(nameof(comparisonService));
            _progressReporter = progressReporter ?? throw new ArgumentNullException(nameof(progressReporter));
        }

        protected override async Task<int> ExecuteInternalAsync(CommandContext context, CancellationToken cancellationToken)
        {
            // Extract parameters from context
            var test1 = context.GetParameter<string>("test1") ?? throw new ArgumentException("test1 parameter is required");
            var test2 = context.GetParameter<string>("test2") ?? throw new ArgumentException("test2 parameter is required");
            var solution = context.GetParameter<string>("solution") ?? throw new ArgumentException("solution parameter is required");
            var format = context.GetParameter<string>("format") ?? "text";
            var output = context.GetParameter<string>("output");
            var depth = context.GetParameter<string>("depth") ?? "medium";
            var verbose = context.GetParameter<bool>("verbose");
            var includePerformance = context.GetParameter<bool>("include-performance");
            // Timeout functionality temporarily removed for CLI argument limit

            // Validate inputs
            var command = new CompareTestsCommand
            {
                Test1 = test1,
                Test2 = test2,
                Solution = solution,
                Format = format,
                Output = output,
                Depth = depth,
                Verbose = verbose,
                IncludePerformance = includePerformance
            };

            var validationErrors = command.Validate();
            if (validationErrors.Count > 0)
            {
                Console.Error.WriteLine("❌ Validation errors:");
                foreach (var error in validationErrors)
                {
                    Console.Error.WriteLine($"   • {error}");
                }
                return 1;
            }

            // Validate solution file exists
            if (!File.Exists(solution) && !Directory.Exists(solution))
            {
                Console.Error.WriteLine($"❌ Solution path not found: {solution}");
                return 2;
            }

            Logger.LogInformation("Starting test comparison: {Test1} vs {Test2}", test1, test2);

            try
            {
                // Configure comparison options based on command parameters
                var options = new ComparisonOptions
                {
                    Depth = ParseAnalysisDepth(depth)
                };

                // Set up progress reporting for long-running operations
                Console.WriteLine("🔍 Initializing comparison analysis...");
                
                // Set up cancellation token (timeout functionality temporarily removed)
                var combinedCts = cancellationToken;

                // Perform the comparison
                Console.WriteLine("📊 Analyzing test coverage and dependencies...");
                var result = await _comparisonService.CompareTestsAsync(
                    test1, test2, solution, options, combinedCts);

                Console.WriteLine("📝 Formatting results...");
                
                // Format and output results
                await OutputResultsAsync(result, format, output, verbose, includePerformance);
                
                Console.WriteLine("✅ Comparison completed successfully");
                
                Logger.LogInformation("Test comparison completed successfully in {Duration}ms", 
                    result.AnalysisDuration.TotalMilliseconds);

                // Show warnings if any
                if (result.Warnings?.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("⚠️ Warnings:");
                    foreach (var warning in result.Warnings)
                    {
                        Console.WriteLine($"   • {warning}");
                    }
                }

                return 0;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("❌ Operation was cancelled");
                return 130; // Standard cancellation exit code
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"❌ Invalid test method identifier: {ex.Message}");
                Console.Error.WriteLine("💡 Use format: 'Namespace.ClassName.MethodName'");
                return 1;
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine($"❌ Required file not found: {ex.FileName}");
                return 2;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("test method not found"))
            {
                Console.Error.WriteLine($"❌ Test method not found: {ex.Message}");
                Console.Error.WriteLine("💡 Verify test method identifiers and ensure they exist in the solution");
                return 3;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error during test comparison");
                Console.Error.WriteLine($"❌ Comparison failed: {ex.Message}");
                
                if (verbose)
                {
                    Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                }
                return 1;
            }
        }

        private static AnalysisDepth ParseAnalysisDepth(string depth)
        {
            return depth.ToLowerInvariant() switch
            {
                "shallow" => AnalysisDepth.Shallow,
                "medium" => AnalysisDepth.Medium,
                "deep" => AnalysisDepth.Deep,
                _ => AnalysisDepth.Medium
            };
        }

        private async Task OutputResultsAsync(TestComparisonResult result, string format, string? outputPath, bool verbose, bool includePerformance)
        {
            try
            {
                string content = format.ToLowerInvariant() switch
                {
                    "json" => await FormatAsJsonAsync(result),
                    "text" => FormatAsText(result, verbose, includePerformance),
                    _ => throw new ArgumentException($"Unsupported format: {format}")
                };

                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    await File.WriteAllTextAsync(outputPath, content);
                    Console.WriteLine($"✅ Results written to: {outputPath}");
                }
                else
                {
                    Console.WriteLine(content);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to format or write output");
                throw;
            }
        }

        private async Task<string> FormatAsJsonAsync(TestComparisonResult result)
        {
            return await Task.Run(() => 
                System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                }));
        }

        private string FormatAsText(TestComparisonResult result, bool verbose, bool includePerformance)
        {
            var output = new System.Text.StringBuilder();
            
            output.AppendLine("═══ Test Comparison Report ═══");
            output.AppendLine();
            output.AppendLine($"Test 1: {result.Test1Id}");
            output.AppendLine($"Test 2: {result.Test2Id}");
            output.AppendLine($"Analysis Time: {result.AnalysisDuration.TotalMilliseconds:F0}ms");
            output.AppendLine($"Timestamp: {result.AnalysisTimestamp:yyyy-MM-dd HH:mm:ss} UTC");
            output.AppendLine();
            
            // Overall similarity
            var similarityBar = CreateProgressBar(result.OverallSimilarity, 20);
            output.AppendLine($"Overall Similarity: {result.OverallSimilarity:P1} {similarityBar}");
            output.AppendLine(result.GetSummary());
            output.AppendLine();
            
            // Coverage overlap details
            output.AppendLine("── Coverage Overlap Analysis ──");
            output.AppendLine($"Shared Production Methods: {result.CoverageOverlap.SharedProductionMethods}");
            output.AppendLine($"Overlap Percentage: {result.CoverageOverlap.OverlapPercentage:F1}%");
            
            var overlapBar = CreateProgressBar(result.CoverageOverlap.OverlapPercentage / 100.0, 20);
            output.AppendLine($"Coverage Overlap: {overlapBar}");
            output.AppendLine();
            
            // Metadata similarity
            output.AppendLine("── Metadata Similarity ──");
            output.AppendLine($"Overall Score: {result.MetadataSimilarity.OverallScore:P1}");
            output.AppendLine($"Category Alignment: {result.MetadataSimilarity.CategoryAlignmentScore:P1}");
            output.AppendLine($"Tag Similarity: {result.MetadataSimilarity.TagOverlapScore:P1}");
            output.AppendLine($"Naming Similarity: {result.MetadataSimilarity.NamingPatternScore:P1}");
            output.AppendLine();
            
            // Recommendations
            if (result.Recommendations.Count > 0)
            {
                output.AppendLine("── Optimization Recommendations ──");
                for (int i = 0; i < result.Recommendations.Count; i++)
                {
                    var rec = result.Recommendations[i];
                    var effort = GetEffortIcon(rec.EstimatedEffortLevel);
                    output.AppendLine($"{i + 1}. {effort} {rec.Type}: {rec.Description}");
                    output.AppendLine($"   Confidence: {rec.ConfidenceScore:P0} | Effort: {rec.EstimatedEffortLevel}");
                    
                    if (verbose && !string.IsNullOrWhiteSpace(rec.Rationale))
                    {
                        output.AppendLine($"   Rationale: {rec.Rationale}");
                    }
                    output.AppendLine();
                }
            }
            
            // Performance metrics if requested
            if (includePerformance)
            {
                output.AppendLine("── Performance Analysis ──");
                output.AppendLine($"Analysis Duration: {result.AnalysisDuration:mm\\:ss\\.fff}");
                output.AppendLine($"Analysis Depth: {result.Options.Depth}");
                output.AppendLine();
            }

            return output.ToString();
        }

        private static string CreateProgressBar(double value, int width)
        {
            var filledWidth = (int)(value * width);
            var filled = new string('█', Math.Max(0, filledWidth));
            var empty = new string('░', Math.Max(0, width - filledWidth));
            return $"[{filled}{empty}]";
        }

        private static string GetEffortIcon(EstimatedEffortLevel effort)
        {
            return effort switch
            {
                EstimatedEffortLevel.High => "🔴",
                EstimatedEffortLevel.Medium => "🟡", 
                EstimatedEffortLevel.Low => "🟢",
                _ => "⚪"
            };
        }

        protected override void PrintUsageHint(CommandContext context)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("💡 Compare Tests Usage:");
            Console.Error.WriteLine("   testintel compare-tests --test1 \"MyTests.UnitTest1\" --test2 \"MyTests.UnitTest2\" --solution \"path/to/solution.sln\"");
            Console.Error.WriteLine();
            Console.Error.WriteLine("   Optional parameters:");
            Console.Error.WriteLine("     --format text|json     Output format (default: text)");
            Console.Error.WriteLine("     --output <file>        Output file path");
            Console.Error.WriteLine("     --depth shallow|medium|deep    Analysis depth (default: medium)");
            Console.Error.WriteLine("     --verbose              Enable verbose output");
            Console.Error.WriteLine("     --include-performance  Include performance metrics");
            Console.Error.WriteLine("     --timeout-seconds <n>  Analysis timeout");
        }
    }
}