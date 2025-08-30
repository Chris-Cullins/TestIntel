using System;
using System.Collections.Generic;

namespace TestIntelligence.Core.Assembly
{
    /// <summary>
    /// Represents the result of an assembly loading operation.
    /// </summary>
    public class AssemblyLoadResult
    {
        /// <summary>
        /// Initializes a successful assembly load result.
        /// </summary>
        public AssemblyLoadResult(ITestAssembly assembly)
        {
            IsSuccess = true;
            Assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
            Errors = Array.Empty<string>();
            Warnings = Array.Empty<string>();
        }

        /// <summary>
        /// Initializes a failed assembly load result.
        /// </summary>
        public AssemblyLoadResult(IReadOnlyList<string> errors, IReadOnlyList<string>? warnings = null)
        {
            IsSuccess = false;
            Assembly = null;
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
            Warnings = warnings ?? Array.Empty<string>();
        }

        /// <summary>
        /// Gets a value indicating whether the assembly was loaded successfully.
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// Gets the loaded assembly if successful, null otherwise.
        /// </summary>
        public ITestAssembly? Assembly { get; }

        /// <summary>
        /// Gets any errors that occurred during loading.
        /// </summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>
        /// Gets any warnings that occurred during loading.
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        public static AssemblyLoadResult Success(ITestAssembly assembly) => new(assembly);

        /// <summary>
        /// Creates a failed result with a single error.
        /// </summary>
        public static AssemblyLoadResult Failure(string error) => new(new[] { error });

        /// <summary>
        /// Creates a failed result with multiple errors.
        /// </summary>
        public static AssemblyLoadResult Failure(IReadOnlyList<string> errors) => new(errors);

        /// <summary>
        /// Creates a failed result with errors and warnings.
        /// </summary>
        public static AssemblyLoadResult Failure(IReadOnlyList<string> errors, IReadOnlyList<string> warnings) => new(errors, warnings);
    }
}