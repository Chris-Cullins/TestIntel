using System.CommandLine;
using Microsoft.Extensions.Hosting;

namespace TestIntelligence.CLI.Commands
{
    /// <summary>
    /// Factory interface for creating CLI commands with their handlers.
    /// Separates command creation from the monolithic Program.cs.
    /// </summary>
    public interface ICommandFactory
    {
        /// <summary>
        /// Creates all commands and returns the root command.
        /// Uses command handlers from the DI container to build all CLI commands.
        /// </summary>
        /// <param name="host">The host containing DI services</param>
        /// <returns>The configured root command with all sub-commands</returns>
        RootCommand CreateRootCommand(IHost host);
    }
}