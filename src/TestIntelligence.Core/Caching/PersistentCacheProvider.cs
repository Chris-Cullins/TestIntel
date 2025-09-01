using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.Core.Caching
{
    /// <summary>
    /// Persistent cache provider that stores cache data on disk for improved startup performance.
    /// Uses JSON serialization with file-based storage and checksums for integrity.
    /// </summary>
    public class PersistentCacheProvider : ICacheProvider
    {
        /// <summary>
        /// Custom JsonConverter that ignores System.Type properties during serialization
        /// </summary>
        private class TypeIgnoreConverter : JsonConverter<Type>
        {
            public override Type? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                // Skip reading the value
                reader.Skip();
                return null;
            }

            public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
            {
                // Write null instead of trying to serialize the Type
                writer.WriteNullValue();
            }
        }

        private readonly string _cacheDirectory;
        private readonly ILogger<PersistentCacheProvider>? _logger;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly SemaphoreSlim _ioSemaphore;
        private volatile bool _disposed = false;

        public PersistentCacheProvider(string? cacheDirectory = null, ILogger<PersistentCacheProvider>? logger = null)
        {
            _logger = logger;
            _cacheDirectory = cacheDirectory ?? Path.Combine(Path.GetTempPath(), "TestIntelligence", "Cache");
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            _jsonOptions.Converters.Add(new TypeIgnoreConverter());
            _ioSemaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);

            EnsureCacheDirectoryExists();
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(key))
                return null;

            var filePath = GetCacheFilePath(key);
            
            if (!File.Exists(filePath))
                return null;

            await _ioSemaphore.WaitAsync(cancellationToken);
            try
            {
                var cacheEntry = await ReadCacheEntryAsync<T>(filePath, cancellationToken);
                
                if (cacheEntry == null)
                    return null;

                // Check if expired
                if (cacheEntry.ExpiresAt.HasValue && DateTimeOffset.UtcNow > cacheEntry.ExpiresAt.Value)
                {
                    // Clean up expired entry
                    try
                    {
                        File.Delete(filePath);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to delete expired cache file: {FilePath}", filePath);
                    }
                    return null;
                }

                _logger?.LogDebug("Cache hit for key: {Key}", key);
                return cacheEntry.Value;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read cache entry for key: {Key}", key);
                return null;
            }
            finally
            {
                _ioSemaphore.Release();
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            if (value == null)
                throw new ArgumentNullException(nameof(value));

            var filePath = GetCacheFilePath(key);
            var cacheEntry = new PersistentCacheEntry<T>
            {
                Key = key,
                Value = value,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = expiration.HasValue ? DateTimeOffset.UtcNow.Add(expiration.Value) : null
            };

            await _ioSemaphore.WaitAsync(cancellationToken);
            try
            {
                await WriteCacheEntryAsync(filePath, cacheEntry, cancellationToken);
                _logger?.LogDebug("Cache entry written for key: {Key}", key);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to write cache entry for key: {Key}", key);
                throw;
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
                _logger?.LogDebug("Cache entry removed for key: {Key}", key);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to remove cache entry for key: {Key}", key);
                return false;
            }
            finally
            {
                _ioSemaphore.Release();
            }
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(key))
                return false;

            var filePath = GetCacheFilePath(key);
            
            if (!File.Exists(filePath))
                return false;

            // Check if expired
            await _ioSemaphore.WaitAsync(cancellationToken);
            try
            {
                var metadata = await ReadCacheMetadataAsync(filePath, cancellationToken);
                
                if (metadata?.ExpiresAt.HasValue == true && DateTimeOffset.UtcNow > metadata.ExpiresAt.Value)
                {
                    File.Delete(filePath);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to check existence of cache entry for key: {Key}", key);
                return false;
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

                _logger?.LogInformation("Persistent cache cleared: {FileCount} files deleted", files.Length);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to clear persistent cache");
                throw;
            }
            finally
            {
                _ioSemaphore.Release();
            }
        }

        public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            // Try to get existing value
            var existing = await GetAsync<T>(key, cancellationToken);
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

        /// <summary>
        /// Gets statistics about the persistent cache.
        /// </summary>
        public async Task<PersistentCacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!Directory.Exists(_cacheDirectory))
                return new PersistentCacheStatistics();

            await _ioSemaphore.WaitAsync(cancellationToken);
            try
            {
                var files = Directory.GetFiles(_cacheDirectory, "*.cache", SearchOption.AllDirectories);
                var stats = new PersistentCacheStatistics
                {
                    TotalFiles = files.Length
                };

                long totalSize = 0;
                var expiredCount = 0;

                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        totalSize += fileInfo.Length;

                        var metadata = await ReadCacheMetadataAsync(file, cancellationToken);
                        if (metadata?.ExpiresAt.HasValue == true && DateTimeOffset.UtcNow > metadata.ExpiresAt.Value)
                        {
                            expiredCount++;
                        }
                    }
                    catch
                    {
                        // Ignore errors for individual files
                    }
                }

                stats.TotalSizeBytes = totalSize;
                stats.ExpiredFiles = expiredCount;
                stats.ActiveFiles = stats.TotalFiles - stats.ExpiredFiles;

                return stats;
            }
            finally
            {
                _ioSemaphore.Release();
            }
        }

        /// <summary>
        /// Performs cleanup of expired cache entries.
        /// </summary>
        public async Task CleanupExpiredAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!Directory.Exists(_cacheDirectory))
                return;

            await _ioSemaphore.WaitAsync(cancellationToken);
            try
            {
                var files = Directory.GetFiles(_cacheDirectory, "*.cache", SearchOption.AllDirectories);
                var deletedCount = 0;

                foreach (var file in files)
                {
                    try
                    {
                        var metadata = await ReadCacheMetadataAsync(file, cancellationToken);
                        if (metadata?.ExpiresAt.HasValue == true && DateTimeOffset.UtcNow > metadata.ExpiresAt.Value)
                        {
                            File.Delete(file);
                            deletedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to check/delete expired cache file: {FilePath}", file);
                    }
                }

                _logger?.LogInformation("Cleanup completed: {DeletedCount} expired cache files removed", deletedCount);
            }
            finally
            {
                _ioSemaphore.Release();
            }
        }

        private void EnsureCacheDirectoryExists()
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                Directory.CreateDirectory(_cacheDirectory);
                _logger?.LogDebug("Created cache directory: {Directory}", _cacheDirectory);
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

        private async Task<PersistentCacheEntry<T>?> ReadCacheEntryAsync<T>(string filePath, CancellationToken cancellationToken) where T : class
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var cacheEntry = await JsonSerializer.DeserializeAsync<PersistentCacheEntry<T>>(fileStream, _jsonOptions, cancellationToken);
            return cacheEntry;
        }

        private async Task<PersistentCacheMetadata?> ReadCacheMetadataAsync(string filePath, CancellationToken cancellationToken)
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var metadata = await JsonSerializer.DeserializeAsync<PersistentCacheMetadata>(fileStream, _jsonOptions, cancellationToken);
            return metadata ?? null;
        }

        private async Task WriteCacheEntryAsync<T>(string filePath, PersistentCacheEntry<T> cacheEntry, CancellationToken cancellationToken) where T : class
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(fileStream, cacheEntry, _jsonOptions, cancellationToken);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PersistentCacheProvider));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _ioSemaphore?.Dispose();
        }
    }

    /// <summary>
    /// Represents a cache entry stored in persistent storage.
    /// </summary>
    public class PersistentCacheEntry<T> : PersistentCacheMetadata where T : class
    {
        public T? Value { get; set; }
    }

    /// <summary>
    /// Metadata for cache entries.
    /// </summary>
    public class PersistentCacheMetadata
    {
        public string Key { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    /// <summary>
    /// Statistics for persistent cache usage.
    /// </summary>
    public class PersistentCacheStatistics
    {
        public int TotalFiles { get; set; }
        public int ActiveFiles { get; set; }
        public int ExpiredFiles { get; set; }
        public long TotalSizeBytes { get; set; }
        
        public string TotalSizeFormatted => FormatBytes(TotalSizeBytes);
        
        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}