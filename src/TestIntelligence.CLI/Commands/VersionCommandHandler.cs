using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.CLI.Commands
{
    /// <summary>
    /// Command handler for the version command.
    /// Shows version information for the TestIntelligence CLI.
    /// </summary>
    public class VersionCommandHandler : BaseCommandHandler
    {
        public VersionCommandHandler(ILogger<VersionCommandHandler> logger) : base(logger)
        {
        }

        protected override Task<int> ExecuteInternalAsync(CommandContext context, CancellationToken cancellationToken)
        {
            var version = typeof(VersionCommandHandler).Assembly.GetName().Version;
            Console.WriteLine($"TestIntelligence CLI v{version}");
            Console.WriteLine("Intelligent test analysis and selection tool");
            
            return Task.FromResult(0);
        }
    }
}