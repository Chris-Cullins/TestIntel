using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace TestIntelligence.Core.Assembly
{
    /// <summary>
    /// Exception thrown when an assembly cannot be loaded by the CrossFrameworkAssemblyLoader.
    /// </summary>
    [Serializable]
    public class AssemblyLoadException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the AssemblyLoadException.
        /// </summary>
        public AssemblyLoadException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the AssemblyLoadException with a specified error message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public AssemblyLoadException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the AssemblyLoadException with a specified error message and inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public AssemblyLoadException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the AssemblyLoadException with assembly path and errors.
        /// </summary>
        /// <param name="assemblyPath">The path to the assembly that failed to load.</param>
        /// <param name="errors">The collection of errors that occurred.</param>
        public AssemblyLoadException(string assemblyPath, IReadOnlyList<string> errors) 
            : base(CreateMessage(assemblyPath, errors))
        {
            AssemblyPath = assemblyPath;
            Errors = errors ?? Array.Empty<string>();
        }

        /// <summary>
        /// Initializes a new instance of the AssemblyLoadException with assembly path, errors, and inner exception.
        /// </summary>
        /// <param name="assemblyPath">The path to the assembly that failed to load.</param>
        /// <param name="errors">The collection of errors that occurred.</param>
        /// <param name="innerException">The inner exception.</param>
        public AssemblyLoadException(string assemblyPath, IReadOnlyList<string> errors, Exception innerException) 
            : base(CreateMessage(assemblyPath, errors), innerException)
        {
            AssemblyPath = assemblyPath;
            Errors = errors ?? Array.Empty<string>();
        }

        /// <summary>
        /// Initializes a new instance of the AssemblyLoadException for serialization.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        protected AssemblyLoadException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            AssemblyPath = info.GetString(nameof(AssemblyPath)) ?? string.Empty;
            var errorCount = info.GetInt32(nameof(Errors) + "_Count");
            var errors = new string[errorCount];
            for (int i = 0; i < errorCount; i++)
            {
                errors[i] = info.GetString($"{nameof(Errors)}_{i}") ?? string.Empty;
            }
            Errors = errors;
        }

        /// <summary>
        /// Gets the path to the assembly that failed to load.
        /// </summary>
        public string AssemblyPath { get; } = string.Empty;

        /// <summary>
        /// Gets the collection of errors that occurred during loading.
        /// </summary>
        public IReadOnlyList<string> Errors { get; } = Array.Empty<string>();

        /// <inheritdoc />
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            base.GetObjectData(info, context);
            info.AddValue(nameof(AssemblyPath), AssemblyPath);
            info.AddValue(nameof(Errors) + "_Count", Errors.Count);
            for (int i = 0; i < Errors.Count; i++)
            {
                info.AddValue($"{nameof(Errors)}_{i}", Errors[i]);
            }
        }

        /// <summary>
        /// Creates a descriptive error message from the assembly path and errors.
        /// </summary>
        private static string CreateMessage(string assemblyPath, IReadOnlyList<string>? errors)
        {
            var message = $"Failed to load assembly: {assemblyPath}";
            
            if (errors != null && errors.Count > 0)
            {
                message += Environment.NewLine + "Errors:";
                for (int i = 0; i < errors.Count; i++)
                {
                    message += Environment.NewLine + $"  {i + 1}. {errors[i]}";
                }
            }
            
            return message;
        }
    }
}