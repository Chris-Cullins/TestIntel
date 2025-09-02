using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Services;

namespace TestIntelligence.CLI.Commands
{
    /// <summary>
    /// Command handler for the categorize command.
    /// Categorizes tests by type (Unit, Integration, Database, etc.).
    /// </summary>
    public class CategorizeCommandHandler : BaseCommandHandler
    {
        public CategorizeCommandHandler(ILogger<CategorizeCommandHandler> logger) : base(logger)
        {
        }

        protected override async Task<int> ExecuteInternalAsync(CommandContext context, CancellationToken cancellationToken)
        {
            // Validate required parameters
            ValidateRequiredParameters(context, "path");

            // Extract parameters
            var path = context.GetParameter<string>("path");
            var output = context.GetParameter<string>("output");

            Logger.LogInformation("Categorizing tests at path: {Path}", path);
            
            // Get the categorization service from DI
            var categorizationService = context.GetService<ICategorizationService>();
            
            // Execute the categorization
            await categorizationService.CategorizeAsync(path!, output);
            
            return 0;
        }
    }
}