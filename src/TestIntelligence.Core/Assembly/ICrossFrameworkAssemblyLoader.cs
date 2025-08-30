using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TestIntelligence.Core.Assembly
{
    /// <summary>
    /// Defines the contract for a cross-framework assembly loader that can handle multiple .NET framework versions.
    /// </summary>
    public interface ICrossFrameworkAssemblyLoader : IDisposable
    {
        /// <summary>
        /// Detects the framework version of the specified assembly.
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly file.</param>
        /// <returns>The detected framework version.</returns>
        FrameworkVersion DetectFrameworkVersion(string assemblyPath);

        /// <summary>
        /// Loads an assembly using the appropriate framework-specific loader.
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly file.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The assembly load result.</returns>
        Task<AssemblyLoadResult> LoadAssemblyAsync(string assemblyPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Loads an assembly synchronously using the appropriate framework-specific loader.
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly file.</param>
        /// <returns>The assembly load result.</returns>
        AssemblyLoadResult LoadAssembly(string assemblyPath);

        /// <summary>
        /// Loads multiple assemblies with optimal batching and parallel processing.
        /// </summary>
        /// <param name="assemblyPaths">Paths to the assembly files.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Dictionary of assembly paths to their load results.</returns>
        Task<IReadOnlyDictionary<string, AssemblyLoadResult>> LoadAssembliesAsync(
            IEnumerable<string> assemblyPaths, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all currently loaded assemblies.
        /// </summary>
        IReadOnlyList<ITestAssembly> GetLoadedAssemblies();

        /// <summary>
        /// Unloads the specified assembly if supported.
        /// </summary>
        /// <param name="testAssembly">The assembly to unload.</param>
        /// <returns>True if unloading was successful or supported.</returns>
        bool TryUnloadAssembly(ITestAssembly testAssembly);

        /// <summary>
        /// Unloads all currently loaded assemblies.
        /// </summary>
        void UnloadAllAssemblies();

        /// <summary>
        /// Gets the supported framework versions by this loader.
        /// </summary>
        IReadOnlyList<FrameworkVersion> SupportedFrameworks { get; }

        /// <summary>
        /// Event raised when an assembly is successfully loaded.
        /// </summary>
        event EventHandler<AssemblyLoadedEventArgs>? AssemblyLoaded;

        /// <summary>
        /// Event raised when an assembly fails to load.
        /// </summary>
        event EventHandler<AssemblyLoadFailedEventArgs>? AssemblyLoadFailed;
    }

    /// <summary>
    /// Event arguments for successful assembly loading.
    /// </summary>
    public class AssemblyLoadedEventArgs : EventArgs
    {
        public AssemblyLoadedEventArgs(string assemblyPath, ITestAssembly testAssembly, FrameworkVersion frameworkVersion)
        {
            AssemblyPath = assemblyPath;
            TestAssembly = testAssembly;
            FrameworkVersion = frameworkVersion;
            LoadedAt = DateTimeOffset.UtcNow;
        }

        public string AssemblyPath { get; }
        public ITestAssembly TestAssembly { get; }
        public FrameworkVersion FrameworkVersion { get; }
        public DateTimeOffset LoadedAt { get; }
    }

    /// <summary>
    /// Event arguments for failed assembly loading.
    /// </summary>
    public class AssemblyLoadFailedEventArgs : EventArgs
    {
        public AssemblyLoadFailedEventArgs(string assemblyPath, IReadOnlyList<string> errors, FrameworkVersion? detectedFramework = null)
        {
            AssemblyPath = assemblyPath;
            Errors = errors;
            DetectedFramework = detectedFramework;
            FailedAt = DateTimeOffset.UtcNow;
        }

        public string AssemblyPath { get; }
        public IReadOnlyList<string> Errors { get; }
        public FrameworkVersion? DetectedFramework { get; }
        public DateTimeOffset FailedAt { get; }
    }
}