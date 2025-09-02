using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Services;
using TestIntelligence.Core.Interfaces;

namespace TestIntelligence.CLI.Commands;

/// <summary>
/// Command handler for the trace-execution command.
/// Traces all production code executed by a test method.
/// </summary>
public class TraceExecutionCommandHandler : BaseCommandHandler
{
    public TraceExecutionCommandHandler(ILogger<TraceExecutionCommandHandler> logger) : base(logger)
    {
    }

    protected override async Task<int> ExecuteInternalAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Validate required parameters
        ValidateRequiredParameters(context, "test", "solution");

        // Extract parameters
        var test = context.GetParameter<string>("test");
        var solution = context.GetParameter<string>("solution");
        var output = context.GetParameter<string>("output");
        var format = context.GetParameter<string>("format") ?? "text";
        var verbose = context.GetParameter<bool>("verbose");
        var maxDepth = context.GetParameter<int>("max-depth");
        
        // Use default if max-depth is not set
        if (maxDepth == 0) maxDepth = 20;

        Logger.LogInformation("Tracing execution for test: {Test} in solution: {Solution} with max depth: {MaxDepth}", test, solution, maxDepth);
        
        // Get services from DI
        var testExecutionTracer = context.GetService<ITestExecutionTracer>();
        var outputFormatter = context.GetService<IOutputFormatter>();
        
        Console.WriteLine($"Tracing execution for test method: {test}");
        Console.WriteLine($"Solution path: {solution}");
        Console.WriteLine($"Max depth: {maxDepth}");
        Console.WriteLine();

        var executionTrace = await testExecutionTracer.TraceTestExecutionAsync(test!, solution!);
        
        if (executionTrace.ExecutedMethods.Count == 0)
        {
            Console.WriteLine("No methods found in execution trace.");
            return 0;
        }

        Console.WriteLine($"Found {executionTrace.TotalMethodsCalled} method(s) in execution trace:");
        Console.WriteLine($"Production methods: {executionTrace.ProductionMethodsCalled}");
        Console.WriteLine($"Estimated complexity: {executionTrace.EstimatedExecutionComplexity}");
        Console.WriteLine();

        if (format == "json")
        {
            var json = outputFormatter.FormatAsJson(executionTrace);
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
            
            // Group by production vs non-production
            var productionMethods = executionTrace.ExecutedMethods.Where(em => em.IsProductionCode).ToList();
            var nonProductionMethods = executionTrace.ExecutedMethods.Where(em => !em.IsProductionCode).ToList();

            if (productionMethods.Any())
            {
                result.AppendLine("=== PRODUCTION CODE ===");
                result.AppendLine();
                
                foreach (var method in productionMethods.OrderBy(em => em.CallDepth))
                {
                    result.AppendLine($"• {method.ContainingType}.{method.MethodName}");
                    result.AppendLine($"  Category: {method.Category}");
                    result.AppendLine($"  Call Depth: {method.CallDepth}");
                    result.AppendLine($"  File: {Path.GetFileName(method.FilePath)}:{method.LineNumber}");
                    
                    if (verbose)
                    {
                        result.AppendLine($"  Call Path: {string.Join(" → ", method.CallPath)}");
                    }
                    
                    result.AppendLine();
                }
            }

            if (nonProductionMethods.Any() && verbose)
            {
                result.AppendLine("=== FRAMEWORK & TEST CODE ===");
                result.AppendLine();
                
                foreach (var method in nonProductionMethods.OrderBy(em => em.CallDepth))
                {
                    result.AppendLine($"• {method.ContainingType}.{method.MethodName}");
                    result.AppendLine($"  Category: {method.Category}");
                    result.AppendLine($"  Call Depth: {method.CallDepth}");
                    result.AppendLine();
                }
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