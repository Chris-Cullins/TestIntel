using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TestIntelligence.Core.Caching
{
    /// <summary>
    /// In-memory cache provider implementation using ConcurrentDictionary.
    /// </summary>
    public class MemoryCacheProvider : ICacheProvider, IDisposable
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _cache;
        private readonly Timer _cleanupTimer;
        private readonly object _lockObject = new object();
        private volatile bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the MemoryCacheProvider.
        /// </summary>
        /// <param name="cleanupIntervalSeconds">Interval in seconds for cleaning up expired entries. Default is 300 (5 minutes).</param>
        public MemoryCacheProvider(int cleanupIntervalSeconds = 300)
        {
            _cache = new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
            
            // Set up periodic cleanup of expired entries
            _cleanupTimer = new Timer(CleanupExpiredEntries, null, 
                TimeSpan.FromSeconds(cleanupIntervalSeconds), 
                TimeSpan.FromSeconds(cleanupIntervalSeconds));
        }

        /// <inheritdoc />
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
        {
            ThrowIfDisposed();
            
            if (string.IsNullOrEmpty(key))
                return Task.FromResult<T?>(null);

            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.IsExpired)
                {
                    _cache.TryRemove(key, out _);
                    return Task.FromResult<T?>(null);
                }

                return Task.FromResult(entry.Value as T);
            }

            return Task.FromResult<T?>(null);
        }

        /// <inheritdoc />
        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
        {
            ThrowIfDisposed();
            
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            if (value == null)
                throw new ArgumentNullException(nameof(value));

            var entry = new CacheEntry(value, expiration);
            _cache.AddOrUpdate(key, entry, (k, v) => entry);
            
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            if (string.IsNullOrEmpty(key))
                return Task.FromResult(false);

            var result = _cache.TryRemove(key, out _);
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            if (string.IsNullOrEmpty(key))
                return Task.FromResult(false);

            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.IsExpired)
                {
                    _cache.TryRemove(key, out _);
                    return Task.FromResult(false);
                }

                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        /// <inheritdoc />
        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            _cache.Clear();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
        {
            ThrowIfDisposed();
            
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            // Try to get existing value
            var existing = await GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
            if (existing != null)
                return existing;

            // Use double-checked locking pattern for thread safety
            lock (_lockObject)
            {
                // Check again after acquiring lock
                if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
                {
                    return (T)entry.Value;
                }
            }

            // Create new value
            var newValue = await factory().ConfigureAwait(false);
            await SetAsync(key, newValue, expiration, cancellationToken).ConfigureAwait(false);
            return newValue;
        }

        /// <summary>
        /// Gets cache statistics for monitoring.
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            ThrowIfDisposed();
            
            var totalEntries = _cache.Count;
            var expiredEntries = 0;

            foreach (var entry in _cache.Values)
            {
                if (entry.IsExpired)
                    expiredEntries++;
            }

            return new CacheStatistics
            {
                TotalEntries = totalEntries,
                ExpiredEntries = expiredEntries,
                ActiveEntries = totalEntries - expiredEntries
            };
        }

        /// <summary>
        /// Manually triggers cleanup of expired entries.
        /// </summary>
        public void CleanupExpiredEntries()
        {
            if (_disposed)
                return;

            var expiredKeys = new List<string>();

            foreach (var kvp in _cache)
            {
                if (kvp.Value.IsExpired)
                    expiredKeys.Add(kvp.Key);
            }

            foreach (var key in expiredKeys)
            {
                _cache.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// Timer callback for cleaning up expired entries.
        /// </summary>
        private void CleanupExpiredEntries(object? state)
        {
            try
            {
                CleanupExpiredEntries();
            }
            catch
            {
                // Ignore cleanup errors to prevent timer from stopping
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MemoryCacheProvider));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cleanupTimer?.Dispose();
            _cache.Clear();
        }

        /// <summary>
        /// Represents a cache entry with expiration support.
        /// </summary>
        private class CacheEntry
        {
            public CacheEntry(object value, TimeSpan? expiration)
            {
                Value = value;
                CreatedAt = DateTimeOffset.UtcNow;
                ExpiresAt = expiration.HasValue ? CreatedAt.Add(expiration.Value) : null;
            }

            public object Value { get; }
            public DateTimeOffset CreatedAt { get; }
            public DateTimeOffset? ExpiresAt { get; }

            public bool IsExpired => ExpiresAt.HasValue && DateTimeOffset.UtcNow > ExpiresAt.Value;
        }
    }

    /// <summary>
    /// Cache statistics for monitoring.
    /// </summary>
    public class CacheStatistics
    {
        public int TotalEntries { get; set; }
        public int ActiveEntries { get; set; }
        public int ExpiredEntries { get; set; }
    }
}