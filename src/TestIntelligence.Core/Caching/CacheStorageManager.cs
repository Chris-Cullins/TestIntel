using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.Core.Caching
{
    /// <summary>
    /// Manages cache storage with disk space monitoring and automatic cleanup policies.
    /// Prevents cache from consuming excessive disk space on very large solutions.
    /// </summary>
    public class CacheStorageManager : IDisposable
    {
        private readonly string _cacheRootDirectory;
        private readonly ILogger<CacheStorageManager>? _logger;
        private readonly CacheStorageOptions _options;
        private readonly Timer? _monitoringTimer;
        private volatile bool _disposed = false;

        public CacheStorageManager(
            string cacheRootDirectory, 
            CacheStorageOptions? options = null,
            ILogger<CacheStorageManager>? logger = null)
        {
            _cacheRootDirectory = cacheRootDirectory ?? throw new ArgumentNullException(nameof(cacheRootDirectory));
            _options = options ?? new CacheStorageOptions();
            _logger = logger;

            // Start periodic monitoring if enabled
            if (_options.EnablePeriodicCleanup)
            {
                _monitoringTimer = new Timer(PeriodicCleanup, null, _options.CleanupInterval, _options.CleanupInterval);
            }
        }

        /// <summary>
        /// Checks if there's sufficient disk space before allowing cache operations.
        /// </summary>
        public bool HasSufficientDiskSpace(long requiredBytes = 0)
        {
            try
            {
                var driveInfo = new DriveInfo(Path.GetPathRoot(_cacheRootDirectory) ?? _cacheRootDirectory);
                var availableSpace = driveInfo.AvailableFreeSpace;
                var totalNeeded = requiredBytes + _options.MinimumFreeSpaceBytes;

                _logger?.LogDebug("Disk space check: Available={0:N0} bytes, Required={1:N0} bytes", 
                    availableSpace, totalNeeded);

                return availableSpace > totalNeeded;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Unable to check disk space, allowing cache operation");
                return true; // Allow operation if we can't check
            }
        }

        /// <summary>
        /// Gets current cache storage statistics.
        /// </summary>
        public Task<CacheStorageStatistics> GetStorageStatisticsAsync(CancellationToken cancellationToken = default)
        {
            var stats = new CacheStorageStatistics
            {
                CacheRootDirectory = _cacheRootDirectory,
                MaxCacheSizeBytes = _options.MaxCacheSizeBytes,
                LastCleanupTime = GetLastCleanupTime()
            };

            if (!Directory.Exists(_cacheRootDirectory))
            {
                return Task.FromResult(stats);
            }

            var allFiles = Directory.GetFiles(_cacheRootDirectory, "*", SearchOption.AllDirectories);
            var now = DateTime.UtcNow;

            foreach (var file in allFiles)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    stats.TotalFiles++;
                    stats.TotalSizeBytes += fileInfo.Length;

                    var age = now - fileInfo.CreationTimeUtc;
                    if (age > _options.MaxFileAge)
                    {
                        stats.ExpiredFiles++;
                        stats.ExpiredSizeBytes += fileInfo.Length;
                    }

                    if (fileInfo.LastAccessTimeUtc < now.AddDays(-_options.UnusedFileThresholdDays))
                    {
                        stats.UnusedFiles++;
                        stats.UnusedSizeBytes += fileInfo.Length;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Error analyzing file: {FilePath}", file);
                }
            }

            // Check if cache is approaching limits
            stats.IsNearSizeLimit = stats.TotalSizeBytes > (_options.MaxCacheSizeBytes * 0.8);
            stats.NeedsCleanup = stats.ExpiredFiles > 0 || stats.UnusedFiles > 10 || stats.IsNearSizeLimit;

            return Task.FromResult(stats);
        }

        /// <summary>
        /// Performs cleanup of old, unused, or excessive cache files.
        /// </summary>
        public async Task<CacheCleanupResult> PerformCleanupAsync(CancellationToken cancellationToken = default)
        {
            var result = new CacheCleanupResult();

            if (!Directory.Exists(_cacheRootDirectory))
            {
                return result;
            }

            _logger?.LogInformation("Starting cache cleanup in: {Directory}", _cacheRootDirectory);

            var allFiles = Directory.GetFiles(_cacheRootDirectory, "*", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .Where(f => f.Exists)
                .ToList();

            var now = DateTime.UtcNow;
            var filesToDelete = new List<FileInfo>();

            // 1. Delete expired files (based on age)
            var expiredFiles = allFiles.Where(f => now - f.CreationTimeUtc > _options.MaxFileAge).ToList();
            filesToDelete.AddRange(expiredFiles);
            result.ExpiredFilesDeleted = expiredFiles.Count;
            result.ExpiredBytesDeleted = expiredFiles.Sum(f => f.Length);

            // 2. Delete unused files (not accessed recently)
            var unusedFiles = allFiles
                .Where(f => f.LastAccessTimeUtc < now.AddDays(-_options.UnusedFileThresholdDays))
                .Where(f => !filesToDelete.Contains(f))
                .ToList();
            filesToDelete.AddRange(unusedFiles);
            result.UnusedFilesDeleted = unusedFiles.Count;
            result.UnusedBytesDeleted = unusedFiles.Sum(f => f.Length);

            // 3. If still too large, delete oldest files
            var currentSize = allFiles.Sum(f => f.Length);
            if (currentSize > _options.MaxCacheSizeBytes)
            {
                var remainingFiles = allFiles.Except(filesToDelete)
                    .OrderBy(f => f.LastAccessTimeUtc)
                    .ToList();

                var targetSize = _options.MaxCacheSizeBytes * 0.7; // Clean to 70% of max
                var currentSizeAfterCleanup = currentSize - filesToDelete.Sum(f => f.Length);

                foreach (var file in remainingFiles)
                {
                    if (currentSizeAfterCleanup <= targetSize) break;

                    filesToDelete.Add(file);
                    currentSizeAfterCleanup -= file.Length;
                    result.OversizeFilesDeleted++;
                    result.OversizeBytesDeleted += file.Length;
                }
            }

            // Actually delete the files
            foreach (var file in filesToDelete)
            {
                try
                {
                    file.Delete();
                    result.TotalFilesDeleted++;
                    result.TotalBytesDeleted += file.Length;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to delete cache file: {FilePath}", file.FullName);
                    result.FailedDeletions++;
                }
            }

            // Clean up empty directories
            await CleanupEmptyDirectoriesAsync(_cacheRootDirectory);

            // Update last cleanup time
            SetLastCleanupTime();

            result.CompletedAt = DateTime.UtcNow;
            _logger?.LogInformation("Cache cleanup completed: {FilesDeleted} files ({SizeDeleted:N0} bytes) deleted",
                result.TotalFilesDeleted, result.TotalBytesDeleted);

            return result;
        }

        /// <summary>
        /// Validates that a cache operation won't exceed storage limits.
        /// </summary>
        public async Task<bool> ValidateCacheOperationAsync(long estimatedSizeBytes, CancellationToken cancellationToken = default)
        {
            // Check disk space
            if (!HasSufficientDiskSpace(estimatedSizeBytes))
            {
                _logger?.LogWarning("Insufficient disk space for cache operation requiring {SizeBytes:N0} bytes", estimatedSizeBytes);
                return false;
            }

            // Check cache size limits
            var stats = await GetStorageStatisticsAsync(cancellationToken);
            if (stats.TotalSizeBytes + estimatedSizeBytes > _options.MaxCacheSizeBytes)
            {
                _logger?.LogInformation("Cache operation would exceed size limit, triggering cleanup");
                
                // Try cleanup first
                var cleanupResult = await PerformCleanupAsync(cancellationToken);
                
                // Recheck after cleanup
                var newStats = await GetStorageStatisticsAsync(cancellationToken);
                if (newStats.TotalSizeBytes + estimatedSizeBytes > _options.MaxCacheSizeBytes)
                {
                    _logger?.LogWarning("Cache operation would still exceed size limit even after cleanup");
                    return false;
                }
            }

            return true;
        }

        private Task CleanupEmptyDirectoriesAsync(string rootPath)
        {
            try
            {
                var directories = Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length) // Start with deepest directories
                    .ToList();

                foreach (var directory in directories)
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(directory).Any())
                        {
                            Directory.Delete(directory);
                            _logger?.LogDebug("Deleted empty directory: {Directory}", directory);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "Failed to delete empty directory: {Directory}", directory);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error during empty directory cleanup");
            }

            return Task.CompletedTask;
        }

        private void PeriodicCleanup(object? state)
        {
            try
            {
                _ = Task.Run(async () =>
                {
                    var stats = await GetStorageStatisticsAsync();
                    if (stats.NeedsCleanup)
                    {
                        await PerformCleanupAsync();
                    }
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during periodic cache cleanup");
            }
        }

        private DateTime? GetLastCleanupTime()
        {
            var markerFile = Path.Combine(_cacheRootDirectory, ".last-cleanup");
            if (File.Exists(markerFile))
            {
                try
                {
                    var content = File.ReadAllText(markerFile);
                    if (DateTime.TryParse(content, out var time))
                    {
                        return time;
                    }
                }
                catch { }
            }
            return null;
        }

        private void SetLastCleanupTime()
        {
            var markerFile = Path.Combine(_cacheRootDirectory, ".last-cleanup");
            try
            {
                Directory.CreateDirectory(_cacheRootDirectory);
                File.WriteAllText(markerFile, DateTime.UtcNow.ToString("O"));
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to update cleanup marker file");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _monitoringTimer?.Dispose();
        }
    }

    /// <summary>
    /// Configuration options for cache storage management.
    /// </summary>
    public class CacheStorageOptions
    {
        /// <summary>
        /// Maximum total cache size in bytes. Default: 1GB.
        /// </summary>
        public long MaxCacheSizeBytes { get; set; } = 1_073_741_824; // 1GB

        /// <summary>
        /// Minimum free disk space to maintain in bytes. Default: 5GB.
        /// </summary>
        public long MinimumFreeSpaceBytes { get; set; } = 5_368_709_120; // 5GB

        /// <summary>
        /// Maximum age for cache files before they're considered expired. Default: 30 days.
        /// </summary>
        public TimeSpan MaxFileAge { get; set; } = TimeSpan.FromDays(30);

        /// <summary>
        /// Number of days without access before a file is considered unused. Default: 7 days.
        /// </summary>
        public int UnusedFileThresholdDays { get; set; } = 7;

        /// <summary>
        /// Whether to enable periodic automated cleanup. Default: true.
        /// </summary>
        public bool EnablePeriodicCleanup { get; set; } = true;

        /// <summary>
        /// Interval between cleanup checks. Default: 1 hour.
        /// </summary>
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
    }

    /// <summary>
    /// Statistics about cache storage usage.
    /// </summary>
    public class CacheStorageStatistics
    {
        public string CacheRootDirectory { get; set; } = string.Empty;
        public int TotalFiles { get; set; }
        public long TotalSizeBytes { get; set; }
        public int ExpiredFiles { get; set; }
        public long ExpiredSizeBytes { get; set; }
        public int UnusedFiles { get; set; }
        public long UnusedSizeBytes { get; set; }
        public long MaxCacheSizeBytes { get; set; }
        public bool IsNearSizeLimit { get; set; }
        public bool NeedsCleanup { get; set; }
        public DateTime? LastCleanupTime { get; set; }

        public string TotalSizeFormatted => FormatBytes(TotalSizeBytes);
        public string MaxSizeFormatted => FormatBytes(MaxCacheSizeBytes);
        public double SizeUtilizationPercent => MaxCacheSizeBytes > 0 ? (double)TotalSizeBytes / MaxCacheSizeBytes * 100 : 0;

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
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

    /// <summary>
    /// Results from a cache cleanup operation.
    /// </summary>
    public class CacheCleanupResult
    {
        public int ExpiredFilesDeleted { get; set; }
        public long ExpiredBytesDeleted { get; set; }
        public int UnusedFilesDeleted { get; set; }
        public long UnusedBytesDeleted { get; set; }
        public int OversizeFilesDeleted { get; set; }
        public long OversizeBytesDeleted { get; set; }
        public int TotalFilesDeleted { get; set; }
        public long TotalBytesDeleted { get; set; }
        public int FailedDeletions { get; set; }
        public DateTime CompletedAt { get; set; }

        public string TotalDeletedFormatted => FormatBytes(TotalBytesDeleted);

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
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