using System.Threading;
using System.Threading.Tasks;

namespace TestIntelligence.CLI.Commands
{
    /// <summary>
    /// Interface for CLI command handlers following the command pattern.
    /// Each command handler encapsulates the logic for executing a specific CLI command.
    /// </summary>
    public interface ICommandHandler
    {
        /// <summary>
        /// Executes the command with the provided context.
        /// </summary>
        /// <param name="context">The command execution context containing parameters and services</param>
        /// <param name="cancellationToken">Cancellation token for async operations</param>
        /// <returns>Exit code (0 for success, non-zero for error)</returns>
        Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default);
    }
}