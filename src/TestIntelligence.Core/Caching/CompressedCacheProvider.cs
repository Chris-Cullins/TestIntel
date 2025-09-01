using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.Core.Caching
{
    /// <summary>
    /// A compressed cache provider that stores data with GZip compression and manages storage limits through LRU eviction.
    /// </summary>
    public class CompressedCacheProvider<T> : ICompressedCache<T> where T : class
    {
        private readonly string _cacheDirectory;
        private readonly CompressedCacheOptions _options;
        private readonly ILogger<CompressedCacheProvider<T>>? _logger;
        private readonly SemaphoreSlim _ioSemaphore;
        private readonly ConcurrentDictionary<string, CacheEntryMetadata> _metadata;
        private readonly Timer? _maintenanceTimer;
        private readonly object _statsLock = new();
        
        private CacheCompressionStats _stats = new();
        private volatile bool _disposed = false;

        public CompressedCacheProvider(
            string? cacheDirectory = null,
            CompressedCacheOptions? options = null,
            ILogger<CompressedCacheProvider<T>>? logger = null)
        {
            _logger = logger;
            _options = options ?? new CompressedCacheOptions();
            _cacheDirectory = cacheDirectory ?? Path.Combine(Path.GetTempPath(), "TestIntelligence", "CompressedCache", typeof(T).Name);
            _ioSemaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);
            _metadata = new ConcurrentDictionary<string, CacheEntryMetadata>();

            EnsureCacheDirectoryExists();
            LoadMetadataIndex();

            if (_options.EnableBackgroundMaintenance)
            {
                _maintenanceTimer = new Timer(
                    async _ => await PerformMaintenanceAsync(),
                    null,
                    _options.MaintenanceInterval,
                    _options.MaintenanceInterval);
            }
        }

        public async Task<T?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(key))
                return null;

            var filePath = GetCacheFilePath(key);
            
            if (!File.Exists(filePath) || !_metadata.TryGetValue(key, out var metadata))
            {
                IncrementMiss();
                return null;
            }

            // Check if expired
            if (metadata.ExpiresAt.HasValue && DateTime.UtcNow > metadata.ExpiresAt.Value)
            {
                await RemoveAsync(key, cancellationToken);
                IncrementMiss();
                return null;
            }

            await _ioSemaphore.WaitAsync(cancellationToken);
            try
            {
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var compressedData = new CompressedData
                {
                    Data = new byte[fileStream.Length]
                };
                
                await fileStream.ReadAsync(compressedData.Data, 0, compressedData.Data.Length, cancellationToken);
                
                var result = await CacheCompressionUtilities.DecompressAsync<T>(compressedData, cancellationToken);
                
                if (result != null)
                {
                    // Update access time for LRU
                    metadata.LastAccessedAt = DateTime.UtcNow;
                    IncrementHit();
                    _logger?.LogDebug("Cache hit for key: {Key}", key);
                }
                else
                {
                    IncrementMiss();
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read compressed cache entry for key: {Key}", key);
                IncrementMiss();
                return null;
            }
            finally
            {
                _ioSemaphore.Release();
            }
        }

        public async Task SetAsync(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            if (value == null)
                throw new ArgumentNullException(nameof(value));

            await _ioSemaphore.WaitAsync(cancellationToken);
            try
            {
                var compressedData = await CacheCompressionUtilities.CompressAsync(value, _options.CompressionLevel, cancellationToken);
                var filePath = GetCacheFilePath(key);

                var metadata = new CacheEntryMetadata
                {
                    Key = key,
                    CreatedAt = DateTime.UtcNow,
                    LastAccessedAt = DateTime.UtcNow,
                    ExpiresAt = expiration.HasValue ? DateTime.UtcNow.Add(expiration.Value) : null,
                    CompressedSize = compressedData.CompressedSize,
                    UncompressedSize = compressedData.UncompressedSize,
                    CompressionRatio = compressedData.CompressionRatio
                };

                // Check if we need to make space
                await EnsureStorageCapacityAsync(compressedData.CompressedSize, cancellationToken);

                // Write compressed data to file
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await fileStream.WriteAsync(compressedData.Data, 0, compressedData.Data.Length, cancellationToken);
                }

                _metadata[key] = metadata;
                UpdateStats(compressedData);

                _logger?.LogDebug("Compressed cache entry written for key: {Key}, compression ratio: {Ratio:P2}", 
                    key, compressedData.CompressionRatio);
            }
            finally
            {
                _ioSemaphore.Release();
            }
        }

        public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(key))
                return false;

            var filePath = GetCacheFilePath(key);
            
            if (!File.Exists(filePath))
                return false;

            await _ioSemaphore.WaitAsync(cancellationToken);
            try
            {
                File.Delete(filePath);
                
                if (_metadata.TryRemove(key, out var metadata))
                {
                    RemoveFromStats(metadata);
                }

                _logger?.LogDebug("Compressed cache entry removed for key: {Key}", key);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to remove compressed cache entry for key: {Key}", key);
                return false;
            }
            finally
            {
                _ioSemaphore.Release();
            }
        }

        public Task<long?> GetCompressedSizeAsync(string key, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(key))
                return Task.FromResult<long?>(null);

            if (_metadata.TryGetValue(key, out var metadata))
            {
                return Task.FromResult<long?>(metadata.CompressedSize);
            }

            var filePath = GetCacheFilePath(key);
            if (File.Exists(filePath))
            {
                return Task.Run(async () =>
                {
                    await _ioSemaphore.WaitAsync(cancellationToken);
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        return (long?)fileInfo.Length;
                    }
                    finally
                    {
                        _ioSemaphore.Release();
                    }
                });
            }

            return Task.FromResult<long?>(null);
        }

        public Task<CacheCompressionStats> GetStatsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            lock (_statsLock)
            {
                return Task.FromResult(new CacheCompressionStats
                {
                    TotalEntries = _stats.TotalEntries,
                    TotalCompressedSize = _stats.TotalCompressedSize,
                    TotalUncompressedSize = _stats.TotalUncompressedSize,
                    HitCount = _stats.HitCount,
                    MissCount = _stats.MissCount,
                    EvictionCount = _stats.EvictionCount,
                    LastMaintenanceRun = _stats.LastMaintenanceRun
                });
            }
        }

        public async Task PerformMaintenanceAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _ioSemaphore.WaitAsync(cancellationToken);
            try
            {
                var expiredCount = await CleanupExpiredEntriesAsync(cancellationToken);
                var evictedCount = await EnforceStorageLimitsAsync(cancellationToken);

                lock (_statsLock)
                {
                    _stats.LastMaintenanceRun = DateTime.UtcNow;
                    _stats.EvictionCount += evictedCount;
                }

                _logger?.LogInformation("Cache maintenance completed: {ExpiredCount} expired, {EvictedCount} evicted", 
                    expiredCount, evictedCount);
            }
            finally
            {
                _ioSemaphore.Release();
            }
        }

        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!Directory.Exists(_cacheDirectory))
                return;

            await _ioSemaphore.WaitAsync(cancellationToken);
            try
            {
                var files = Directory.GetFiles(_cacheDirectory, "*.cache", SearchOption.AllDirectories);
                
                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to delete cache file: {FilePath}", file);
                    }
                }

                _metadata.Clear();
                ResetStats();

                _logger?.LogInformation("Compressed cache cleared: {FileCount} files deleted", files.Length);
            }
            finally
            {
                _ioSemaphore.Release();
            }
        }

        public async Task<T> GetOrSetAsync(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            // Try to get existing value
            var existing = await GetAsync(key, cancellationToken);
            if (existing != null)
                return existing;

            // Create new value
            var newValue = await factory();
            if (newValue != null)
            {
                await SetAsync(key, newValue, expiration, cancellationToken);
            }
            
            return newValue!;
        }

        private async Task<int> CleanupExpiredEntriesAsync(CancellationToken cancellationToken)
        {
            var expiredKeys = _metadata
                .Where(kvp => kvp.Value.ExpiresAt.HasValue && DateTime.UtcNow > kvp.Value.ExpiresAt.Value)
                .Select(kvp => kvp.Key)
                .ToList();

            var deletedCount = 0;
            foreach (var key in expiredKeys)
            {
                if (await RemoveAsync(key, cancellationToken))
                {
                    deletedCount++;
                }
            }

            return deletedCount;
        }

        private async Task<int> EnforceStorageLimitsAsync(CancellationToken cancellationToken)
        {
            var currentSize = _metadata.Values.Sum(m => m.CompressedSize);
            
            if (currentSize <= _options.MaxCacheSizeBytes)
                return 0;

            // Sort by LRU (least recently used first)
            var entriesToEvict = _metadata.Values
                .OrderBy(m => m.LastAccessedAt)
                .ToList();

            var evictedCount = 0;
            var targetSize = (long)(_options.MaxCacheSizeBytes * 0.8); // Target 80% of max size

            foreach (var entry in entriesToEvict)
            {
                if (currentSize <= targetSize)
                    break;

                if (await RemoveAsync(entry.Key, cancellationToken))
                {
                    currentSize -= entry.CompressedSize;
                    evictedCount++;
                }
            }

            return evictedCount;
        }

        private async Task EnsureStorageCapacityAsync(int requiredSize, CancellationToken cancellationToken)
        {
            var currentSize = _metadata.Values.Sum(m => m.CompressedSize);
            
            if (currentSize + requiredSize <= _options.MaxCacheSizeBytes)
                return;

            await EnforceStorageLimitsAsync(cancellationToken);
        }

        private void LoadMetadataIndex()
        {
            if (!Directory.Exists(_cacheDirectory))
                return;

            var files = Directory.GetFiles(_cacheDirectory, "*.cache", SearchOption.AllDirectories);
            
            foreach (var file in files)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    var fileName = Path.GetFileNameWithoutExtension(fileInfo.Name);
                    
                    // Basic metadata from file system
                    var metadata = new CacheEntryMetadata
                    {
                        Key = fileName,
                        CreatedAt = fileInfo.CreationTimeUtc,
                        LastAccessedAt = fileInfo.LastAccessTimeUtc,
                        CompressedSize = (int)fileInfo.Length
                    };

                    _metadata[fileName] = metadata;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to load metadata for cache file: {FilePath}", file);
                }
            }

            RecalculateStats();
        }

        private void UpdateStats(CompressedData data)
        {
            lock (_statsLock)
            {
                _stats.TotalEntries++;
                _stats.TotalCompressedSize += data.CompressedSize;
                _stats.TotalUncompressedSize += data.UncompressedSize;
            }
        }

        private void RemoveFromStats(CacheEntryMetadata metadata)
        {
            lock (_statsLock)
            {
                _stats.TotalEntries--;
                _stats.TotalCompressedSize -= metadata.CompressedSize;
                _stats.TotalUncompressedSize -= metadata.UncompressedSize;
            }
        }

        private void IncrementHit()
        {
            lock (_statsLock)
            {
                _stats.HitCount++;
            }
        }

        private void IncrementMiss()
        {
            lock (_statsLock)
            {
                _stats.MissCount++;
            }
        }

        private void ResetStats()
        {
            lock (_statsLock)
            {
                _stats = new CacheCompressionStats();
            }
        }

        private void RecalculateStats()
        {
            lock (_statsLock)
            {
                _stats.TotalEntries = _metadata.Count;
                _stats.TotalCompressedSize = _metadata.Values.Sum(m => m.CompressedSize);
                _stats.TotalUncompressedSize = _metadata.Values.Sum(m => m.UncompressedSize);
            }
        }

        private void EnsureCacheDirectoryExists()
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                Directory.CreateDirectory(_cacheDirectory);
                _logger?.LogDebug("Created compressed cache directory: {Directory}", _cacheDirectory);
            }
        }

        private string GetCacheFilePath(string key)
        {
            var safeName = GetSafeFileName(key);
            return Path.Combine(_cacheDirectory, $"{safeName}.cache");
        }

        private string GetSafeFileName(string key)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CompressedCacheProvider<T>));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _maintenanceTimer?.Dispose();
            _ioSemaphore?.Dispose();
        }
    }

    /// <summary>
    /// Metadata for cached entries.
    /// </summary>
    internal class CacheEntryMetadata
    {
        public string Key { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime LastAccessedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public int CompressedSize { get; set; }
        public int UncompressedSize { get; set; }
        public double CompressionRatio { get; set; }
    }

    /// <summary>
    /// Configuration options for compressed cache storage.
    /// </summary>
    public class CompressedCacheOptions
    {
        public long MaxCacheSizeBytes { get; set; } = 500 * 1024 * 1024; // 500MB
        public int MaxEntriesPerProject { get; set; } = 10; // Keep last 10 analyses
        public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(30);
        public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;
        public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromMinutes(30);
        public bool EnableBackgroundMaintenance { get; set; } = true;
    }
}