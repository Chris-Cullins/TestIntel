using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Caching;
using TestIntelligence.Core.Discovery;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Models;

namespace TestIntelligence.CLI.Services
{
    /// <summary>
    /// Service for managing cache operations through the CLI.
    /// </summary>
    public class CacheManagementService
    {
        private readonly ILogger<CacheManagementService> _logger;
        private readonly ITestDiscovery _testDiscovery;
        private readonly CrossFrameworkAssemblyLoader _assemblyLoader;

        public CacheManagementService(
            ILogger<CacheManagementService> logger,
            ITestDiscovery testDiscovery,
            CrossFrameworkAssemblyLoader assemblyLoader)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _testDiscovery = testDiscovery ?? throw new ArgumentNullException(nameof(testDiscovery));
            _assemblyLoader = assemblyLoader ?? throw new ArgumentNullException(nameof(assemblyLoader));
        }

        /// <summary>
        /// Handles cache management commands from the CLI.
        /// </summary>
        public async Task HandleCacheCommandAsync(
            string solutionPath,
            string action,
            string? customCacheDirectory,
            string format,
            bool verbose)
        {
            if (!File.Exists(solutionPath))
            {
                Console.WriteLine($"Error: Solution file not found: {solutionPath}");
                return;
            }

            try
            {
                // Detect solution size and use appropriate cache configuration
                var projectCount = EstimateProjectCount(solutionPath);
                
                LargeSolutionCacheSetup cacheSetup;
                if (customCacheDirectory != null)
                {
                    cacheSetup = CacheManagerFactory.CreateWithCustomCacheDirectory(solutionPath, customCacheDirectory);
                }
                else if (projectCount >= 100)
                {
                    cacheSetup = CacheManagerFactory.CreateForEnterpriseSolution(solutionPath);
                    if (verbose) Console.WriteLine($"Detected enterprise solution ({projectCount} projects) - using strict storage limits");
                }
                else if (projectCount >= 40)
                {
                    cacheSetup = CacheManagerFactory.CreateForVeryLargeSolution(solutionPath);
                    if (verbose) Console.WriteLine($"Detected very large solution ({projectCount} projects) - using storage safeguards");
                }
                else
                {
                    cacheSetup = CacheManagerFactory.CreateLargeSolutionSetup(solutionPath);
                    if (verbose) Console.WriteLine($"Detected standard solution ({projectCount} projects) - using default settings");
                }

                using (cacheSetup)
                {
                    switch (action.ToLowerInvariant())
                    {
                        case "status":
                            await HandleStatusCommand(cacheSetup, format, verbose);
                            break;

                        case "clear":
                            await HandleClearCommand(cacheSetup, verbose);
                            break;

                        case "init":
                            await HandleInitCommand(cacheSetup, verbose);
                            break;

                        case "warm-up":
                            await HandleWarmUpCommand(cacheSetup, solutionPath, verbose);
                            break;

                        case "stats":
                            await HandleStatsCommand(cacheSetup, format, verbose);
                            break;

                        default:
                            Console.WriteLine($"Error: Unknown cache action: {action}");
                            Console.WriteLine("Available actions: status, clear, init, warm-up, stats");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling cache command");
                Console.WriteLine($"Error: {ex.Message}");
                
                if (verbose)
                {
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        private async Task HandleStatusCommand(LargeSolutionCacheSetup cacheSetup, string format, bool verbose)
        {
            await cacheSetup.InitializeAsync();
            var stats = await cacheSetup.GetStatisticsAsync();
            var changes = await cacheSetup.SolutionCacheManager.DetectChangesAsync();

            if (format == "json")
            {
                var statusData = new
                {
                    solutionPath = stats.SolutionPath,
                    lastSnapshot = stats.LastSnapshotTime,
                    trackedFiles = stats.TrackedFileCount,
                    dependencyMappings = stats.DependencyMappingCount,
                    persistentCache = new
                    {
                        totalFiles = stats.PersistentCache.TotalFiles,
                        activeFiles = stats.PersistentCache.ActiveFiles,
                        expiredFiles = stats.PersistentCache.ExpiredFiles,
                        totalSize = stats.PersistentCache.TotalSizeBytes,
                        totalSizeFormatted = stats.PersistentCache.TotalSizeFormatted
                    },
                    changes = new
                    {
                        hasChanges = changes.HasChanges,
                        reason = changes.Reason,
                        modifiedFiles = changes.ModifiedFiles.Count,
                        addedFiles = changes.AddedFiles.Count,
                        deletedFiles = changes.DeletedFiles.Count
                    },
                    cacheDirectory = cacheSetup.CacheDirectory
                };

                Console.WriteLine(JsonSerializer.Serialize(statusData, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine("Cache Status Report");
                Console.WriteLine("===================");
                Console.WriteLine($"Solution: {stats.SolutionPath}");
                Console.WriteLine($"Cache Directory: {cacheSetup.CacheDirectory}");
                Console.WriteLine();

                Console.WriteLine("Cache State:");
                Console.WriteLine($"  Last Snapshot: {stats.LastSnapshotTime?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "None"}");
                Console.WriteLine($"  Tracked Files: {stats.TrackedFileCount:N0}");
                Console.WriteLine($"  Dependency Mappings: {stats.DependencyMappingCount:N0}");
                Console.WriteLine();

                Console.WriteLine("Persistent Cache:");
                Console.WriteLine($"  Total Files: {stats.PersistentCache.TotalFiles:N0}");
                Console.WriteLine($"  Active Files: {stats.PersistentCache.ActiveFiles:N0}");
                Console.WriteLine($"  Expired Files: {stats.PersistentCache.ExpiredFiles:N0}");
                Console.WriteLine($"  Total Size: {stats.PersistentCache.TotalSizeFormatted}");
                Console.WriteLine();

                Console.WriteLine("Change Detection:");
                if (changes.HasChanges)
                {
                    Console.WriteLine($"  Status: Changes detected - {changes.Reason}");
                    Console.WriteLine($"  Modified Files: {changes.ModifiedFiles.Count}");
                    Console.WriteLine($"  Added Files: {changes.AddedFiles.Count}");
                    Console.WriteLine($"  Deleted Files: {changes.DeletedFiles.Count}");
                    
                    if (verbose && changes.ModifiedFiles.Count > 0)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Modified Files:");
                        foreach (var file in changes.ModifiedFiles.Take(10))
                        {
                            Console.WriteLine($"    {file}");
                        }
                        if (changes.ModifiedFiles.Count > 10)
                        {
                            Console.WriteLine($"    ... and {changes.ModifiedFiles.Count - 10} more");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("  Status: No changes detected since last snapshot");
                }
            }
        }

        private async Task HandleClearCommand(LargeSolutionCacheSetup cacheSetup, bool verbose)
        {
            if (verbose)
            {
                Console.WriteLine("Clearing all cached data...");
            }

            await cacheSetup.ClearAllAsync();
            Console.WriteLine("Cache cleared successfully");
        }

        private async Task HandleInitCommand(LargeSolutionCacheSetup cacheSetup, bool verbose)
        {
            if (verbose)
            {
                Console.WriteLine("Initializing cache system...");
            }

            await cacheSetup.InitializeAsync();
            
            if (verbose)
            {
                Console.WriteLine("Creating initial snapshot...");
            }

            await cacheSetup.SaveSnapshotAsync();
            Console.WriteLine("Cache initialized successfully");
        }

        private async Task HandleWarmUpCommand(LargeSolutionCacheSetup cacheSetup, string solutionPath, bool verbose)
        {
            if (verbose)
            {
                Console.WriteLine("Warming up cache by analyzing solution...");
            }

            await cacheSetup.InitializeAsync();

            // Find all assemblies in the solution
            var solutionDirectory = Path.GetDirectoryName(solutionPath);
            if (string.IsNullOrEmpty(solutionDirectory))
            {
                Console.WriteLine("Error: Could not determine solution directory");
                return;
            }

            var assemblies = Directory.GetFiles(solutionDirectory, "*.dll", SearchOption.AllDirectories)
                .Where(f => !f.Contains("\\bin\\Debug\\") && !f.Contains("\\bin\\Release\\") && 
                           !f.Contains("\\obj\\") && !f.Contains("\\packages\\"))
                .ToArray();

            if (verbose)
            {
                Console.WriteLine($"Found {assemblies.Length} assemblies to analyze");
            }

            var processedCount = 0;
            var cachedCount = 0;

            foreach (var assemblyPath in assemblies)
            {
                try
                {
                    if (verbose)
                    {
                        Console.WriteLine($"Processing: {Path.GetFileName(assemblyPath)}");
                    }

                    // Use assembly metadata cache to load and discover tests
                    await cacheSetup.AssemblyMetadataCache.GetOrCacheTestDiscoveryAsync(
                        assemblyPath,
                        async () =>
                        {
                            var loadResult = await _assemblyLoader.LoadAssemblyAsync(assemblyPath);
                            if (loadResult.IsSuccess && loadResult.Assembly != null)
                            {
                                var discoveryResult = await _testDiscovery.DiscoverTestsAsync(loadResult.Assembly);
                                cachedCount++;
                                return discoveryResult;
                            }

                            return new TestDiscoveryResult(
                                assemblyPath,
                                FrameworkVersion.Unknown,
                                new List<TestFixture>(),
                                new List<string> { loadResult.IsSuccess ? "Unknown error" : "Failed to load assembly" });
                        });

                    processedCount++;
                }
                catch (Exception ex)
                {
                    if (verbose)
                    {
                        Console.WriteLine($"  Warning: Failed to process {assemblyPath}: {ex.Message}");
                    }
                    _logger.LogWarning(ex, "Failed to process assembly: {AssemblyPath}", assemblyPath);
                }
            }

            await cacheSetup.SaveSnapshotAsync();

            Console.WriteLine($"Cache warm-up completed: {processedCount} assemblies processed, {cachedCount} cached");
        }

        private async Task HandleStatsCommand(LargeSolutionCacheSetup cacheSetup, string format, bool verbose)
        {
            await cacheSetup.InitializeAsync();
            var stats = await cacheSetup.GetStatisticsAsync();

            if (format == "json")
            {
                var statsData = new
                {
                    solutionPath = stats.SolutionPath,
                    lastSnapshot = stats.LastSnapshotTime,
                    trackedFiles = stats.TrackedFileCount,
                    dependencyMappings = stats.DependencyMappingCount,
                    persistentCache = new
                    {
                        totalFiles = stats.PersistentCache.TotalFiles,
                        activeFiles = stats.PersistentCache.ActiveFiles,
                        expiredFiles = stats.PersistentCache.ExpiredFiles,
                        totalSizeBytes = stats.PersistentCache.TotalSizeBytes,
                        totalSizeFormatted = stats.PersistentCache.TotalSizeFormatted
                    },
                    cacheDirectory = cacheSetup.CacheDirectory
                };

                Console.WriteLine(JsonSerializer.Serialize(statsData, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine("Cache Statistics");
                Console.WriteLine("================");
                Console.WriteLine($"Solution: {stats.SolutionPath}");
                Console.WriteLine($"Cache Directory: {cacheSetup.CacheDirectory}");
                Console.WriteLine();

                Console.WriteLine($"Tracked Files: {stats.TrackedFileCount:N0}");
                Console.WriteLine($"Dependency Mappings: {stats.DependencyMappingCount:N0}");
                Console.WriteLine();

                Console.WriteLine("Persistent Cache Files:");
                Console.WriteLine($"  Total: {stats.PersistentCache.TotalFiles:N0}");
                Console.WriteLine($"  Active: {stats.PersistentCache.ActiveFiles:N0}");
                Console.WriteLine($"  Expired: {stats.PersistentCache.ExpiredFiles:N0}");
                Console.WriteLine($"  Total Size: {stats.PersistentCache.TotalSizeFormatted}");
                
                if (stats.LastSnapshotTime.HasValue)
                {
                    Console.WriteLine();
                    Console.WriteLine($"Last Snapshot: {stats.LastSnapshotTime:yyyy-MM-dd HH:mm:ss UTC}");
                    var age = DateTimeOffset.UtcNow - stats.LastSnapshotTime.Value;
                    Console.WriteLine($"Snapshot Age: {FormatTimeSpan(age)}");
                }
            }
        }

        private static string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
                return $"{timeSpan.TotalDays:F1} days";
            if (timeSpan.TotalHours >= 1)
                return $"{timeSpan.TotalHours:F1} hours";
            if (timeSpan.TotalMinutes >= 1)
                return $"{timeSpan.TotalMinutes:F1} minutes";
            return $"{timeSpan.TotalSeconds:F1} seconds";
        }

        private int EstimateProjectCount(string solutionPath)
        {
            try
            {
                var solutionContent = File.ReadAllText(solutionPath);
                
                // Count project references in solution file
                var projectLines = solutionContent
                    .Split('\n')
                    .Count(line => line.TrimStart().StartsWith("Project(", StringComparison.OrdinalIgnoreCase) &&
                                  (line.Contains(".csproj") || line.Contains(".vbproj") || line.Contains(".fsproj")));

                _logger.LogDebug("Estimated {ProjectCount} projects from solution file", projectLines);
                return projectLines;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to estimate project count, assuming standard solution");
                return 10; // Default assumption
            }
        }
    }
}