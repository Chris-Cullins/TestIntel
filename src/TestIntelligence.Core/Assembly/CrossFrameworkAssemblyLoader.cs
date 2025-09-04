using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Core.Assembly.Loaders;

namespace TestIntelligence.Core.Assembly
{
    /// <summary>
    /// Cross-framework assembly loader that can handle multiple .NET framework versions with proper isolation.
    /// </summary>
    public class CrossFrameworkAssemblyLoader : ICrossFrameworkAssemblyLoader
    {
        private readonly Dictionary<FrameworkVersion, IAssemblyLoader> _loaders;
        private readonly ConcurrentDictionary<string, ITestAssembly> _loadedAssemblies;
        private readonly IAssemblyLoadLogger _logger;
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the CrossFrameworkAssemblyLoader.
        /// </summary>
        /// <param name="logger">Optional logger for assembly loading operations. Uses null logger if not provided.</param>
        public CrossFrameworkAssemblyLoader(IAssemblyLoadLogger? logger = null)
        {
            _loadedAssemblies = new ConcurrentDictionary<string, ITestAssembly>(StringComparer.OrdinalIgnoreCase);
            _loaders = new Dictionary<FrameworkVersion, IAssemblyLoader>();
            _logger = logger ?? NullAssemblyLoadLogger.Instance;
            
            InitializeLoaders();
        }

        /// <summary>
        /// Initializes framework-specific loaders.
        /// </summary>
        private void InitializeLoaders()
        {
            _logger.LogDebug("Initializing framework-specific assembly loaders");

            try
            {
                _loaders[FrameworkVersion.NetFramework48] = new Framework48LoaderCompatible();
                _logger.LogDebug("Framework48LoaderCompatible initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to initialize Framework48LoaderCompatible: {0}", ex.Message);
            }

            try
            {
                _loaders[FrameworkVersion.NetCore] = new IsolatedAssemblyLoader();
                _logger.LogDebug("IsolatedAssemblyLoader initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to initialize IsolatedAssemblyLoader, falling back to NetCoreLoaderCompatible: {0}", ex.Message);
                try
                {
                    _loaders[FrameworkVersion.NetCore] = new NetCoreLoaderCompatible();
                    _logger.LogDebug("NetCoreLoaderCompatible fallback initialized successfully");
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogWarning("Failed to initialize NetCoreLoaderCompatible fallback: {0}", fallbackEx.Message);
                }
            }

            try
            {
                _loaders[FrameworkVersion.Net5Plus] = new IsolatedAssemblyLoader();
                _logger.LogDebug("IsolatedAssemblyLoader for Net5Plus initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to initialize IsolatedAssemblyLoader for Net5Plus, falling back to Net5PlusLoaderCompatible: {0}", ex.Message);
                try
                {
                    _loaders[FrameworkVersion.Net5Plus] = new Net5PlusLoaderCompatible();
                    _logger.LogDebug("Net5PlusLoaderCompatible fallback initialized successfully");
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogWarning("Failed to initialize Net5PlusLoaderCompatible fallback: {0}", fallbackEx.Message);
                }
            }

            // Standard loader should always be available as fallback
            try
            {
                _loaders[FrameworkVersion.NetStandard] = new StandardLoader();
                _logger.LogDebug("StandardLoader initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize StandardLoader - this is a critical error");
                throw;
            }

            _logger.LogInformation("Initialized {0} assembly loaders", _loaders.Count);
        }

        /// <inheritdoc />
        public IReadOnlyList<FrameworkVersion> SupportedFrameworks => _loaders.Keys.ToList();

        /// <inheritdoc />
        public event EventHandler<AssemblyLoadedEventArgs>? AssemblyLoaded;

        /// <inheritdoc />
        public event EventHandler<AssemblyLoadFailedEventArgs>? AssemblyLoadFailed;

        /// <inheritdoc />
        public FrameworkVersion DetectFrameworkVersion(string assemblyPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
                throw new ArgumentNullException(nameof(assemblyPath));

            if (!File.Exists(assemblyPath))
                throw new FileNotFoundException($"Assembly file not found: {assemblyPath}", assemblyPath);

            try
            {
                _logger.LogDebug("Detecting framework version for assembly: {0}", assemblyPath);
                var frameworkVersion = FrameworkDetector.DetectFrameworkVersion(assemblyPath);
                _logger.LogDebug("Detected framework version {0} for assembly: {1}", frameworkVersion.GetDescription(), assemblyPath);
                return frameworkVersion;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to detect framework version for assembly: {0}", assemblyPath);
                throw new InvalidOperationException($"Failed to detect framework version for assembly: {assemblyPath}", ex);
            }
        }

        /// <inheritdoc />
        public async Task<AssemblyLoadResult> LoadAssemblyAsync(string assemblyPath, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => LoadAssembly(assemblyPath), cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public AssemblyLoadResult LoadAssembly(string assemblyPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
                return AssemblyLoadResult.Failure("Assembly path cannot be null or empty.");

            if (!File.Exists(assemblyPath))
                return AssemblyLoadResult.Failure($"Assembly file not found: {assemblyPath}");

            // Normalize path for consistent key lookup
            var normalizedPath = Path.GetFullPath(assemblyPath);

            // Check if already loaded
            if (_loadedAssemblies.TryGetValue(normalizedPath, out var existingAssembly))
            {
                return AssemblyLoadResult.Success(existingAssembly);
            }

            lock (_lockObject)
            {
                // Double-check after acquiring lock
                if (_loadedAssemblies.TryGetValue(normalizedPath, out existingAssembly))
                {
                    return AssemblyLoadResult.Success(existingAssembly);
                }

                try
                {
                    _logger.LogInformation("Loading assembly: {0}", normalizedPath);
                    
                    // Detect framework version
                    var frameworkVersion = DetectFrameworkVersion(normalizedPath);
                    
                    // Get appropriate loader
                    var loader = GetLoaderForFramework(frameworkVersion);
                    if (loader == null)
                    {
                        var error = $"No loader available for framework version: {frameworkVersion.GetDescription()}";
                        _logger.LogError("No loader available for framework version {0} for assembly: {1}", frameworkVersion.GetDescription(), normalizedPath);
                        OnAssemblyLoadFailed(normalizedPath, new[] { error }, frameworkVersion);
                        return AssemblyLoadResult.Failure(error);
                    }

                    _logger.LogDebug("Using {0} to load assembly: {1}", loader.GetType().Name, normalizedPath);
                    
                    // Load the assembly
                    var testAssembly = loader.LoadAssembly(normalizedPath);
                    
                    // Cache the loaded assembly
                    _loadedAssemblies.TryAdd(normalizedPath, testAssembly);
                    
                    _logger.LogInformation("Successfully loaded assembly {0} with framework {1}", testAssembly.AssemblyName, frameworkVersion.GetDescription());
                    
                    // Raise success event
                    OnAssemblyLoaded(normalizedPath, testAssembly, frameworkVersion);
                    
                    return AssemblyLoadResult.Success(testAssembly);
                }
                catch (Exception ex)
                {
                    var error = $"Failed to load assembly '{normalizedPath}': {ex.Message}";
                    _logger.LogError(ex, "Failed to load assembly: {0}", normalizedPath);
                    OnAssemblyLoadFailed(normalizedPath, new[] { error });
                    return AssemblyLoadResult.Failure(error);
                }
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyDictionary<string, AssemblyLoadResult>> LoadAssembliesAsync(
            IEnumerable<string> assemblyPaths, 
            CancellationToken cancellationToken = default)
        {
            if (assemblyPaths == null)
                throw new ArgumentNullException(nameof(assemblyPaths));

            var paths = assemblyPaths.ToList();
            var results = new ConcurrentDictionary<string, AssemblyLoadResult>();

            // Group paths by framework version for optimal loading
            var groupedPaths = new Dictionary<FrameworkVersion, List<string>>();
            
            foreach (var path in paths)
            {
                try
                {
                    var framework = DetectFrameworkVersion(path);
                    if (!groupedPaths.ContainsKey(framework))
                        groupedPaths[framework] = new List<string>();
                    
                    groupedPaths[framework].Add(path);
                }
                catch (Exception ex)
                {
                    results.TryAdd(path, AssemblyLoadResult.Failure($"Failed to detect framework: {ex.Message}"));
                }
            }

            // Load assemblies in parallel by framework group
            var tasks = groupedPaths.Select(async group =>
            {
                var frameworkVersion = group.Key;
                var frameworkPaths = group.Value;

                await Task.Run(() =>
                {
                    foreach (var path in frameworkPaths)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var result = LoadAssembly(path);
                        results.TryAdd(path, result);
                    }
                }, cancellationToken);
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
            
            return results;
        }

        /// <inheritdoc />
        public IReadOnlyList<ITestAssembly> GetLoadedAssemblies()
        {
            return _loadedAssemblies.Values.ToList();
        }

        /// <inheritdoc />
        public bool TryUnloadAssembly(ITestAssembly testAssembly)
        {
            if (testAssembly == null)
                return false;

            var pathToRemove = _loadedAssemblies
                .FirstOrDefault(kvp => ReferenceEquals(kvp.Value, testAssembly))
                .Key;

            if (pathToRemove == null)
                return false;

            lock (_lockObject)
            {
                if (_loadedAssemblies.TryRemove(pathToRemove, out var removedAssembly))
                {
                    // Try to get the appropriate loader for unloading
                    var loader = GetLoaderForFramework(removedAssembly.FrameworkVersion);
                    var unloaded = loader?.TryUnloadAssembly(removedAssembly) ?? false;
                    
                    // Dispose the assembly if it implements IDisposable
                    removedAssembly?.Dispose();
                    
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public void UnloadAllAssemblies()
        {
            lock (_lockObject)
            {
                var assemblies = _loadedAssemblies.Values.ToList();
                
                foreach (var assembly in assemblies)
                {
                    try
                    {
                        TryUnloadAssembly(assembly);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error unloading assembly {assembly.AssemblyName}: {ex.Message}");
                    }
                }

                _loadedAssemblies.Clear();
            }
        }

        /// <summary>
        /// Gets the appropriate loader for the specified framework version.
        /// </summary>
        private IAssemblyLoader? GetLoaderForFramework(FrameworkVersion frameworkVersion)
        {
            if (_loaders.TryGetValue(frameworkVersion, out var loader))
                return loader;

            // Fallback logic
            switch (frameworkVersion)
            {
                case FrameworkVersion.Unknown:
                    // Try Standard loader as fallback
                    return _loaders.TryGetValue(FrameworkVersion.NetStandard, out var standardLoader) 
                        ? standardLoader 
                        : null;

                case FrameworkVersion.NetFramework48:
                    // Fallback to Standard loader if Framework48Loader is not available
                    return _loaders.TryGetValue(FrameworkVersion.NetStandard, out var netStandardLoader) 
                        ? netStandardLoader 
                        : null;

                case FrameworkVersion.NetCore:
                    // Try Net5Plus loader as fallback, then Standard
                    return _loaders.TryGetValue(FrameworkVersion.Net5Plus, out var net5Loader) 
                        ? net5Loader 
                        : _loaders.TryGetValue(FrameworkVersion.NetStandard, out var coreStandardLoader) 
                            ? coreStandardLoader 
                            : null;

                default:
                    return null;
            }
        }

        /// <summary>
        /// Raises the AssemblyLoaded event.
        /// </summary>
        protected virtual void OnAssemblyLoaded(string assemblyPath, ITestAssembly testAssembly, FrameworkVersion frameworkVersion)
        {
            AssemblyLoaded?.Invoke(this, new AssemblyLoadedEventArgs(assemblyPath, testAssembly, frameworkVersion));
        }

        /// <summary>
        /// Raises the AssemblyLoadFailed event.
        /// </summary>
        protected virtual void OnAssemblyLoadFailed(string assemblyPath, IReadOnlyList<string> errors, FrameworkVersion? detectedFramework = null)
        {
            AssemblyLoadFailed?.Invoke(this, new AssemblyLoadFailedEventArgs(assemblyPath, errors, detectedFramework));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                UnloadAllAssemblies();

                foreach (var loader in _loaders.Values)
                {
                    try
                    {
                        loader?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error disposing loader: {ex.Message}");
                    }
                }

                _loaders.Clear();
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}// Test change for coverage analysis
