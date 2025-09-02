using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Services;

namespace TestIntelligence.CLI.Commands
{
    /// <summary>
    /// Command handler for the analyze command.
    /// Analyzes test assemblies for categorization and impact.
    /// </summary>
    public class AnalyzeCommandHandler : BaseCommandHandler
    {
        public AnalyzeCommandHandler(ILogger<AnalyzeCommandHandler> logger) : base(logger)
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

            Logger.LogInformation("Analyzing assemblies at path: {Path}", path);
            
            // Get the analysis service from DI
            var analysisService = context.GetService<IAnalysisService>();
            
            // Execute the analysis
            await analysisService.AnalyzeAsync(path!, output, format, verbose);
            
            return 0;
        }
    }
}