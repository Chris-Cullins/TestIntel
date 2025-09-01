using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Discovery;
using TestIntelligence.Core.Models;

namespace TestIntelligence.Core.Caching
{
    /// <summary>
    /// Caches assembly metadata and test discovery results to improve performance.
    /// Enhanced with persistent caching and solution-level dependency tracking.
    /// </summary>
    public class AssemblyMetadataCache : IDisposable
    {
        private readonly ICacheProvider _cacheProvider;
        private readonly SolutionCacheManager? _solutionCacheManager;
        private readonly TimeSpan _defaultExpiration;
        private volatile bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the AssemblyMetadataCache.
        /// </summary>
        /// <param name="cacheProvider">Cache provider to use. If null, uses in-memory cache.</param>
        /// <param name="solutionCacheManager">Solution-level cache manager for persistent storage.</param>
        /// <param name="defaultExpiration">Default expiration time for cached entries. Default is 1 hour.</param>
        public AssemblyMetadataCache(
            ICacheProvider? cacheProvider = null, 
            SolutionCacheManager? solutionCacheManager = null,
            TimeSpan? defaultExpiration = null)
        {
            _cacheProvider = cacheProvider ?? new MemoryCacheProvider();
            _solutionCacheManager = solutionCacheManager;
            _defaultExpiration = defaultExpiration ?? TimeSpan.FromHours(1);
        }

        /// <summary>
        /// Gets or caches test discovery results for an assembly.
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly.</param>
        /// <param name="discoveryFactory">Factory function to perform test discovery if not cached.</param>
        /// <param name="forceRefresh">Whether to force refresh the cache.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Test discovery result.</returns>
        public async Task<TestDiscoveryResult> GetOrCacheTestDiscoveryAsync(
            string assemblyPath,
            Func<Task<TestDiscoveryResult>> discoveryFactory,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            if (string.IsNullOrEmpty(assemblyPath))
                throw new ArgumentNullException(nameof(assemblyPath));

            if (discoveryFactory == null)
                throw new ArgumentNullException(nameof(discoveryFactory));

            var cacheKey = GetTestDiscoveryCacheKey(assemblyPath);

            if (forceRefresh)
            {
                await _cacheProvider.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
                if (_solutionCacheManager != null)
                {
                    await _solutionCacheManager.RegisterFileDependenciesAsync(
                        cacheKey, 
                        new[] { assemblyPath }, 
                        cancellationToken).ConfigureAwait(false);
                }
            }

            // Use solution cache manager if available for persistent caching
            if (_solutionCacheManager != null)
            {
                return await _solutionCacheManager.GetOrSetAsync(
                    cacheKey,
                    discoveryFactory,
                    new[] { assemblyPath }, // Register assembly as dependency
                    GetExpirationForAssembly(assemblyPath),
                    cancellationToken).ConfigureAwait(false);
            }

            // Fallback to regular cache provider
            return await _cacheProvider.GetOrSetAsync(
                cacheKey,
                async () =>
                {
                    var result = await discoveryFactory().ConfigureAwait(false);
                    return result;
                },
                GetExpirationForAssembly(assemblyPath),
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets or caches assembly load results.
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly.</param>
        /// <param name="loadFactory">Factory function to load the assembly if not cached.</param>
        /// <param name="forceRefresh">Whether to force refresh the cache.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Assembly load result.</returns>
        public async Task<AssemblyLoadResult> GetOrCacheAssemblyLoadAsync(
            string assemblyPath,
            Func<Task<AssemblyLoadResult>> loadFactory,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            if (string.IsNullOrEmpty(assemblyPath))
                throw new ArgumentNullException(nameof(assemblyPath));

            if (loadFactory == null)
                throw new ArgumentNullException(nameof(loadFactory));

            var cacheKey = GetAssemblyLoadCacheKey(assemblyPath);

            if (forceRefresh)
            {
                await _cacheProvider.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
                if (_solutionCacheManager != null)
                {
                    await _solutionCacheManager.RegisterFileDependenciesAsync(
                        cacheKey, 
                        new[] { assemblyPath }, 
                        cancellationToken).ConfigureAwait(false);
                }
            }

            // Use solution cache manager if available for persistent caching
            if (_solutionCacheManager != null)
            {
                return await _solutionCacheManager.GetOrSetAsync(
                    cacheKey,
                    loadFactory,
                    new[] { assemblyPath }, // Register assembly as dependency
                    GetExpirationForAssembly(assemblyPath),
                    cancellationToken).ConfigureAwait(false);
            }

            // Fallback to regular cache provider
            return await _cacheProvider.GetOrSetAsync(
                cacheKey,
                loadFactory,
                GetExpirationForAssembly(assemblyPath),
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Caches framework version detection results.
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly.</param>
        /// <param name="frameworkVersion">Detected framework version.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task CacheFrameworkVersionAsync(
            string assemblyPath,
            FrameworkVersion frameworkVersion,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            if (string.IsNullOrEmpty(assemblyPath))
                throw new ArgumentNullException(nameof(assemblyPath));

            var cacheKey = GetFrameworkVersionCacheKey(assemblyPath);
            var cacheValue = new FrameworkVersionCacheEntry(frameworkVersion);

            await _cacheProvider.SetAsync(
                cacheKey,
                cacheValue,
                GetExpirationForAssembly(assemblyPath),
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets cached framework version for an assembly.
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Cached framework version, or null if not found.</returns>
        public async Task<FrameworkVersion?> GetCachedFrameworkVersionAsync(
            string assemblyPath,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            if (string.IsNullOrEmpty(assemblyPath))
                return null;

            var cacheKey = GetFrameworkVersionCacheKey(assemblyPath);
            var cacheEntry = await _cacheProvider.GetAsync<FrameworkVersionCacheEntry>(cacheKey, cancellationToken).ConfigureAwait(false);

            return cacheEntry?.FrameworkVersion;
        }

        /// <summary>
        /// Invalidates cache entries for the specified assembly.
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task InvalidateAssemblyAsync(string assemblyPath, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            if (string.IsNullOrEmpty(assemblyPath))
                return;

            var tasks = new[]
            {
                _cacheProvider.RemoveAsync(GetTestDiscoveryCacheKey(assemblyPath), cancellationToken),
                _cacheProvider.RemoveAsync(GetAssemblyLoadCacheKey(assemblyPath), cancellationToken),
                _cacheProvider.RemoveAsync(GetFrameworkVersionCacheKey(assemblyPath), cancellationToken)
            };

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// Clears all cached entries.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task ClearAllAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await _cacheProvider.ClearAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks if any assemblies in the cache have been modified since caching.
        /// </summary>
        /// <param name="assemblyPaths">Paths to assemblies to check.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of assemblies that have been modified.</returns>
        public async Task<IReadOnlyList<string>> GetModifiedAssembliesAsync(
            IEnumerable<string> assemblyPaths,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            if (assemblyPaths == null)
                return Array.Empty<string>();

            var modifiedAssemblies = new List<string>();

            foreach (var assemblyPath in assemblyPaths)
            {
                if (await IsAssemblyModifiedAsync(assemblyPath, cancellationToken).ConfigureAwait(false))
                {
                    modifiedAssemblies.Add(assemblyPath);
                }
            }

            return modifiedAssemblies.AsReadOnly();
        }

        /// <summary>
        /// Checks if an assembly has been modified since it was last cached.
        /// </summary>
        private async Task<bool> IsAssemblyModifiedAsync(string assemblyPath, CancellationToken cancellationToken)
        {
            if (!File.Exists(assemblyPath))
                return true; // Assembly no longer exists

            try
            {
                var currentHash = await GetAssemblyHashAsync(assemblyPath).ConfigureAwait(false);
                var cacheKey = GetTestDiscoveryCacheKey(assemblyPath);
                
                var cachedResult = await _cacheProvider.GetAsync<TestDiscoveryResult>(cacheKey, cancellationToken).ConfigureAwait(false);
                if (cachedResult == null)
                    return true; // Not in cache

                // For simplicity, we'll check if the cache entry is older than the assembly's last write time
                var assemblyLastWrite = File.GetLastWriteTimeUtc(assemblyPath);
                return cachedResult.DiscoveredAt < assemblyLastWrite;
            }
            catch
            {
                return true; // Assume modified on error
            }
        }

        /// <summary>
        /// Generates a cache key for test discovery results.
        /// </summary>
        private string GetTestDiscoveryCacheKey(string assemblyPath)
        {
            var normalizedPath = Path.GetFullPath(assemblyPath);
            return $"TestDiscovery:{GetAssemblyHash(normalizedPath)}";
        }

        /// <summary>
        /// Generates a cache key for assembly load results.
        /// </summary>
        private string GetAssemblyLoadCacheKey(string assemblyPath)
        {
            var normalizedPath = Path.GetFullPath(assemblyPath);
            return $"AssemblyLoad:{GetAssemblyHash(normalizedPath)}";
        }

        /// <summary>
        /// Generates a cache key for framework version results.
        /// </summary>
        private string GetFrameworkVersionCacheKey(string assemblyPath)
        {
            var normalizedPath = Path.GetFullPath(assemblyPath);
            return $"FrameworkVersion:{GetAssemblyHash(normalizedPath)}";
        }

        /// <summary>
        /// Gets the cache expiration time for an assembly based on its characteristics.
        /// </summary>
        private TimeSpan GetExpirationForAssembly(string assemblyPath)
        {
            // For now, use the default expiration. In the future, this could be
            // customized based on assembly properties (e.g., debug vs release builds)
            return _defaultExpiration;
        }

        /// <summary>
        /// Generates a hash for an assembly path and its last write time.
        /// </summary>
        private string GetAssemblyHash(string assemblyPath)
        {
            try
            {
                var lastWriteTime = File.Exists(assemblyPath) 
                    ? File.GetLastWriteTimeUtc(assemblyPath).ToBinary().ToString() 
                    : "0";
                
                var input = $"{assemblyPath}:{lastWriteTime}";
                return ComputeHash(input);
            }
            catch
            {
                return ComputeHash(assemblyPath);
            }
        }

        /// <summary>
        /// Generates a hash for an assembly path and its last write time asynchronously.
        /// </summary>
        private async Task<string> GetAssemblyHashAsync(string assemblyPath)
        {
            return await Task.Run(() => GetAssemblyHash(assemblyPath)).ConfigureAwait(false);
        }

        /// <summary>
        /// Computes a SHA-256 hash of the input string.
        /// </summary>
        private static string ComputeHash(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hashBytes);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AssemblyMetadataCache));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cacheProvider?.Dispose();
        }

        /// <summary>
        /// Cache entry for framework version information.
        /// </summary>
        private class FrameworkVersionCacheEntry
        {
            public FrameworkVersionCacheEntry(FrameworkVersion frameworkVersion)
            {
                FrameworkVersion = frameworkVersion;
                CachedAt = DateTimeOffset.UtcNow;
            }

            public FrameworkVersion FrameworkVersion { get; }
            public DateTimeOffset CachedAt { get; }
        }
    }
}