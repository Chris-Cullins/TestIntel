using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Services;
using TestIntelligence.Core.Services;
using TestIntelligence.Core.Models;

namespace TestIntelligence.CLI.Commands;

/// <summary>
/// Command handler for the find-tests command.
/// Finds all tests that exercise a given method.
/// </summary>
public class FindTestsCommandHandler : BaseCommandHandler
{
    public FindTestsCommandHandler(ILogger<FindTestsCommandHandler> logger) : base(logger)
    {
    }

    protected override async Task<int> ExecuteInternalAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Validate required parameters
        ValidateRequiredParameters(context, "method", "solution");

        // Extract parameters
        var method = context.GetParameter<string>("method");
        var solution = context.GetParameter<string>("solution");
        var output = context.GetParameter<string>("output");
        var format = context.GetParameter<string>("format") ?? "text";
        var verbose = context.GetParameter<bool>("verbose");

        // Additional validation
        ValidateInputs(method!, solution!, output, format);

        Logger.LogInformation("Finding tests that exercise method: {Method} in solution: {Solution}", method, solution);

        // Load configuration and apply find-tests traversal limits (if provided)
        try
        {
            var configService = context.GetService<IConfigurationService>();
            if (configService != null)
            {
                var config = await configService.LoadConfigurationAsync(solution);
                if (config?.Analysis != null)
                {
                    // Apply configuration by setting env vars consumed by the analyzer
                    Environment.SetEnvironmentVariable("TI_MAX_PATH_DEPTH", config.Analysis.FindTestsMaxPathDepth.ToString());
                    Environment.SetEnvironmentVariable("TI_MAX_VISITED_NODES", config.Analysis.FindTestsMaxVisitedNodes.ToString());
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to load/apply configuration for find-tests; using defaults");
        }
        
        // Get services from DI
        var testCoverageAnalyzer = context.GetService<ITestCoverageAnalyzer>();
        var outputFormatter = context.GetService<IOutputFormatter>();
        
        if (testCoverageAnalyzer == null)
        {
            throw new InvalidOperationException("Test coverage analyzer service is not available. Please check the application configuration.");
        }
        
        if (outputFormatter == null)
        {
            throw new InvalidOperationException("Output formatter service is not available. Please check the application configuration.");
        }
        
        Console.WriteLine($"Finding tests that exercise method: {method}");
        Console.WriteLine($"Solution path: {solution}");
        Console.WriteLine();

        IReadOnlyList<TestCoverageInfo> tests;
        try
        {
            tests = await testCoverageAnalyzer.FindTestsExercisingMethodAsync(method!, solution!, cancellationToken);
            
            if (tests == null || !tests.Any())
            {
                Console.WriteLine("No tests found that exercise this method.");
                Console.WriteLine();
                Console.WriteLine("💡 This could mean:");
                Console.WriteLine("   • The method name/signature is incorrect");
                Console.WriteLine("   • No tests actually exercise this method");
                Console.WriteLine("   • The method is not public or accessible");
                Console.WriteLine("   • There are compilation errors preventing analysis");
                return 0;
            }

            Console.WriteLine($"Found {tests.Count} test(s) exercising this method:");
            Console.WriteLine();
        }
        catch (ArgumentException)
        {
            // Re-throw argument exceptions to be handled by base class
            throw;
        }
        catch (Exception ex) when (ex.Message.Contains("No test methods found"))
        {
            Console.WriteLine("No test methods found in the solution.");
            Console.WriteLine("💡 Make sure the solution builds successfully and contains test projects.");
            return 0;
        }

        if (format == "json")
        {
            var json = outputFormatter.FormatAsJson(tests);
            if (!string.IsNullOrWhiteSpace(output))
            {
                await File.WriteAllTextAsync(output, json, cancellationToken);
                Console.WriteLine($"Results written to: {output}");
            }
            else
            {
                Console.WriteLine(json);
            }
        }
        else
        {
            var result = new StringBuilder();
            
            foreach (var test in tests.OrderByDescending(t => t.Confidence))
            {
                result.AppendLine($"• {test.TestClassName}.{test.TestMethodName}");
                result.AppendLine($"  Assembly: {test.TestAssembly}");
                result.AppendLine($"  Type: {test.TestType}");
                result.AppendLine($"  Confidence: {test.Confidence:F2}");
                result.AppendLine($"  Call Depth: {test.CallDepth}");
                
                if (verbose)
                {
                    result.AppendLine($"  Call Path: {test.GetCallPathDisplay()}");
                }
                
                result.AppendLine();
            }

            var textOutput = result.ToString();
            if (!string.IsNullOrWhiteSpace(output))
            {
                await File.WriteAllTextAsync(output, textOutput, cancellationToken);
                Console.WriteLine($"Results written to: {output}");
            }
            else
            {
                Console.WriteLine(textOutput);
            }
        }
        
        return 0;
    }

    /// <summary>
    /// Validates the input parameters for the find-tests command.
    /// </summary>
    private void ValidateInputs(string method, string solution, string? output, string format)
    {
        // Validate solution file exists
        if (!File.Exists(solution))
        {
            throw new FileNotFoundException($"Solution file not found: {solution}");
        }

        // Validate method format (basic validation)
        if (!method.Contains('.'))
        {
            throw new ArgumentException($"Method parameter should be in format 'Namespace.Class.Method', got: {method}");
        }

        // Validate output format
        if (!string.Equals(format, "json", StringComparison.OrdinalIgnoreCase) && 
            !string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Format must be 'json' or 'text', got: {format}");
        }

        // Validate output path if provided
        if (!string.IsNullOrWhiteSpace(output))
        {
            var outputDir = Path.GetDirectoryName(Path.GetFullPath(output));
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                throw new DirectoryNotFoundException($"Output directory does not exist: {outputDir}");
            }
        }
    }

    protected override void PrintUsageHint(CommandContext context)
    {
        Console.Error.WriteLine("💡 Usage: find-tests --method \"Namespace.Class.Method\" --solution \"path/to/solution.sln\"");
        Console.Error.WriteLine("   Example: find-tests --method \"MyApp.Services.UserService.GetUser\" --solution \"MyApp.sln\"");
    }
}
