using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private readonly ITestValidationService _validationService;

        public CompareTestsCommandHandler(
            ILogger<CompareTestsCommandHandler> logger,
            ITestComparisonService comparisonService,
            IProgressReporter progressReporter,
            ITestValidationService validationService) : base(logger)
        {
            _comparisonService = comparisonService ?? throw new ArgumentNullException(nameof(comparisonService));
            _progressReporter = progressReporter ?? throw new ArgumentNullException(nameof(progressReporter));
            _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
        }

        protected override async Task<int> ExecuteInternalAsync(CommandContext context, CancellationToken cancellationToken)
        {
            // Extract parameters from context - clustering parameters are optional
            var test1 = context.GetParameter<string>("test1");
            var test2 = context.GetParameter<string>("test2");
            var tests = context.GetParameter<string>("tests");
            var scope = context.GetParameter<string>("scope");
            var target = context.GetParameter<string>("target");
            var similarityThreshold = context.GetParameter<double?>("similarity-threshold") ?? 0.6;
            var clusterAlgorithm = context.GetParameter<string>("cluster-algorithm") ?? "hierarchical";
            var solution = context.GetParameter<string>("solution") ?? throw new ArgumentException("solution parameter is required");
            var format = context.GetParameter<string>("format") ?? "text";
            var output = context.GetParameter<string>("output");
            var depth = context.GetParameter<string>("depth") ?? "medium";
            var verbose = context.GetParameter<bool>("verbose");
            var includePerformance = context.GetParameter<bool>("include-performance");
            var timeoutSeconds = context.GetParameter<int?>("timeout-seconds");

            // Validate inputs
            var command = new CompareTestsCommand
            {
                Test1 = test1 ?? string.Empty,
                Test2 = test2 ?? string.Empty,
                Tests = tests,
                Scope = scope,
                Target = target,
                SimilarityThreshold = similarityThreshold,
                ClusterAlgorithm = clusterAlgorithm,
                Solution = solution,
                Format = format,
                Output = output,
                Depth = depth,
                Verbose = verbose,
                IncludePerformance = includePerformance,
                TimeoutSeconds = timeoutSeconds
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
                // Determine operation mode
                var isTwoTestComparison = !string.IsNullOrWhiteSpace(command.Test1) && !string.IsNullOrWhiteSpace(command.Test2);
                var isClusterAnalysis = !string.IsNullOrWhiteSpace(command.Tests) || !string.IsNullOrWhiteSpace(command.Scope);

                // Perform early test validation to fail fast for invalid test method names
                if (isTwoTestComparison)
                {
                    var validationResult = await PerformEarlyValidationAsync(command, cancellationToken);
                    if (validationResult != 0)
                        return validationResult;
                        
                    return await ExecutePairwiseComparisonAsync(command, cancellationToken);
                }
                else if (isClusterAnalysis)
                {
                    var validationResult = await PerformEarlyValidationForClusteringAsync(command, cancellationToken);
                    if (validationResult != 0)
                        return validationResult;
                        
                    return await ExecuteClusterAnalysisAsync(command, cancellationToken);
                }
                else
                {
                    Console.Error.WriteLine("❌ Invalid operation mode. This should have been caught by validation.");
                    return 1;
                }
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

        private async Task<int> ExecutePairwiseComparisonAsync(CompareTestsCommand command, CancellationToken cancellationToken)
        {
            // Configure comparison options based on command parameters
            var options = new ComparisonOptions
            {
                Depth = ParseAnalysisDepth(command.Depth)
            };

            // Create timeout cancellation token if specified
            using var timeoutCts = command.TimeoutSeconds.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;

            if (timeoutCts != null)
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(command.TimeoutSeconds!.Value));
                Console.WriteLine($"⏱️ Analysis will timeout after {command.TimeoutSeconds.Value} seconds");
            }

            var effectiveCancellationToken = timeoutCts?.Token ?? cancellationToken;

            try
            {
                // Set up progress reporting for long-running operations
                Console.WriteLine("🔍 Initializing pairwise comparison analysis...");
                
                // Perform the comparison with timeout
                Console.WriteLine("📊 Analyzing test coverage and dependencies...");
                var result = await _comparisonService.CompareTestsAsync(
                    command.Test1, command.Test2, command.Solution, options, effectiveCancellationToken);

                Console.WriteLine("📝 Formatting results...");
                
                // Format and output results
                await OutputResultsAsync(result, command.Format, command.Output, command.Verbose, command.IncludePerformance);
                
                Console.WriteLine("✅ Pairwise comparison completed successfully");
                
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
            catch (OperationCanceledException) when (timeoutCts?.Token.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine($"❌ Analysis timed out after {command.TimeoutSeconds} seconds");
                Console.Error.WriteLine("💡 Try using a larger timeout with --timeout-seconds or reduce analysis depth with --depth shallow");
                return 124; // Timeout error code
            }
        }

        private async Task<int> ExecuteClusterAnalysisAsync(CompareTestsCommand command, CancellationToken cancellationToken)
        {
            // Configure clustering options based on command parameters
            var clusteringOptions = new ClusteringOptions
            {
                Algorithm = ParseClusteringAlgorithm(command.ClusterAlgorithm),
                SimilarityThreshold = command.SimilarityThreshold,
                ComparisonOptions = new ComparisonOptions
                {
                    Depth = ParseAnalysisDepth(command.Depth)
                }
            };

            // Create timeout cancellation token if specified
            using var timeoutCts = command.TimeoutSeconds.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;

            if (timeoutCts != null)
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(command.TimeoutSeconds!.Value));
                Console.WriteLine($"⏱️ Cluster analysis will timeout after {command.TimeoutSeconds.Value} seconds");
            }

            var effectiveCancellationToken = timeoutCts?.Token ?? cancellationToken;

            try
            {
                // Set up progress reporting for long-running operations
                Console.WriteLine("🔍 Initializing cluster analysis...");
                
                // Determine test IDs based on scope or explicit list
                var testIds = GetTestIds(command);
                
                // Perform the cluster analysis with timeout
                Console.WriteLine("📊 Analyzing test clusters and similarities...");
                var result = await _comparisonService.AnalyzeTestClustersAsync(
                    testIds, command.Solution, clusteringOptions, effectiveCancellationToken);

                Console.WriteLine("📝 Formatting cluster analysis results...");
                
                // Format and output results
                await OutputClusterResultsAsync(result, command.Format, command.Output, command.Verbose, command.IncludePerformance);
                
                Console.WriteLine("✅ Cluster analysis completed successfully");
                
                Logger.LogInformation("Cluster analysis completed successfully, found {ClusterCount} clusters", 
                    result.Clusters.Count);

                return 0;
            }
            catch (OperationCanceledException) when (timeoutCts?.Token.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine($"❌ Cluster analysis timed out after {command.TimeoutSeconds} seconds");
                Console.Error.WriteLine("💡 Try using a larger timeout with --timeout-seconds or reduce the number of tests");
                return 124; // Timeout error code
            }
        }

        private static IEnumerable<string> GetTestIds(CompareTestsCommand command)
        {
            if (!string.IsNullOrWhiteSpace(command.Tests))
            {
                // Parse comma-separated test list
                return command.Tests.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim());
            }
            
            if (!string.IsNullOrWhiteSpace(command.Scope) && !string.IsNullOrWhiteSpace(command.Target))
            {
                // This is a placeholder - in a real implementation, you would discover tests based on scope/target
                // For now, return empty to let the service handle discovery
                return Enumerable.Empty<string>();
            }
            
            return Enumerable.Empty<string>();
        }

        private static ClusteringAlgorithm ParseClusteringAlgorithm(string algorithm)
        {
            return algorithm.ToLowerInvariant() switch
            {
                "hierarchical" => ClusteringAlgorithm.Hierarchical,
                "kmeans" => ClusteringAlgorithm.KMeans,
                "dbscan" => ClusteringAlgorithm.DBSCAN,
                _ => ClusteringAlgorithm.Hierarchical
            };
        }

        private async Task OutputClusterResultsAsync(TestClusterAnalysis result, string format, string? outputPath, bool verbose, bool includePerformance)
        {
            try
            {
                string content = format.ToLowerInvariant() switch
                {
                    "json" => await FormatClusterAsJsonAsync(result),
                    "text" => FormatClusterAsText(result, verbose, includePerformance),
                    _ => throw new ArgumentException($"Unsupported format for cluster analysis: {format}")
                };

                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    await File.WriteAllTextAsync(outputPath, content);
                    Console.WriteLine($"✅ Cluster analysis results written to: {outputPath}");
                }
                else
                {
                    Console.WriteLine(content);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to format or write cluster analysis output");
                throw;
            }
        }

        private async Task<string> FormatClusterAsJsonAsync(TestClusterAnalysis result)
        {
            return await Task.Run(() => 
                System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                }));
        }

        private string FormatClusterAsText(TestClusterAnalysis result, bool verbose, bool includePerformance)
        {
            var output = new System.Text.StringBuilder();
            
            output.AppendLine("═══ Test Cluster Analysis Report ═══");
            output.AppendLine();
            output.AppendLine($"Analysis completed: {result.AnalysisTimestamp:yyyy-MM-dd HH:mm:ss} UTC");
            output.AppendLine($"Total tests analyzed: {result.Statistics.TotalTests}");
            output.AppendLine($"Clusters found: {result.Clusters.Count}");
            output.AppendLine($"Algorithm used: {result.Options.Algorithm}");
            output.AppendLine();

            for (int i = 0; i < result.Clusters.Count; i++)
            {
                var cluster = result.Clusters[i];
                output.AppendLine($"── Cluster {i + 1} ──");
                output.AppendLine($"Tests in cluster: {cluster.TestIds.Count}");
                output.AppendLine($"Average similarity: {cluster.IntraClusterSimilarity:P1}");
                
                if (verbose)
                {
                    output.AppendLine("Test members:");
                    foreach (var testId in cluster.TestIds)
                    {
                        output.AppendLine($"  • {testId}");
                    }
                }
                output.AppendLine();
            }

            if (includePerformance)
            {
                output.AppendLine("── Performance Metrics ──");
                output.AppendLine($"Analysis duration: {result.AnalysisDuration:mm\\:ss\\.fff}");
                output.AppendLine();
            }

            return output.ToString();
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

        /// <summary>
        /// Performs early validation for pairwise test comparison to fail fast on invalid test methods.
        /// </summary>
        private async Task<int> PerformEarlyValidationAsync(CompareTestsCommand command, CancellationToken cancellationToken)
        {
            Console.WriteLine("🔍 Validating test method identifiers...");

            try
            {
                // Create timeout token for validation (5 seconds max)
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

                // Check if we can discover any tests at all first
                var availableTests = await _validationService.DiscoverAvailableTestsAsync(command.Solution, timeoutCts.Token);
                if (availableTests.Count == 0)
                {
                    Console.WriteLine("⚠️ No tests discovered in solution - skipping validation (analysis may discover tests differently)");
                    return 0; // Let the analysis proceed and handle the case where no tests are found
                }

                var testIds = new[] { command.Test1, command.Test2 };
                var validationResult = await _validationService.ValidateTestsAsync(testIds, command.Solution, timeoutCts.Token);

                var invalidTests = validationResult.InvalidTests;
                if (invalidTests.Count > 0)
                {
                    Console.Error.WriteLine($"❌ Found {invalidTests.Count} invalid test method(s):");
                    Console.Error.WriteLine();

                    foreach (var invalidTest in invalidTests)
                    {
                        Console.Error.WriteLine($"   • {invalidTest.TestMethodId}");
                        Console.Error.WriteLine($"     Error: {invalidTest.ErrorMessage}");
                        
                        if (invalidTest.Suggestions?.Count > 0)
                        {
                            Console.Error.WriteLine($"     💡 Did you mean:");
                            foreach (var suggestion in invalidTest.Suggestions.Take(3))
                            {
                                Console.Error.WriteLine($"        - {suggestion}");
                            }
                        }
                        Console.Error.WriteLine();
                    }

                    Console.Error.WriteLine("💡 Use exact test method identifiers in format: 'Namespace.ClassName.MethodName'");
                    return 3; // Test not found error code
                }

                Console.WriteLine($"✅ Test validation completed in {validationResult.TotalValidationDuration.TotalMilliseconds:F0}ms");
                return 0; // Success
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine("❌ Validation was cancelled");
                return 130;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("❌ Test validation timed out after 5 seconds");
                Console.Error.WriteLine("💡 This usually indicates an issue with solution loading or test discovery");
                return 124; // Timeout error code
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to perform early test validation");
                Console.Error.WriteLine($"❌ Validation failed: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Performs early validation for clustering analysis to fail fast on invalid test methods.
        /// </summary>
        private async Task<int> PerformEarlyValidationForClusteringAsync(CompareTestsCommand command, CancellationToken cancellationToken)
        {
            Console.WriteLine("🔍 Validating test methods for clustering analysis...");

            try
            {
                // Create timeout token for validation (10 seconds max for potentially more tests)
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

                // Get test IDs to validate
                var testIds = GetTestIds(command).ToList();
                
                if (testIds.Count == 0)
                {
                    // If no explicit tests provided, skip validation (will be handled by discovery)
                    Console.WriteLine("ℹ️ No explicit test methods provided - discovery will validate during analysis");
                    return 0;
                }

                // Check if we can discover any tests at all first
                var availableTests = await _validationService.DiscoverAvailableTestsAsync(command.Solution, timeoutCts.Token);
                if (availableTests.Count == 0)
                {
                    Console.WriteLine("⚠️ No tests discovered in solution - skipping validation (analysis may discover tests differently)");
                    return 0; // Let the analysis proceed and handle the case where no tests are found
                }

                var validationResult = await _validationService.ValidateTestsAsync(testIds, command.Solution, timeoutCts.Token);

                var invalidTests = validationResult.InvalidTests;
                if (invalidTests.Count > 0)
                {
                    Console.Error.WriteLine($"❌ Found {invalidTests.Count} invalid test method(s) out of {testIds.Count}:");
                    Console.Error.WriteLine();

                    // Show first few invalid tests with suggestions
                    foreach (var invalidTest in invalidTests.Take(5))
                    {
                        Console.Error.WriteLine($"   • {invalidTest.TestMethodId}");
                        Console.Error.WriteLine($"     Error: {invalidTest.ErrorMessage}");
                        
                        if (invalidTest.Suggestions?.Count > 0)
                        {
                            Console.Error.WriteLine($"     💡 Did you mean: {string.Join(", ", invalidTest.Suggestions.Take(2))}");
                        }
                    }

                    if (invalidTests.Count > 5)
                    {
                        Console.Error.WriteLine($"   ... and {invalidTests.Count - 5} more invalid tests");
                    }

                    Console.Error.WriteLine();
                    Console.Error.WriteLine("💡 Use exact test method identifiers in format: 'Namespace.ClassName.MethodName'");
                    
                    // For clustering, we could potentially continue with valid tests, but for now fail completely
                    return 3; // Test not found error code
                }

                Console.WriteLine($"✅ Validation completed for {testIds.Count} test(s) in {validationResult.TotalValidationDuration.TotalMilliseconds:F0}ms");
                return 0; // Success
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine("❌ Validation was cancelled");
                return 130;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("❌ Test validation timed out after 10 seconds");
                Console.Error.WriteLine("💡 This usually indicates an issue with solution loading or test discovery");
                return 124; // Timeout error code
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to perform early test validation for clustering");
                Console.Error.WriteLine($"❌ Validation failed: {ex.Message}");
                return 1;
            }
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