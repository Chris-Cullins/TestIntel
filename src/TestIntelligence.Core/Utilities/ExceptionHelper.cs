using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.Core.Utilities
{
    /// <summary>
    /// Centralized exception handling utilities to promote consistent error handling patterns.
    /// </summary>
    public static class ExceptionHelper
    {
        /// <summary>
        /// Throws ArgumentNullException if the value is null, with a consistent message format.
        /// </summary>
        /// <typeparam name="T">Type of the parameter being validated</typeparam>
        /// <param name="value">Value to check for null</param>
        /// <param name="paramName">Name of the parameter</param>
        /// <returns>The non-null value for method chaining</returns>
        /// <exception cref="ArgumentNullException">Thrown when value is null</exception>
        public static T ThrowIfNull<T>(T value, string paramName) where T : class
        {
            if (value == null)
                throw new ArgumentNullException(paramName);
            return value;
        }

        /// <summary>
        /// Throws ArgumentException if the string is null, empty, or whitespace.
        /// </summary>
        /// <param name="value">String value to validate</param>
        /// <param name="paramName">Name of the parameter</param>
        /// <param name="customMessage">Optional custom error message</param>
        /// <returns>The validated string value for method chaining</returns>
        /// <exception cref="ArgumentException">Thrown when string is null, empty, or whitespace</exception>
        public static string ThrowIfNullOrWhiteSpace(string value, string paramName, string? customMessage = null)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(customMessage ?? $"{paramName} cannot be null, empty, or whitespace", paramName);
            return value;
        }

        /// <summary>
        /// Safely executes an action and logs any exceptions without rethrowing.
        /// Useful for cleanup operations or non-critical tasks.
        /// </summary>
        /// <param name="action">Action to execute safely</param>
        /// <param name="logger">Logger to record any exceptions</param>
        /// <param name="operationName">Name of the operation for logging context</param>
        public static void ExecuteSafely(Action action, ILogger logger, string operationName)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during {OperationName}: {ErrorMessage}", operationName, ex.Message);
            }
        }

        /// <summary>
        /// Safely executes a function and returns a default value on exception.
        /// Logs exceptions without rethrowing.
        /// </summary>
        /// <typeparam name="T">Return type</typeparam>
        /// <param name="func">Function to execute safely</param>
        /// <param name="defaultValue">Default value to return on exception</param>
        /// <param name="logger">Logger to record any exceptions</param>
        /// <param name="operationName">Name of the operation for logging context</param>
        /// <returns>Function result or default value on exception</returns>
        public static T ExecuteSafely<T>(Func<T> func, T defaultValue, ILogger logger, string operationName)
        {
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during {OperationName}: {ErrorMessage}, returning default value", operationName, ex.Message);
                return defaultValue;
            }
        }

        /// <summary>
        /// Wraps common file operation exceptions with more user-friendly messages.
        /// </summary>
        /// <param name="filePath">Path to the file</param>
        /// <param name="operation">Description of the operation being performed</param>
        /// <param name="ex">The original exception</param>
        /// <returns>A more descriptive exception</returns>
        public static Exception WrapFileException(string filePath, string operation, Exception ex)
        {
            return ex switch
            {
                UnauthorizedAccessException => new UnauthorizedAccessException($"Access denied while {operation} file: {filePath}", ex),
                DirectoryNotFoundException => new DirectoryNotFoundException($"Directory not found while {operation} file: {filePath}", ex),
                FileNotFoundException => new FileNotFoundException($"File not found while {operation}: {filePath}", ex),
                IOException => new IOException($"I/O error while {operation} file: {filePath}", ex),
                _ => new InvalidOperationException($"Unexpected error while {operation} file: {filePath}", ex)
            };
        }

        /// <summary>
        /// Creates a standardized analysis exception with context information.
        /// </summary>
        /// <param name="analysisType">Type of analysis being performed</param>
        /// <param name="target">Target being analyzed (e.g., solution path, method name)</param>
        /// <param name="innerException">The underlying exception</param>
        /// <returns>A context-rich analysis exception</returns>
        public static InvalidOperationException CreateAnalysisException(string analysisType, string target, Exception innerException)
        {
            return new InvalidOperationException($"{analysisType} analysis failed for target: {target}. {innerException.Message}", innerException);
        }

        /// <summary>
        /// Determines if an exception should be treated as a fatal error that should not be caught.
        /// </summary>
        /// <param name="ex">Exception to evaluate</param>
        /// <returns>True if the exception should be considered fatal</returns>
        public static bool IsFatalException(Exception ex)
        {
            return ex is StackOverflowException or 
                   OutOfMemoryException or 
                   AccessViolationException or 
                   AppDomainUnloadedException or 
                   BadImageFormatException or 
                   CannotUnloadAppDomainException or 
                   InvalidProgramException;
        }

        /// <summary>
        /// Logs exception details with consistent format and appropriate log level.
        /// </summary>
        /// <param name="logger">Logger instance</param>
        /// <param name="ex">Exception to log</param>
        /// <param name="operationName">Name of the operation that failed</param>
        /// <param name="additionalContext">Additional context information</param>
        public static void LogException(ILogger logger, Exception ex, string operationName, object? additionalContext = null)
        {
            if (IsFatalException(ex))
            {
                logger.LogCritical(ex, "Fatal exception during {OperationName}: {ErrorMessage}. Additional context: {@Context}", 
                    operationName, ex.Message, additionalContext);
            }
            else if (ex is ArgumentException or InvalidOperationException)
            {
                logger.LogError(ex, "Operation failed: {OperationName} - {ErrorMessage}. Additional context: {@Context}", 
                    operationName, ex.Message, additionalContext);
            }
            else
            {
                logger.LogWarning(ex, "Unexpected error during {OperationName}: {ErrorMessage}. Additional context: {@Context}", 
                    operationName, ex.Message, additionalContext);
            }
        }
    }
}