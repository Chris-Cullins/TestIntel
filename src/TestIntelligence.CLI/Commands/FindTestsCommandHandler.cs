using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Services;
using TestIntelligence.Core.Services;

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

        Logger.LogInformation("Finding tests that exercise method: {Method} in solution: {Solution}", method, solution);
        
        // Get services from DI
        var testCoverageAnalyzer = context.GetService<ITestCoverageAnalyzer>();
        var outputFormatter = context.GetService<IOutputFormatter>();
        
        Console.WriteLine($"Finding tests that exercise method: {method}");
        Console.WriteLine($"Solution path: {solution}");
        Console.WriteLine();

        var tests = await testCoverageAnalyzer.FindTestsExercisingMethodAsync(method!, solution!);
        
        if (!tests.Any())
        {
            Console.WriteLine("No tests found that exercise this method.");
            return 0;
        }

        Console.WriteLine($"Found {tests.Count} test(s) exercising this method:");
        Console.WriteLine();

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
}