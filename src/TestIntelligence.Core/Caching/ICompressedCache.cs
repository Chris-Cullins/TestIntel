using System;
using System.Threading;
using System.Threading.Tasks;

namespace TestIntelligence.Core.Caching
{
    /// <summary>
    /// Defines a compressed cache interface with storage statistics and management capabilities.
    /// </summary>
    public interface ICompressedCache<T> : IDisposable where T : class
    {
        /// <summary>
        /// Gets a value from the cache with decompression.
        /// </summary>
        /// <param name="key">Cache key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The cached value, or null if not found.</returns>
        Task<T?> GetAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets a value in the cache with compression.
        /// </summary>
        /// <param name="key">Cache key.</param>
        /// <param name="value">Value to cache.</param>
        /// <param name="expiration">Cache expiration time.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task SetAsync(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a value from the cache.
        /// </summary>
        /// <param name="key">Cache key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the key was found and removed.</returns>
        Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the compressed size of a cached entry.
        /// </summary>
        /// <param name="key">Cache key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Size in bytes, or null if not found.</returns>
        Task<long?> GetCompressedSizeAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets compression and storage statistics.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Cache statistics including compression ratios and storage usage.</returns>
        Task<CacheCompressionStats> GetStatsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs cache maintenance including cleanup of expired entries and LRU eviction.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task PerformMaintenanceAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Clears all entries from the cache.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task ClearAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets or sets a value in the cache with compression, using a factory function to create the value if it doesn't exist.
        /// </summary>
        /// <param name="key">Cache key.</param>
        /// <param name="factory">Factory function to create the value if not cached.</param>
        /// <param name="expiration">Cache expiration time.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The cached or newly created value.</returns>
        Task<T> GetOrSetAsync(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Compression and storage statistics for cached data.
    /// </summary>
    public class CacheCompressionStats
    {
        public int TotalEntries { get; set; }
        public long TotalCompressedSize { get; set; }
        public long TotalUncompressedSize { get; set; }
        public long AverageCompressionRatio => TotalUncompressedSize > 0 
            ? (long)((1.0 - (double)TotalCompressedSize / TotalUncompressedSize) * 100) 
            : 0;
        public int HitCount { get; set; }
        public int MissCount { get; set; }
        public double HitRatio => (HitCount + MissCount) > 0 
            ? (double)HitCount / (HitCount + MissCount) 
            : 0;
        public int EvictionCount { get; set; }
        public DateTime LastMaintenanceRun { get; set; }
        
        public string TotalCompressedSizeFormatted => FormatBytes(TotalCompressedSize);
        public string TotalUncompressedSizeFormatted => FormatBytes(TotalUncompressedSize);
        
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