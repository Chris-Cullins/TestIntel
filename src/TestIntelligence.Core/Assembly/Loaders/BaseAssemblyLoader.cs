using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TestIntelligence.Core.Assembly.Loaders
{
    /// <summary>
    /// Base class for framework-specific assembly loaders providing common functionality.
    /// </summary>
    public abstract class BaseAssemblyLoader : IAssemblyLoader
    {
        private bool _disposed = false;
        private static readonly ThreadLocal<HashSet<string>> _resolutionStack = new ThreadLocal<HashSet<string>>(() => new HashSet<string>());

        /// <summary>
        /// Initializes a new instance of the BaseAssemblyLoader.
        /// </summary>
        /// <param name="supportedFramework">The framework version this loader supports.</param>
        protected BaseAssemblyLoader(FrameworkVersion supportedFramework)
        {
            SupportedFramework = supportedFramework;
        }

        /// <inheritdoc />
        public virtual FrameworkVersion SupportedFramework { get; }

        /// <inheritdoc />
        public Func<object, ResolveEventArgs, System.Reflection.Assembly?> AssemblyResolve { get; set; } = DefaultAssemblyResolver;

        /// <inheritdoc />
        public virtual bool CanLoad(string assemblyPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
                return false;

            try
            {
                var detectedFramework = FrameworkDetector.DetectFrameworkVersion(assemblyPath);
                return CanLoadFramework(detectedFramework);
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<ITestAssembly> LoadAssemblyAsync(string assemblyPath, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => LoadAssembly(assemblyPath), cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public abstract ITestAssembly LoadAssembly(string assemblyPath);

        /// <inheritdoc />
        public abstract bool TryUnloadAssembly(ITestAssembly testAssembly);

        /// <summary>
        /// Determines if this loader can handle the specified framework version.
        /// </summary>
        /// <param name="frameworkVersion">The framework version to check.</param>
        /// <returns>True if this loader can handle the framework version.</returns>
        protected virtual bool CanLoadFramework(FrameworkVersion frameworkVersion)
        {
            return frameworkVersion == SupportedFramework;
        }

        /// <summary>
        /// Validates the assembly path before loading.
        /// </summary>
        /// <param name="assemblyPath">The assembly path to validate.</param>
        /// <exception cref="ArgumentNullException">Thrown when path is null or empty.</exception>
        /// <exception cref="FileNotFoundException">Thrown when file does not exist.</exception>
        protected static void ValidateAssemblyPath(string assemblyPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
                throw new ArgumentNullException(nameof(assemblyPath), "Assembly path cannot be null or empty.");

            if (!File.Exists(assemblyPath))
                throw new FileNotFoundException($"Assembly file not found: {assemblyPath}", assemblyPath);
        }

        /// <summary>
        /// Default assembly resolver that attempts to resolve dependencies from the assembly directory.
        /// Includes circular dependency detection to prevent stack overflow.
        /// </summary>
        /// <param name="sender">The sender of the resolve request.</param>
        /// <param name="args">The resolve event arguments.</param>
        /// <returns>The resolved assembly or null if not found.</returns>
        protected static System.Reflection.Assembly? DefaultAssemblyResolver(object sender, ResolveEventArgs args)
        {
            try
            {
                // Check for circular dependency to prevent stack overflow
                var resolutionStack = _resolutionStack.Value!;
                if (resolutionStack.Contains(args.Name))
                {
                    // Circular dependency detected, return null to break the cycle
                    return null;
                }

                resolutionStack.Add(args.Name);
                try
                {
                    // Extract the simple name from the full assembly name
                    var assemblyName = new System.Reflection.AssemblyName(args.Name);
                    var simpleName = assemblyName.Name;

                    if (string.IsNullOrEmpty(simpleName))
                        return null;

                    // Try to find the assembly in the requesting assembly's directory
                    if (args.RequestingAssembly != null && !string.IsNullOrEmpty(args.RequestingAssembly.Location))
                    {
                        var requestingDir = Path.GetDirectoryName(args.RequestingAssembly.Location);
                        if (!string.IsNullOrEmpty(requestingDir))
                        {
                            var candidatePaths = new[]
                            {
                                Path.Combine(requestingDir, $"{simpleName}.dll"),
                                Path.Combine(requestingDir, $"{simpleName}.exe")
                            };

                            foreach (var candidatePath in candidatePaths)
                            {
                                if (File.Exists(candidatePath))
                                {
                                    try
                                    {
                                        return System.Reflection.Assembly.LoadFrom(candidatePath);
                                    }
                                    catch
                                    {
                                        // Continue to next candidate
                                    }
                                }
                            }
                        }
                    }

                    // Try loading from GAC or already loaded assemblies
                    try
                    {
                        return System.Reflection.Assembly.Load(args.Name);
                    }
                    catch
                    {
                        return null;
                    }
                }
                finally
                {
                    resolutionStack.Remove(args.Name);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Creates a wrapper for the loaded assembly with metadata extraction capabilities.
        /// </summary>
        /// <param name="assembly">The loaded assembly.</param>
        /// <param name="assemblyPath">The path to the assembly file.</param>
        /// <param name="frameworkVersion">The detected framework version.</param>
        /// <returns>A test assembly wrapper.</returns>
        protected virtual ITestAssembly CreateTestAssembly(System.Reflection.Assembly assembly, string assemblyPath, FrameworkVersion frameworkVersion)
        {
            return new TestAssemblyWrapper(assembly, assemblyPath, frameworkVersion);
        }

        /// <inheritdoc />
        public virtual void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                DisposeCore();
            }
            finally
            {
                _disposed = true;
            }
        }

        /// <summary>
        /// Override this method to provide custom disposal logic.
        /// </summary>
        protected virtual void DisposeCore()
        {
            // Clean up thread-local resolution stack
            if (_resolutionStack.IsValueCreated)
            {
                _resolutionStack.Value?.Clear();
            }
        }

        /// <summary>
        /// Throws if this instance has been disposed.
        /// </summary>
        protected void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }
    }
}