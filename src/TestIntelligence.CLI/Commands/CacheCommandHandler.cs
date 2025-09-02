using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Services;

namespace TestIntelligence.CLI.Commands;

/// <summary>
/// Command handler for the cache command.
/// Manages persistent cache for large solutions.
/// </summary>
public class CacheCommandHandler : BaseCommandHandler
{
    public CacheCommandHandler(ILogger<CacheCommandHandler> logger) : base(logger)
    {
    }

    protected override async Task<int> ExecuteInternalAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Validate required parameters
        ValidateRequiredParameters(context, "solution", "action");

        // Extract parameters
        var solution = context.GetParameter<string>("solution");
        var action = context.GetParameter<string>("action");
        var cacheDir = context.GetParameter<string>("cache-dir");
        var format = context.GetParameter<string>("format") ?? "text";
        var verbose = context.GetParameter<bool>("verbose");

        Logger.LogInformation("Executing cache action: {Action} for solution: {Solution}", action, solution);
        
        // Get the cache management service from DI
        var cacheService = context.GetService<CacheManagementService>();
        
        // Execute the cache operation
        await cacheService.HandleCacheCommandAsync(solution!, action!, cacheDir, format, verbose);
        
        return 0;
    }
}