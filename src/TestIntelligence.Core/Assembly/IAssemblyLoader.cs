using System;
using System.Threading;
using System.Threading.Tasks;

namespace TestIntelligence.Core.Assembly
{
    /// <summary>
    /// Defines the contract for loading assemblies with framework-specific isolation strategies.
    /// </summary>
    public interface IAssemblyLoader : IDisposable
    {
        /// <summary>
        /// Gets the framework version this loader supports.
        /// </summary>
        FrameworkVersion SupportedFramework { get; }

        /// <summary>
        /// Determines if this loader can load the specified assembly.
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly file.</param>
        /// <returns>True if the loader can handle this assembly.</returns>
        bool CanLoad(string assemblyPath);

        /// <summary>
        /// Loads an assembly with appropriate isolation.
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly file.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The loaded test assembly.</returns>
        Task<ITestAssembly> LoadAssemblyAsync(string assemblyPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Loads an assembly synchronously with appropriate isolation.
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly file.</param>
        /// <returns>The loaded test assembly.</returns>
        ITestAssembly LoadAssembly(string assemblyPath);

        /// <summary>
        /// Unloads a previously loaded assembly if supported by the loader.
        /// </summary>
        /// <param name="testAssembly">The assembly to unload.</param>
        /// <returns>True if unloading was successful or supported.</returns>
        bool TryUnloadAssembly(ITestAssembly testAssembly);

        /// <summary>
        /// Gets or sets the assembly resolution handler for dependency loading.
        /// </summary>
        Func<object, ResolveEventArgs, System.Reflection.Assembly?> AssemblyResolve { get; set; }
    }
}