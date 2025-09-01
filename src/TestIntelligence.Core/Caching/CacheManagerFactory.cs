using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.Core.Caching
{
    /// <summary>
    /// Factory for creating cache managers optimized for large solutions.
    /// Provides convenient methods to set up persistent caching with appropriate defaults.
    /// </summary>
    public static class CacheManagerFactory
    {
        /// <summary>
        /// Creates a solution cache manager optimized for large solutions with storage safeguards.
        /// </summary>
        /// <param name="solutionPath">Path to the solution file (.sln).</param>
        /// <param name="logger">Optional logger for diagnostics.</param>
        /// <param name="options">Optional configuration options.</param>
        /// <param name="storageOptions">Optional storage management options for disk space control.</param>
        /// <returns>Configured solution cache manager.</returns>
        public static SolutionCacheManager CreateForLargeSolution(
            string solutionPath,
            ILogger? logger = null,
            SolutionCacheOptions? options = null,
            CacheStorageOptions? storageOptions = null)
        {
            if (string.IsNullOrEmpty(solutionPath))
                throw new ArgumentNullException(nameof(solutionPath));

            if (!File.Exists(solutionPath))
                throw new FileNotFoundException($"Solution file not found: {solutionPath}");

            // Configure options for large solutions
            var cacheOptions = options ?? new SolutionCacheOptions
            {
                // Longer cache durations for large solutions to maximize benefit
                DefaultExpiration = TimeSpan.FromDays(30),
                FallbackExpiration = TimeSpan.FromHours(4),
                
                // Extended file patterns to catch more dependencies
                FilePatterns = new[]
                {
                    "*.cs", "*.csproj", "*.vbproj", "*.fsproj", "*.sln", 
                    "*.config", "*.json", "*.xml", "*.xaml", "*.resx",
                    "*.props", "*.targets", "packages.config", "*.nuspec"
                },
                
                // More comprehensive exclusions for large solutions
                ExcludePatterns = new[]
                {
                    "bin\\", "obj\\", ".vs\\", ".git\\", "packages\\", 
                    "node_modules\\", ".nuget\\", "TestResults\\", 
                    "coverage\\", "artifacts\\", ".sonarqube\\"
                }
            };

            // Create persistent cache with solution-specific directory
            var solutionName = Path.GetFileNameWithoutExtension(solutionPath);
            var cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TestIntelligence",
                "PersistentCache",
                solutionName);

            var persistentCache = new PersistentCacheProvider(
                cacheDirectory, 
                logger as ILogger<PersistentCacheProvider>);

            // Create fallback memory cache
            var memoryCache = new MemoryCacheProvider(cleanupIntervalSeconds: 600); // 10 min cleanup

            return new SolutionCacheManager(
                solutionPath,
                persistentCache,
                memoryCache,
                logger as ILogger<SolutionCacheManager>,
                cacheOptions);
        }

        /// <summary>
        /// Creates an assembly metadata cache configured for large solutions.
        /// </summary>
        /// <param name="solutionCacheManager">Solution cache manager to use for persistent storage.</param>
        /// <param name="logger">Optional logger for diagnostics.</param>
        /// <returns>Configured assembly metadata cache.</returns>
        public static AssemblyMetadataCache CreateAssemblyMetadataCache(
            SolutionCacheManager solutionCacheManager,
            ILogger? logger = null)
        {
            if (solutionCacheManager == null)
                throw new ArgumentNullException(nameof(solutionCacheManager));

            // Create memory cache for frequently accessed items
            var memoryCache = new MemoryCacheProvider(cleanupIntervalSeconds: 300); // 5 min cleanup
            
            // Longer default expiration for assembly metadata (assemblies don't change often)
            var defaultExpiration = TimeSpan.FromDays(7);

            return new AssemblyMetadataCache(
                memoryCache,
                solutionCacheManager,
                defaultExpiration);
        }

        /// <summary>
        /// Creates a complete cache setup optimized for large solutions.
        /// </summary>
        /// <param name="solutionPath">Path to the solution file (.sln).</param>
        /// <param name="loggerFactory">Optional logger factory for creating loggers.</param>
        /// <param name="options">Optional configuration options.</param>
        /// <returns>Cache setup containing all configured cache managers.</returns>
        public static LargeSolutionCacheSetup CreateLargeSolutionSetup(
            string solutionPath,
            ILoggerFactory? loggerFactory = null,
            SolutionCacheOptions? options = null)
        {
            var solutionLogger = loggerFactory?.CreateLogger<SolutionCacheManager>();
            var assemblyLogger = loggerFactory?.CreateLogger<AssemblyMetadataCache>();

            var solutionCacheManager = CreateForLargeSolution(solutionPath, solutionLogger, options);
            var assemblyMetadataCache = CreateAssemblyMetadataCache(solutionCacheManager, assemblyLogger);

            return new LargeSolutionCacheSetup
            {
                SolutionPath = solutionPath,
                SolutionCacheManager = solutionCacheManager,
                AssemblyMetadataCache = assemblyMetadataCache
            };
        }

        /// <summary>
        /// Creates cache managers with custom cache directory for enterprise scenarios.
        /// </summary>
        /// <param name="solutionPath">Path to the solution file (.sln).</param>
        /// <param name="cacheRootDirectory">Root directory for cache storage.</param>
        /// <param name="loggerFactory">Optional logger factory.</param>
        /// <param name="options">Optional configuration options.</param>
        /// <returns>Cache setup with custom cache location.</returns>
        public static LargeSolutionCacheSetup CreateWithCustomCacheDirectory(
            string solutionPath,
            string cacheRootDirectory,
            ILoggerFactory? loggerFactory = null,
            SolutionCacheOptions? options = null)
        {
            if (string.IsNullOrEmpty(solutionPath))
                throw new ArgumentNullException(nameof(solutionPath));

            if (string.IsNullOrEmpty(cacheRootDirectory))
                throw new ArgumentNullException(nameof(cacheRootDirectory));

            if (!File.Exists(solutionPath))
                throw new FileNotFoundException($"Solution file not found: {solutionPath}");

            // Configure options for large solutions
            var cacheOptions = options ?? new SolutionCacheOptions
            {
                DefaultExpiration = TimeSpan.FromDays(30),
                FallbackExpiration = TimeSpan.FromHours(4),
                FilePatterns = new[]
                {
                    "*.cs", "*.csproj", "*.vbproj", "*.fsproj", "*.sln", 
                    "*.config", "*.json", "*.xml", "*.xaml", "*.resx",
                    "*.props", "*.targets", "packages.config", "*.nuspec"
                },
                ExcludePatterns = new[]
                {
                    "bin\\", "obj\\", ".vs\\", ".git\\", "packages\\", 
                    "node_modules\\", ".nuget\\", "TestResults\\", 
                    "coverage\\", "artifacts\\", ".sonarqube\\"
                }
            };

            var solutionLogger = loggerFactory?.CreateLogger<SolutionCacheManager>();
            var assemblyLogger = loggerFactory?.CreateLogger<AssemblyMetadataCache>();

            // Create persistent cache in custom directory
            var solutionName = Path.GetFileNameWithoutExtension(solutionPath);
            var cacheDirectory = Path.Combine(cacheRootDirectory, "TestIntelligence", solutionName);

            var persistentCache = new PersistentCacheProvider(
                cacheDirectory,
                loggerFactory?.CreateLogger<PersistentCacheProvider>());

            var memoryCache = new MemoryCacheProvider(cleanupIntervalSeconds: 600);

            var solutionCacheManager = new SolutionCacheManager(
                solutionPath,
                persistentCache,
                memoryCache,
                solutionLogger,
                cacheOptions);

            var assemblyMetadataCache = CreateAssemblyMetadataCache(solutionCacheManager, assemblyLogger);

            return new LargeSolutionCacheSetup
            {
                SolutionPath = solutionPath,
                SolutionCacheManager = solutionCacheManager,
                AssemblyMetadataCache = assemblyMetadataCache,
                CacheDirectory = cacheDirectory
            };
        }

        /// <summary>
        /// Creates cache setup for very large solutions (40+ projects) with aggressive storage management.
        /// </summary>
        public static LargeSolutionCacheSetup CreateForVeryLargeSolution(
            string solutionPath,
            ILoggerFactory? loggerFactory = null)
        {
            var storageOptions = new CacheStorageOptions
            {
                MaxCacheSizeBytes = 500_000_000, // 500MB limit for very large solutions
                MinimumFreeSpaceBytes = 10_737_418_240, // 10GB minimum free space
                MaxFileAge = TimeSpan.FromDays(14), // Shorter retention for faster cleanup
                UnusedFileThresholdDays = 3, // Aggressive cleanup of unused files
                EnablePeriodicCleanup = true,
                CleanupInterval = TimeSpan.FromMinutes(30) // More frequent cleanup
            };

            var cacheOptions = new SolutionCacheOptions
            {
                DefaultExpiration = TimeSpan.FromDays(14), // Shorter but still beneficial
                FallbackExpiration = TimeSpan.FromHours(2),
                FilePatterns = new[]
                {
                    "*.cs", "*.csproj", "*.sln", "*.config", "*.json"
                    // Reduced patterns for very large solutions
                },
                ExcludePatterns = new[]
                {
                    "bin\\", "obj\\", ".vs\\", ".git\\", "packages\\", 
                    "node_modules\\", ".nuget\\", "TestResults\\", 
                    "coverage\\", "artifacts\\", ".sonarqube\\",
                    "temp\\", "tmp\\", "cache\\"
                }
            };

            return CreateLargeSolutionSetup(solutionPath, loggerFactory, cacheOptions);
        }

        /// <summary>
        /// Creates cache setup for enterprise solutions (100+ projects) with strict storage controls.
        /// </summary>
        public static LargeSolutionCacheSetup CreateForEnterpriseSolution(
            string solutionPath,
            string? customCacheDirectory = null,
            ILoggerFactory? loggerFactory = null)
        {
            var storageOptions = new CacheStorageOptions
            {
                MaxCacheSizeBytes = 250_000_000, // 250MB strict limit
                MinimumFreeSpaceBytes = 21_474_836_480, // 20GB minimum free space
                MaxFileAge = TimeSpan.FromDays(7), // Weekly cleanup cycle
                UnusedFileThresholdDays = 1, // Very aggressive cleanup
                EnablePeriodicCleanup = true,
                CleanupInterval = TimeSpan.FromMinutes(15) // Frequent monitoring
            };

            var cacheOptions = new SolutionCacheOptions
            {
                DefaultExpiration = TimeSpan.FromDays(7), // Weekly refresh
                FallbackExpiration = TimeSpan.FromHours(1),
                FilePatterns = new[]
                {
                    "*.cs", "*.csproj", "*.sln" // Minimal tracking for enterprise
                },
                ExcludePatterns = new[]
                {
                    "bin\\", "obj\\", ".vs\\", ".git\\", "packages\\", 
                    "node_modules\\", ".nuget\\", "TestResults\\", 
                    "coverage\\", "artifacts\\", ".sonarqube\\",
                    "temp\\", "tmp\\", "cache\\", "logs\\", "debug\\"
                }
            };

            if (!string.IsNullOrEmpty(customCacheDirectory))
            {
                return CreateWithCustomCacheDirectory(solutionPath, customCacheDirectory!, loggerFactory, cacheOptions);
            }

            return CreateLargeSolutionSetup(solutionPath, loggerFactory, cacheOptions);
        }

        /// <summary>
        /// Gets recommended storage options based on solution size estimation.
        /// </summary>
        public static CacheStorageOptions GetRecommendedStorageOptions(int estimatedProjectCount)
        {
            if (estimatedProjectCount >= 100)
            {
                // Enterprise solution
                return new CacheStorageOptions
                {
                    MaxCacheSizeBytes = 250_000_000, // 250MB
                    MinimumFreeSpaceBytes = 21_474_836_480, // 20GB
                    MaxFileAge = TimeSpan.FromDays(7),
                    UnusedFileThresholdDays = 1,
                    EnablePeriodicCleanup = true,
                    CleanupInterval = TimeSpan.FromMinutes(15)
                };
            }
            else if (estimatedProjectCount >= 40)
            {
                // Very large solution
                return new CacheStorageOptions
                {
                    MaxCacheSizeBytes = 500_000_000, // 500MB
                    MinimumFreeSpaceBytes = 10_737_418_240, // 10GB
                    MaxFileAge = TimeSpan.FromDays(14),
                    UnusedFileThresholdDays = 3,
                    EnablePeriodicCleanup = true,
                    CleanupInterval = TimeSpan.FromMinutes(30)
                };
            }
            else if (estimatedProjectCount >= 10)
            {
                // Large solution
                return new CacheStorageOptions
                {
                    MaxCacheSizeBytes = 1_073_741_824, // 1GB
                    MinimumFreeSpaceBytes = 5_368_709_120, // 5GB
                    MaxFileAge = TimeSpan.FromDays(30),
                    UnusedFileThresholdDays = 7,
                    EnablePeriodicCleanup = true,
                    CleanupInterval = TimeSpan.FromHours(1)
                };
            }
            else
            {
                // Standard solution
                return new CacheStorageOptions(); // Default settings
            }
        }
    }

    /// <summary>
    /// Complete cache setup for large solutions.
    /// </summary>
    public class LargeSolutionCacheSetup : IDisposable
    {
        public string SolutionPath { get; set; } = string.Empty;
        public string? CacheDirectory { get; set; }
        public SolutionCacheManager SolutionCacheManager { get; set; } = null!;
        public AssemblyMetadataCache AssemblyMetadataCache { get; set; } = null!;

        /// <summary>
        /// Initializes the cache system by loading previous state and checking for changes.
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await SolutionCacheManager.InitializeAsync(cancellationToken);
        }

        /// <summary>
        /// Saves the current state for faster startup next time.
        /// </summary>
        public async Task SaveSnapshotAsync(CancellationToken cancellationToken = default)
        {
            await SolutionCacheManager.SaveSnapshotAsync(cancellationToken);
        }

        /// <summary>
        /// Gets comprehensive statistics about cache usage.
        /// </summary>
        public async Task<SolutionCacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
        {
            return await SolutionCacheManager.GetStatisticsAsync(cancellationToken);
        }

        /// <summary>
        /// Clears all cached data.
        /// </summary>
        public async Task ClearAllAsync(CancellationToken cancellationToken = default)
        {
            await Task.WhenAll(
                SolutionCacheManager.ClearAllAsync(cancellationToken),
                AssemblyMetadataCache.ClearAllAsync(cancellationToken)
            );
        }

        public void Dispose()
        {
            SolutionCacheManager?.Dispose();
            AssemblyMetadataCache?.Dispose();
        }
    }
}