using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Services;

namespace TestIntelligence.CLI.Commands
{
    /// <summary>
    /// Command handler for the select command.
    /// Selects optimal tests based on code changes and confidence level.
    /// </summary>
    public class SelectCommandHandler : BaseCommandHandler
    {
        public SelectCommandHandler(ILogger<SelectCommandHandler> logger) : base(logger)
        {
        }

        protected override async Task<int> ExecuteInternalAsync(CommandContext context, CancellationToken cancellationToken)
        {
            // Validate required parameters
            ValidateRequiredParameters(context, "path");

            // Extract parameters
            var path = context.GetParameter<string>("path");
            var changes = context.GetParameter<string[]>("changes") ?? Array.Empty<string>();
            var confidence = context.GetParameter<string>("confidence") ?? "Medium";
            var output = context.GetParameter<string>("output");
            var maxTests = context.GetParameter<int?>("maxTests");
            var maxTime = context.GetParameter<string>("maxTime");

            Logger.LogInformation("Selecting tests for path: {Path}", path);
            
            // Get the selection service from DI
            var selectionService = context.GetService<ISelectionService>();
            
            // Execute the selection
            await selectionService.SelectAsync(path!, changes ?? Array.Empty<string>(), confidence, output, maxTests, maxTime);
            
            return 0;
        }
    }
}