// Polyfills for modern C# features to support netstandard2.0 and netstandard2.1
#if NETSTANDARD2_0 || NETSTANDARD2_1 || NETFRAMEWORK

using System.ComponentModel;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Reserved to be used by the compiler for tracking metadata.
    /// This class should not be used by developers in source code.
    /// This dummy class is required to compile records when targeting .NET Standard 2.0
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}

#if NETSTANDARD2_0 || NETFRAMEWORK
namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    /// Specifies that when a method returns <see cref="ReturnValue"/>, 
    /// the parameter will not be null even if the corresponding type allows it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class NotNullWhenAttribute : Attribute
    {
        /// <summary>
        /// Initializes the attribute with the specified return value condition.
        /// </summary>
        /// <param name="returnValue">
        /// The return value condition. If the method returns this value, 
        /// the associated parameter will not be null.
        /// </param>
        public NotNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;

        /// <summary>
        /// Gets the return value condition.
        /// </summary>
        public bool ReturnValue { get; }
    }
}
#endif

namespace System
{
    /// <summary>
    /// Polyfill for ArgumentNullException.ThrowIfNull
    /// </summary>
    internal static class ArgumentNullExceptionPolyfill
    {
        /// <summary>
        /// Throws an ArgumentNullException if argument is null.
        /// </summary>
        public static void ThrowIfNull(object? argument, string? paramName = null)
        {
            if (argument is null)
            {
                throw new ArgumentNullException(paramName);
            }
        }
    }
}

#endif