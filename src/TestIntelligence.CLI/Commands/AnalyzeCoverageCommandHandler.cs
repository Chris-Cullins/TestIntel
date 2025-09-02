using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Services;

namespace TestIntelligence.CLI.Commands;

/// <summary>
/// Command handler for the analyze-coverage command.
/// Analyzes how well specific tests cover code changes.
/// </summary>
public class AnalyzeCoverageCommandHandler : BaseCommandHandler
{
    public AnalyzeCoverageCommandHandler(ILogger<AnalyzeCoverageCommandHandler> logger) : base(logger)
    {
    }

    protected override async Task<int> ExecuteInternalAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Validate required parameters
        ValidateRequiredParameters(context, "solution", "tests");

        // Extract parameters
        var solution = context.GetParameter<string>("solution");
        var tests = context.GetParameter<string[]>("tests");
        var diffContent = context.GetParameter<string>("diff-content");
        var diffFile = context.GetParameter<string>("diff-file");
        var gitCommand = context.GetParameter<string>("git-command");
        var output = context.GetParameter<string>("output");
        var format = context.GetParameter<string>("format") ?? "text";
        var verbose = context.GetParameter<bool>("verbose");

        Logger.LogInformation("Analyzing coverage for {TestCount} tests in solution: {Solution}", tests?.Length ?? 0, solution);
        
        // Get the coverage analysis service from DI
        var coverageAnalysisService = context.GetService<ICoverageAnalysisService>();
        
        // Execute the coverage analysis
        await coverageAnalysisService.AnalyzeCoverageAsync(solution!, tests ?? Array.Empty<string>(), diffContent, diffFile, gitCommand, output, format, verbose);
        
        return 0;
    }
}