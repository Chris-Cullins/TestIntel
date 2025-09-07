namespace TestIntelligence.Core.Services
{
    /// <summary>
    /// Service for managing test coverage analysis caches.
    /// Handles cache invalidation and cleanup for test coverage data.
    /// </summary>
    public interface ITestCoverageCacheManager
    {
        /// <summary>
        /// Clears all cached data (call graphs and path calculations).
        /// Call this when source files or solution structure changes.
        /// </summary>
        void ClearCaches();
    }
}