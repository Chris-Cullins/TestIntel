using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;

namespace TestIntelligence.Core.Assembly.Loaders
{
    /// <summary>
    /// Standard assembly loader for .NET Standard and compatible assemblies.
    /// This loader uses the default AppDomain and provides basic assembly loading functionality.
    /// </summary>
    public class StandardLoader : BaseAssemblyLoader
    {
        private readonly ConcurrentDictionary<string, WeakReference<ITestAssembly>> _loadedAssemblies;
        private readonly object _lockObject = new object();

        /// <summary>
        /// Initializes a new instance of the StandardLoader.
        /// </summary>
        public StandardLoader() : base(FrameworkVersion.NetStandard)
        {
            _loadedAssemblies = new ConcurrentDictionary<string, WeakReference<ITestAssembly>>(StringComparer.OrdinalIgnoreCase);
            
            // Subscribe to assembly resolution events
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        /// <inheritdoc />
        public override bool CanLoad(string assemblyPath)
        {
            if (!base.CanLoad(assemblyPath))
                return false;

            try
            {
                // Additional validation - try to peek at the assembly metadata
                using var fileStream = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var peReader = new System.Reflection.PortableExecutable.PEReader(fileStream);
                
                return peReader.HasMetadata;
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc />
        protected override bool CanLoadFramework(FrameworkVersion frameworkVersion)
        {
            // StandardLoader can handle most framework versions as fallback
            return frameworkVersion == FrameworkVersion.NetStandard ||
                   frameworkVersion == FrameworkVersion.NetCore ||
                   frameworkVersion == FrameworkVersion.Net5Plus ||
                   frameworkVersion == FrameworkVersion.Unknown;
        }

        /// <inheritdoc />
        public override ITestAssembly LoadAssembly(string assemblyPath)
        {
            ThrowIfDisposed();
            ValidateAssemblyPath(assemblyPath);

            var normalizedPath = Path.GetFullPath(assemblyPath);

            // Check if already loaded
            if (_loadedAssemblies.TryGetValue(normalizedPath, out var weakRef) && 
                weakRef.TryGetTarget(out var existingAssembly))
            {
                return existingAssembly;
            }

            lock (_lockObject)
            {
                // Double-check after acquiring lock
                if (_loadedAssemblies.TryGetValue(normalizedPath, out weakRef) && 
                    weakRef.TryGetTarget(out existingAssembly))
                {
                    return existingAssembly;
                }

                try
                {
                    // Load the assembly
                    var assembly = LoadAssemblyCore(normalizedPath);
                    
                    // Detect the actual framework version
                    var frameworkVersion = FrameworkDetector.DetectFrameworkVersion(normalizedPath);
                    
                    // Create the test assembly wrapper
                    var testAssembly = CreateTestAssembly(assembly, normalizedPath, frameworkVersion);
                    
                    // Cache using weak reference to allow GC if needed
                    _loadedAssemblies.AddOrUpdate(
                        normalizedPath, 
                        new WeakReference<ITestAssembly>(testAssembly),
                        (key, oldValue) => new WeakReference<ITestAssembly>(testAssembly));
                    
                    return testAssembly;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to load assembly '{normalizedPath}': {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// Core assembly loading logic that can be overridden by derived classes.
        /// </summary>
        /// <param name="assemblyPath">The path to the assembly to load.</param>
        /// <returns>The loaded assembly.</returns>
        protected virtual System.Reflection.Assembly LoadAssemblyCore(string assemblyPath)
        {
            // Use LoadFrom to allow loading assemblies from any location
            return System.Reflection.Assembly.LoadFrom(assemblyPath);
        }

        /// <inheritdoc />
        public override bool TryUnloadAssembly(ITestAssembly testAssembly)
        {
            if (testAssembly == null)
                return false;

            var pathToRemove = FindAssemblyPath(testAssembly);
            if (pathToRemove == null)
                return false;

            lock (_lockObject)
            {
                if (_loadedAssemblies.TryRemove(pathToRemove, out _))
                {
                    // Standard loader cannot truly unload assemblies in .NET Framework/.NET Standard
                    // The assembly will remain loaded until the AppDomain is unloaded
                    // But we can remove it from our tracking
                    try
                    {
                        testAssembly.Dispose();
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Finds the assembly path for a given test assembly.
        /// </summary>
        private string? FindAssemblyPath(ITestAssembly testAssembly)
        {
            foreach (var kvp in _loadedAssemblies)
            {
                if (kvp.Value.TryGetTarget(out var assembly) && ReferenceEquals(assembly, testAssembly))
                {
                    return kvp.Key;
                }
            }
            return null;
        }

        /// <summary>
        /// Handles assembly resolution events.
        /// </summary>
        private System.Reflection.Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
        {
            return AssemblyResolve?.Invoke(sender ?? this, args);
        }

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            try
            {
                // Unsubscribe from events
                AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;

                // Dispose all loaded assemblies
                foreach (var kvp in _loadedAssemblies)
                {
                    if (kvp.Value.TryGetTarget(out var assembly))
                    {
                        try
                        {
                            assembly.Dispose();
                        }
                        catch
                        {
                            // Ignore disposal errors
                        }
                    }
                }

                _loadedAssemblies.Clear();
            }
            catch
            {
                // Ignore errors during disposal
            }
        }
    }
}