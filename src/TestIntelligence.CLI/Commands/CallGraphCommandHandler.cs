using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Services;

namespace TestIntelligence.CLI.Commands;

/// <summary>
/// Command handler for the callgraph command.
/// Analyzes method call graph and generates dependency reports.
/// </summary>
public class CallGraphCommandHandler : BaseCommandHandler
{
    public CallGraphCommandHandler(ILogger<CallGraphCommandHandler> logger) : base(logger)
    {
    }

    protected override async Task<int> ExecuteInternalAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Validate required parameters
        ValidateRequiredParameters(context, "path");

        // Extract parameters
        var path = context.GetParameter<string>("path");
        var output = context.GetParameter<string>("output");
        var format = context.GetParameter<string>("format") ?? "text";
        var verbose = context.GetParameter<bool>("verbose");
        var maxMethods = context.GetParameter<int?>("max-methods");

        Logger.LogInformation("Analyzing call graph for path: {Path}", path);
        
        // Get the call graph service from DI
        var callGraphService = context.GetService<ICallGraphService>();
        
        // Execute the call graph analysis
        await callGraphService.AnalyzeCallGraphAsync(path!, output, format, verbose, maxMethods);
        
        return 0;
    }
}