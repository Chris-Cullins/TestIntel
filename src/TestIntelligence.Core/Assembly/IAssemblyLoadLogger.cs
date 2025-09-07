using System;

namespace TestIntelligence.Core.Assembly
{
    /// <summary>
    /// Defines the contract for logging assembly loading operations.
    /// </summary>
    public interface IAssemblyLoadLogger
    {
        /// <summary>
        /// Logs an informational message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="args">Optional message formatting arguments.</param>
        void LogInformation(string message, params object[] args);

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="args">Optional message formatting arguments.</param>
        void LogWarning(string message, params object[] args);

        /// <summary>
        /// Logs an error message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="args">Optional message formatting arguments.</param>
        void LogError(string message, params object[] args);

        /// <summary>
        /// Logs an error message with an exception.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="args">Optional message formatting arguments.</param>
        void LogError(Exception exception, string message, params object[] args);

        /// <summary>
        /// Logs a debug message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="args">Optional message formatting arguments.</param>
        void LogDebug(string message, params object[] args);

        /// <summary>
        /// Logs an assembly load attempt for debugging purposes.
        /// </summary>
        /// <param name="assemblyPath">The path of the assembly being loaded.</param>
        /// <param name="frameworkVersion">The framework version detected for the assembly.</param>
        void LogAssemblyLoadAttempt(string assemblyPath, FrameworkVersion frameworkVersion);
    }

    /// <summary>
    /// Default console-based logger implementation.
    /// </summary>
    public class ConsoleAssemblyLoadLogger : IAssemblyLoadLogger
    {
        /// <inheritdoc />
        public void LogInformation(string message, params object[] args)
        {
            WriteLog("INFO", message, args);
        }

        /// <inheritdoc />
        public void LogWarning(string message, params object[] args)
        {
            WriteLog("WARN", message, args);
        }

        /// <inheritdoc />
        public void LogError(string message, params object[] args)
        {
            WriteLog("ERROR", message, args);
        }

        /// <inheritdoc />
        public void LogError(Exception exception, string message, params object[] args)
        {
            WriteLog("ERROR", message, args);
            WriteLog("ERROR", $"Exception: {exception}");
        }

        /// <inheritdoc />
        public void LogDebug(string message, params object[] args)
        {
#if DEBUG
            WriteLog("DEBUG", message, args);
#endif
        }

        /// <inheritdoc />
        public void LogAssemblyLoadAttempt(string assemblyPath, FrameworkVersion frameworkVersion)
        {
            LogDebug("Assembly load attempt: {0} (Framework: {1})", assemblyPath, frameworkVersion);
        }

        private static void WriteLog(string level, string message, params object[] args)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var formattedMessage = args.Length > 0 ? string.Format(message, args) : message;
            Console.WriteLine($"[{timestamp}] [{level}] [AssemblyLoader] {formattedMessage}");
        }
    }

    /// <summary>
    /// Null logger that discards all log messages.
    /// </summary>
    public class NullAssemblyLoadLogger : IAssemblyLoadLogger
    {
        /// <summary>
        /// Gets the singleton instance of the null logger.
        /// </summary>
        public static readonly NullAssemblyLoadLogger Instance = new NullAssemblyLoadLogger();

        private NullAssemblyLoadLogger() { }

        /// <inheritdoc />
        public void LogInformation(string message, params object[] args) { }

        /// <inheritdoc />
        public void LogWarning(string message, params object[] args) { }

        /// <inheritdoc />
        public void LogError(string message, params object[] args) { }

        /// <inheritdoc />
        public void LogError(Exception exception, string message, params object[] args) { }

        /// <inheritdoc />
        public void LogDebug(string message, params object[] args) { }

        /// <inheritdoc />
        public void LogAssemblyLoadAttempt(string assemblyPath, FrameworkVersion frameworkVersion) { }
    }
}