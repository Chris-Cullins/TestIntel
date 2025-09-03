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
                Console.Error.WriteLine("Operation was cancelled by user.");
                return 130; // Standard exit code for cancelled operations
            }
            catch (ArgumentException ex)
            {
                Logger.LogError(ex, "Invalid arguments provided to command");
                Console.Error.WriteLine($"❌ Invalid argument: {ex.Message}");
                PrintUsageHint(context);
                return 1;
            }
            catch (FileNotFoundException ex)
            {
                Logger.LogError(ex, "Required file not found");
                Console.Error.WriteLine($"❌ File not found: {ex.FileName ?? ex.Message}");
                Console.Error.WriteLine("Please verify that the file path is correct and accessible.");
                return 2;
            }
            catch (DirectoryNotFoundException ex)
            {
                Logger.LogError(ex, "Required directory not found");
                Console.Error.WriteLine($"❌ Directory not found: {ex.Message}");
                Console.Error.WriteLine("Please verify that the directory path is correct and accessible.");
                return 2;
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.LogError(ex, "Access denied to file or directory");
                Console.Error.WriteLine($"❌ Access denied: {ex.Message}");
                Console.Error.WriteLine("Please check file/directory permissions or run with appropriate privileges.");
                return 13; // Standard exit code for permission denied
            }
            catch (TimeoutException ex)
            {
                Logger.LogError(ex, "Operation timed out");
                Console.Error.WriteLine($"❌ Operation timed out: {ex.Message}");
                Console.Error.WriteLine("The operation took longer than expected. Try reducing the scope or running again.");
                return 124; // Standard exit code for timeout
            }
            catch (System.IO.FileLoadException ex) when (ex.Message.Contains("Microsoft.Bcl.AsyncInterfaces"))
            {
                Logger.LogError(ex, "Assembly loading conflict detected");
                Console.Error.WriteLine("❌ Assembly conflict detected:");
                Console.Error.WriteLine($"   {ex.Message}");
                Console.Error.WriteLine();
                Console.Error.WriteLine("💡 This is usually caused by conflicting package versions. Try:");
                Console.Error.WriteLine("   • Clearing NuGet cache: dotnet nuget locals all --clear");
                Console.Error.WriteLine("   • Rebuilding the solution: dotnet clean && dotnet build");
                Console.Error.WriteLine("   • Updating packages to consistent versions");
                return 125; // Custom exit code for assembly conflicts
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                Logger.LogError(ex, "Failed to load types from assembly");
                Console.Error.WriteLine("❌ Failed to load assembly types:");
                if (ex.LoaderExceptions != null)
                {
                    foreach (var loaderEx in ex.LoaderExceptions.Take(3))
                    {
                        Console.Error.WriteLine($"   • {loaderEx?.Message}");
                    }
                    if (ex.LoaderExceptions.Length > 3)
                    {
                        Console.Error.WriteLine($"   ... and {ex.LoaderExceptions.Length - 3} more errors");
                    }
                }
                Console.Error.WriteLine();
                Console.Error.WriteLine("💡 This usually indicates missing dependencies or version mismatches.");
                Console.Error.WriteLine("   Check that all required packages are installed and compatible.");
                return 126; // Custom exit code for type loading failures
            }
            catch (OutOfMemoryException ex)
            {
                Logger.LogError(ex, "Out of memory during command execution");
                Console.Error.WriteLine("❌ Insufficient memory to complete the operation.");
                Console.Error.WriteLine("💡 Try reducing the scope (e.g., analyze fewer files) or increase available memory.");
                return 127; // Custom exit code for memory issues
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error during command execution");
                Console.Error.WriteLine($"❌ Unexpected error: {ex.Message}");
                
                if (Logger.IsEnabled(LogLevel.Debug))
                {
                    Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                }
                else
                {
                    Console.Error.WriteLine("Use --verbose for detailed error information.");
                }
                
                Console.Error.WriteLine();
                Console.Error.WriteLine("💡 If this error persists, please report it at:");
                Console.Error.WriteLine("   https://github.com/TestIntelligence/TestIntelligence/issues");
                
                return 1;
            }
        }

        /// <summary>
        /// Prints usage hint for the current command.
        /// </summary>
        protected virtual void PrintUsageHint(CommandContext context)
        {
            Console.Error.WriteLine("💡 Use --help to see available options and usage examples.");
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