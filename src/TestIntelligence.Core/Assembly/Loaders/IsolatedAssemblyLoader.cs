using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;

namespace TestIntelligence.Core.Assembly.Loaders
{
    /// <summary>
    /// Enhanced assembly loader that provides better dependency resolution for complex assemblies like ASP.NET Core tests.
    /// Uses enhanced dependency resolution strategies to handle missing runtime dependencies.
    /// </summary>
    public class IsolatedAssemblyLoader : BaseAssemblyLoader
    {
        private readonly ConcurrentDictionary<string, WeakReference<ITestAssembly>> _loadedAssemblies;
        private readonly object _lockObject = new object();
        private readonly string[] _aspNetCoreSharedPaths;

        /// <summary>
        /// Initializes a new instance of the IsolatedAssemblyLoader.
        /// </summary>
        public IsolatedAssemblyLoader() : base(FrameworkVersion.NetCore)
        {
            _loadedAssemblies = new ConcurrentDictionary<string, WeakReference<ITestAssembly>>(StringComparer.OrdinalIgnoreCase);
            
            // Initialize ASP.NET Core shared framework paths
            _aspNetCoreSharedPaths = GetAspNetCoreSharedPaths();
            
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
            // IsolatedAssemblyLoader handles .NET Core and newer frameworks
            return frameworkVersion == FrameworkVersion.NetCore ||
                   frameworkVersion == FrameworkVersion.Net5Plus ||
                   frameworkVersion == FrameworkVersion.NetStandard;
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
                    // Load the assembly with enhanced dependency resolution
                    var assembly = LoadAssemblyWithEnhancedResolution(normalizedPath);
                    
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
        /// Loads assembly with enhanced dependency resolution for ASP.NET Core dependencies.
        /// </summary>
        protected virtual System.Reflection.Assembly LoadAssemblyWithEnhancedResolution(string assemblyPath)
        {
            try
            {
                // First try LoadFrom with our enhanced resolver
                return System.Reflection.Assembly.LoadFrom(assemblyPath);
            }
            catch (Exception ex) when (ex.Message.Contains("Could not load file or assembly"))
            {
                // If LoadFrom fails due to missing dependencies, try UnsafeLoadFrom
                // This loads the assembly without loading its dependencies
                try
                {
                    return System.Reflection.Assembly.UnsafeLoadFrom(assemblyPath);
                }
                catch
                {
                    // If that fails too, re-throw the original exception
                    throw;
                }
            }
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
        /// Enhanced assembly resolution that searches for ASP.NET Core shared framework dependencies.
        /// </summary>
        private System.Reflection.Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
        {
            try
            {
                // First try the base implementation
                var resolved = AssemblyResolve?.Invoke(sender ?? this, args);
                if (resolved != null)
                    return resolved;

                // Extract assembly name
                var assemblyName = new AssemblyName(args.Name);
                var fileName = assemblyName.Name + ".dll";

                // Search in the directory of the requesting assembly
                if (args.RequestingAssembly != null && !string.IsNullOrEmpty(args.RequestingAssembly.Location))
                {
                    var requestingDir = Path.GetDirectoryName(args.RequestingAssembly.Location);
                    if (!string.IsNullOrEmpty(requestingDir))
                    {
                        var localPath = Path.Combine(requestingDir, fileName);
                        if (File.Exists(localPath))
                        {
                            return System.Reflection.Assembly.LoadFrom(localPath);
                        }
                    }
                }

                // For ASP.NET Core assemblies, try shared framework locations
                if (assemblyName.Name?.StartsWith("Microsoft.AspNetCore") == true)
                {
                    var sharedFrameworkPath = FindSharedFrameworkAssembly(assemblyName.Name);
                    if (sharedFrameworkPath != null)
                    {
                        return System.Reflection.Assembly.LoadFrom(sharedFrameworkPath);
                    }
                }

                // Search in loaded assembly directories
                foreach (var loadedPath in _loadedAssemblies.Keys)
                {
                    var loadedDir = Path.GetDirectoryName(loadedPath);
                    if (!string.IsNullOrEmpty(loadedDir))
                    {
                        var dependencyPath = Path.Combine(loadedDir, fileName);
                        if (File.Exists(dependencyPath))
                        {
                            return System.Reflection.Assembly.LoadFrom(dependencyPath);
                        }
                    }
                }

                // Search in current directory
                var currentDirPath = Path.Combine(Environment.CurrentDirectory, fileName);
                if (File.Exists(currentDirPath))
                {
                    return System.Reflection.Assembly.LoadFrom(currentDirPath);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets potential ASP.NET Core shared framework paths for this platform.
        /// </summary>
        private static string[] GetAspNetCoreSharedPaths()
        {
            var paths = new[]
            {
                "/usr/local/share/dotnet/shared/Microsoft.AspNetCore.App",
                "/usr/share/dotnet/shared/Microsoft.AspNetCore.App",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.AspNetCore.App"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", "shared", "Microsoft.AspNetCore.App")
            };

            return paths;
        }

        /// <summary>
        /// Attempts to find an assembly in the ASP.NET Core shared framework.
        /// </summary>
        private string? FindSharedFrameworkAssembly(string assemblyName)
        {
            var fileName = assemblyName + ".dll";
            
            foreach (var basePath in _aspNetCoreSharedPaths)
            {
                if (!Directory.Exists(basePath))
                    continue;

                try
                {
                    // Look for the latest version directory
                    var versionDirs = Directory.GetDirectories(basePath);
                    Array.Sort(versionDirs, StringComparer.OrdinalIgnoreCase);
                    
                    for (int i = versionDirs.Length - 1; i >= 0; i--)
                    {
                        var assemblyPath = Path.Combine(versionDirs[i], fileName);
                        if (File.Exists(assemblyPath))
                        {
                            return assemblyPath;
                        }
                    }
                }
                catch
                {
                    // Continue to next path
                }
            }

            return null;
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