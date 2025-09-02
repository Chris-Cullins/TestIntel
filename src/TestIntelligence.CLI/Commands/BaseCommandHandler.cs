using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.CLI.Commands
{
    /// <summary>
    /// Base class for command handlers providing common functionality.
    /// Handles error handling, logging, and basic validation.
    /// </summary>
    public abstract class BaseCommandHandler : ICommandHandler
    {
        protected readonly ILogger Logger;

        protected BaseCommandHandler(ILogger logger)
        {
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Executes the command with error handling and logging.
        /// </summary>
        public async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.LogDebug("Starting execution of {CommandType}", GetType().Name);
                
                var result = await ExecuteInternalAsync(context, cancellationToken);
                
                Logger.LogDebug("Completed execution of {CommandType} with result {Result}", GetType().Name, result);
                return result;
            }
            catch (OperationCanceledException)
            {
                Logger.LogInformation("Command execution was cancelled");
                return 1;
            }
            catch (ArgumentException ex)
            {
                Logger.LogError(ex, "Invalid arguments provided to command");
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error during command execution");
                Console.Error.WriteLine($"Error: {ex.Message}");
                
                if (Logger.IsEnabled(LogLevel.Debug))
                {
                    Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                }
                
                return 1;
            }
        }

        /// <summary>
        /// Executes the command-specific logic. Override this in derived classes.
        /// </summary>
        /// <param name="context">The command context</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Exit code (0 for success, non-zero for error)</returns>
        protected abstract Task<int> ExecuteInternalAsync(CommandContext context, CancellationToken cancellationToken);

        /// <summary>
        /// Validates required parameters are present.
        /// </summary>
        /// <param name="context">The command context</param>
        /// <param name="requiredParameters">Names of required parameters</param>
        /// <exception cref="ArgumentException">Thrown when a required parameter is missing</exception>
        protected void ValidateRequiredParameters(CommandContext context, params string[] requiredParameters)
        {
            foreach (var param in requiredParameters)
            {
                if (!context.HasParameter(param))
                {
                    throw new ArgumentException($"Required parameter '{param}' is missing");
                }

                var value = context.GetParameter<object>(param);
                if (value == null || (value is string str && string.IsNullOrWhiteSpace(str)))
                {
                    throw new ArgumentException($"Required parameter '{param}' cannot be null or empty");
                }
            }
        }
    }
}