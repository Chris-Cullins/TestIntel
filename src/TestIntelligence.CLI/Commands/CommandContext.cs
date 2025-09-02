using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace TestIntelligence.CLI.Commands
{
    /// <summary>
    /// Context for command execution containing parameters and services.
    /// Provides a clean interface for command handlers to access their dependencies.
    /// </summary>
    public class CommandContext
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Dictionary<string, object> _parameters;

        public CommandContext(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _parameters = new Dictionary<string, object>();
        }

        /// <summary>
        /// Gets a service of the specified type from the DI container.
        /// </summary>
        /// <typeparam name="T">The type of service to retrieve</typeparam>
        /// <returns>The service instance</returns>
        public T GetService<T>() where T : notnull => _serviceProvider.GetRequiredService<T>();

        /// <summary>
        /// Gets a parameter value by name.
        /// </summary>
        /// <typeparam name="T">The expected type of the parameter</typeparam>
        /// <param name="name">The parameter name</param>
        /// <returns>The parameter value</returns>
        public T? GetParameter<T>(string name)
        {
            if (_parameters.TryGetValue(name, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return default(T);
        }

        /// <summary>
        /// Sets a parameter value.
        /// </summary>
        /// <param name="name">The parameter name</param>
        /// <param name="value">The parameter value</param>
        public void SetParameter(string name, object value)
        {
            _parameters[name] = value;
        }

        /// <summary>
        /// Checks if a parameter exists.
        /// </summary>
        /// <param name="name">The parameter name</param>
        /// <returns>True if the parameter exists</returns>
        public bool HasParameter(string name) => _parameters.ContainsKey(name);

        /// <summary>
        /// Writes output to the console or specified output file.
        /// </summary>
        /// <param name="content">The content to write</param>
        /// <param name="outputPath">Optional output file path</param>
        public async Task WriteOutputAsync(string content, string? outputPath = null)
        {
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                await File.WriteAllTextAsync(outputPath, content);
                Console.WriteLine($"Results written to: {outputPath}");
            }
            else
            {
                Console.WriteLine(content);
            }
        }

        /// <summary>
        /// Gets all parameter names.
        /// </summary>
        /// <returns>Collection of parameter names</returns>
        public ICollection<string> ParameterNames => _parameters.Keys;
    }
}