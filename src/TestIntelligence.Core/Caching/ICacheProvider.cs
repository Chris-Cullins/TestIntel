using System;
using System.Threading;
using System.Threading.Tasks;

namespace TestIntelligence.Core.Caching
{
    /// <summary>
    /// Defines the contract for a cache provider.
    /// </summary>
    public interface ICacheProvider : IDisposable
    {
        /// <summary>
        /// Gets a value from the cache.
        /// </summary>
        /// <typeparam name="T">Type of the cached value.</typeparam>
        /// <param name="key">Cache key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The cached value, or null if not found.</returns>
        Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Sets a value in the cache.
        /// </summary>
        /// <typeparam name="T">Type of the value to cache.</typeparam>
        /// <param name="key">Cache key.</param>
        /// <param name="value">Value to cache.</param>
        /// <param name="expiration">Cache expiration time.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Removes a value from the cache.
        /// </summary>
        /// <param name="key">Cache key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the key was found and removed.</returns>
        Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a key exists in the cache.
        /// </summary>
        /// <param name="key">Cache key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the key exists.</returns>
        Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Clears all entries from the cache.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task ClearAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets or sets a value in the cache, with a factory function to create the value if it doesn't exist.
        /// </summary>
        /// <typeparam name="T">Type of the cached value.</typeparam>
        /// <param name="key">Cache key.</param>
        /// <param name="factory">Factory function to create the value if not cached.</param>
        /// <param name="expiration">Cache expiration time.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The cached or newly created value.</returns>
        Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class;
    }
}