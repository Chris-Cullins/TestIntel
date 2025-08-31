using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace TestIntelligence.ImpactAnalyzer.Caching
{
    public class FileSystemCache : IFileSystemCache
    {
        private readonly string _cacheDirectory;
        private readonly ILogger<FileSystemCache> _logger;
        private readonly SemaphoreSlim _cleanupSemaphore = new(1, 1);

        public FileSystemCache(string cacheDirectory, ILogger<FileSystemCache> logger)
        {
            _cacheDirectory = cacheDirectory ?? throw new ArgumentNullException(nameof(cacheDirectory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (!Directory.Exists(_cacheDirectory))
            {
                Directory.CreateDirectory(_cacheDirectory);
            }
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
        {
            try
            {
                var filePath = GetCacheFilePath(key);
                if (!File.Exists(filePath))
                {
                    return null;
                }

                var cacheEntry = await ReadCacheEntryAsync<T>(filePath, cancellationToken);
                if (cacheEntry == null)
                {
                    return null;
                }

                // Check expiration
                if (cacheEntry.ExpiresAt <= DateTime.UtcNow)
                {
                    // Expired, remove file in background
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            File.Delete(filePath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete expired cache file: {FilePath}", filePath);
                        }
                    }, CancellationToken.None);

                    return null;
                }

                return cacheEntry.Value;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading from filesystem cache for key: {Key}", key);
                return null;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default) where T : class
        {
            try
            {
                var filePath = GetCacheFilePath(key);
                var directoryPath = Path.GetDirectoryName(filePath);
                
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath!);
                }

                var cacheEntry = new FileSystemCacheEntry<T>
                {
                    Key = key,
                    Value = value,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.Add(expiration)
                };

                await WriteCacheEntryAsync(filePath, cacheEntry, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error writing to filesystem cache for key: {Key}", key);
            }
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            try
            {
                var filePath = GetCacheFilePath(key);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error removing from filesystem cache for key: {Key}", key);
            }
            return Task.CompletedTask;
        }

        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            await _cleanupSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (Directory.Exists(_cacheDirectory))
                {
                    var files = Directory.GetFiles(_cacheDirectory, "*.cache", SearchOption.AllDirectories);
                    
                    await Task.Run(() =>
                    {
                        foreach (var file in files)
                        {
                            try
                            {
                                File.Delete(file);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to delete cache file during clear: {FilePath}", file);
                            }
                        }
                    }, cancellationToken);
                }

                _logger.LogInformation("Filesystem cache cleared");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing filesystem cache");
            }
            finally
            {
                _cleanupSemaphore.Release();
            }
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            try
            {
                var filePath = GetCacheFilePath(key);
                if (!File.Exists(filePath))
                {
                    return false;
                }

                // Check if the file is expired
                var cacheEntry = await ReadCacheEntryAsync<object>(filePath, cancellationToken);
                return cacheEntry != null && cacheEntry.ExpiresAt > DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking existence in filesystem cache for key: {Key}", key);
                return false;
            }
        }

        private string GetCacheFilePath(string key)
        {
            var hash = ComputeHash(key);
            var subDirectory = hash.Substring(0, 2); // Use first 2 chars for directory structure
            return Path.Combine(_cacheDirectory, subDirectory, $"{hash}.cache");
        }

        private async Task<FileSystemCacheEntry<T>?> ReadCacheEntryAsync<T>(string filePath, CancellationToken cancellationToken) where T : class
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new StreamReader(fileStream, Encoding.UTF8);
            
            var json = await reader.ReadToEndAsync();
            return JsonConvert.DeserializeObject<FileSystemCacheEntry<T>>(json);
        }

        private async Task WriteCacheEntryAsync<T>(string filePath, FileSystemCacheEntry<T> cacheEntry, CancellationToken cancellationToken) where T : class
        {
            var json = JsonConvert.SerializeObject(cacheEntry, Formatting.None);
            
            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(fileStream, Encoding.UTF8);
            
            await writer.WriteAsync(json);
        }

        private static string ComputeHash(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = sha256.ComputeHash(bytes);
            return BitConverter.ToString(hashBytes).Replace("-", "");
        }
    }

    internal class FileSystemCacheEntry<T> where T : class
    {
        public string Key { get; set; } = string.Empty;
        public T? Value { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}