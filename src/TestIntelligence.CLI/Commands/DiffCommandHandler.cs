using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Services;

namespace TestIntelligence.CLI.Commands;

/// <summary>
/// Command handler for the diff command.
/// Analyzes test impact from git diff or patch files.
/// </summary>
public class DiffCommandHandler : BaseCommandHandler
{
    public DiffCommandHandler(ILogger<DiffCommandHandler> logger) : base(logger)
    {
    }

    protected override async Task<int> ExecuteInternalAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Validate required parameters
        ValidateRequiredParameters(context, "solution");

        // Extract parameters
        var solution = context.GetParameter<string>("solution");
        var diffContent = context.GetParameter<string>("diff-content");
        var diffFile = context.GetParameter<string>("diff-file");
        var gitCommand = context.GetParameter<string>("git-command");
        var output = context.GetParameter<string>("output");
        var format = context.GetParameter<string>("format") ?? "text";
        var verbose = context.GetParameter<bool>("verbose");

        Logger.LogInformation("Analyzing diff impact for solution: {Solution}", solution);
        
        // Get the diff analysis service from DI
        var diffAnalysisService = context.GetService<IDiffAnalysisService>();
        
        // Execute the diff analysis
        await diffAnalysisService.AnalyzeDiffAsync(solution!, diffContent, diffFile, gitCommand, output, format, verbose);
        
        return 0;
    }
}