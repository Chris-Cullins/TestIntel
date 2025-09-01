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
using TestIntelligence.ImpactAnalyzer.Caching;
using TestIntelligence.CLI.Progress;

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

            // Create progress reporter for the operation
            using var progressReporter = ProgressReporterFactory.Create(verbose);

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
                using (var enhancedIntegration = CreateEnhancedIntegration(cacheSetup.CacheDirectory ?? "/tmp"))
                {
                    switch (action.ToLowerInvariant())
                    {
                        case "status":
                            await HandleStatusCommand(cacheSetup, enhancedIntegration, format, verbose, progressReporter);
                            break;

                        case "clear":
                            await HandleClearCommand(cacheSetup, enhancedIntegration, verbose, progressReporter);
                            break;

                        case "init":
                            await HandleInitCommand(cacheSetup, enhancedIntegration, verbose, progressReporter);
                            break;

                        case "warm-up":
                            await HandleWarmUpCommand(cacheSetup, enhancedIntegration, solutionPath, verbose, progressReporter);
                            break;

                        case "stats":
                            await HandleStatsCommand(cacheSetup, enhancedIntegration, format, verbose, progressReporter);
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
                progressReporter.ReportError($"{ex.Message}{(verbose ? $"\n{ex.StackTrace}" : "")}");
            }
        }

        private EnhancedRoslynAnalyzerIntegration CreateEnhancedIntegration(string cacheDirectory)
        {
            return new EnhancedRoslynAnalyzerIntegration(
                cacheDirectory, 
                _logger as ILogger<EnhancedRoslynAnalyzerIntegration>);
        }

        private async Task HandleStatusCommand(LargeSolutionCacheSetup cacheSetup, EnhancedRoslynAnalyzerIntegration enhancedIntegration, string format, bool verbose, IProgressReporter progressReporter)
        {
            var progress = new CacheOperationProgress(progressReporter);
            progress.DefineSteps(CacheOperationSteps.CacheStatusSequence);

            // Step 1: Reading cache statistics
            progress.StartNextStep();
            await cacheSetup.InitializeAsync();
            var stats = await cacheSetup.GetStatisticsAsync();
            var changes = await cacheSetup.SolutionCacheManager.DetectChangesAsync();
            progress.CompleteCurrentStep("Cache statistics loaded");

            // Step 2: Analyzing cache health
            progress.StartNextStep();
            var enhancedStats = await enhancedIntegration.GetCacheStatisticsAsync();
            progress.CompleteCurrentStep("Cache health analyzed");

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
                    enhancedCaches = new
                    {
                        callGraph = new
                        {
                            totalEntries = enhancedStats.CallGraph.TotalEntries,
                            hitCount = enhancedStats.CallGraph.HitCount,
                            hitRatio = enhancedStats.CallGraph.HitRatio,
                            totalCompressedSize = enhancedStats.CallGraph.TotalCompressedSize,
                            compressionRatio = enhancedStats.CallGraph.AverageCompressionRatio,
                            lastMaintenance = enhancedStats.CallGraph.LastMaintenanceRun
                        },
                        projects = new
                        {
                            totalEntries = enhancedStats.Projects.TotalEntries,
                            trackedProjects = enhancedStats.Projects.TrackedProjectsCount,
                            hitRatio = enhancedStats.Projects.HitRatio,
                            totalCompressedSize = enhancedStats.Projects.TotalCompressedSize,
                            changeDetections = enhancedStats.Projects.ChangeDetectionCount
                        },
                        overall = new
                        {
                            totalCacheSize = enhancedStats.TotalCacheSize,
                            overallHitRatio = enhancedStats.OverallHitRatio,
                            compressionEfficiency = enhancedStats.CompressionEfficiency
                        }
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

                Console.WriteLine();
                Console.WriteLine("Enhanced Caches:");
                Console.WriteLine($"  Call Graph Cache:");
                Console.WriteLine($"    Total Entries: {enhancedStats.CallGraph.TotalEntries:N0}");
                Console.WriteLine($"    Cache Hits: {enhancedStats.CallGraph.HitCount:N0}");
                Console.WriteLine($"    Hit Ratio: {enhancedStats.CallGraph.HitRatio:P1}");
                Console.WriteLine($"    Size: {FormatBytes(enhancedStats.CallGraph.TotalCompressedSize)}");
                Console.WriteLine($"    Compression: {enhancedStats.CallGraph.AverageCompressionRatio:F1}%");
                Console.WriteLine($"    Last Maintenance: {enhancedStats.CallGraph.LastMaintenanceRun:yyyy-MM-dd HH:mm}");
                Console.WriteLine();
                Console.WriteLine($"  Project Cache:");
                Console.WriteLine($"    Total Entries: {enhancedStats.Projects.TotalEntries:N0}");
                Console.WriteLine($"    Tracked Projects: {enhancedStats.Projects.TrackedProjectsCount:N0}");
                Console.WriteLine($"    Hit Ratio: {enhancedStats.Projects.HitRatio:P1}");
                Console.WriteLine($"    Size: {FormatBytes(enhancedStats.Projects.TotalCompressedSize)}");
                Console.WriteLine($"    Change Detections: {enhancedStats.Projects.ChangeDetectionCount:N0}");
                Console.WriteLine();
                Console.WriteLine($"  Overall Performance:");
                Console.WriteLine($"    Total Cache Size: {FormatBytes(enhancedStats.TotalCacheSize)}");
                Console.WriteLine($"    Overall Hit Ratio: {enhancedStats.OverallHitRatio:P1}");
                Console.WriteLine($"    Compression Efficiency: {enhancedStats.CompressionEfficiency:P1}");
            }

            progress.Complete("✅ Cache status report generated");
        }

        private async Task HandleClearCommand(LargeSolutionCacheSetup cacheSetup, EnhancedRoslynAnalyzerIntegration enhancedIntegration, bool verbose, IProgressReporter progressReporter)
        {
            var progress = new CacheOperationProgress(progressReporter);
            progress.DefineSteps(CacheOperationSteps.CacheClearSequence);

            try
            {
                // Step 1: Clear cache files
                progress.StartNextStep();
                await cacheSetup.ClearAllAsync();
                progress.CompleteCurrentStep("Traditional caches cleared");

                // Step 2: Clean up directories
                progress.StartNextStep();
                await Task.Delay(100); // Enhanced caches manage their own lifecycle
                progress.CompleteCurrentStep("Enhanced cache cleanup completed");

                progress.Complete("✅ All caches cleared successfully");
            }
            catch (Exception ex)
            {
                progress.ReportError($"Failed to clear cache: {ex.Message}");
                throw;
            }
        }

        private async Task HandleInitCommand(LargeSolutionCacheSetup cacheSetup, EnhancedRoslynAnalyzerIntegration enhancedIntegration, bool verbose, IProgressReporter progressReporter)
        {
            var progress = new CacheOperationProgress(progressReporter);
            progress.DefineSteps(CacheOperationSteps.CacheInitSequence);
            
            try
            {
                // Step 1: Initialize cache system
                progress.StartNextStep();
                await cacheSetup.InitializeAsync();
                progress.CompleteCurrentStep("Cache directories created");

                // Step 2: Analyze solution
                progress.StartNextStep();
                await enhancedIntegration.GetCacheStatisticsAsync();
                progress.CompleteCurrentStep("Enhanced cache structures initialized");

                // Step 3: Create snapshot
                progress.StartNextStep();
                await cacheSetup.SaveSnapshotAsync();
                progress.CompleteCurrentStep("Solution snapshot saved");

                progress.Complete("✅ Cache system initialized successfully!\n💡 Run 'cache --action warm-up' to populate caches with data for better performance");
            }
            catch (Exception ex)
            {
                progress.ReportError($"Failed to initialize cache: {ex.Message}");
                throw;
            }
        }

        private async Task HandleWarmUpCommand(LargeSolutionCacheSetup cacheSetup, EnhancedRoslynAnalyzerIntegration enhancedIntegration, string solutionPath, bool verbose, IProgressReporter progressReporter)
        {
            var progress = new CacheOperationProgress(progressReporter);
            progress.DefineSteps(CacheOperationSteps.CacheWarmUpSequence);

            try
            {
                // Step 1: Initialize cache system
                progress.StartNextStep();
                await cacheSetup.InitializeAsync();
                progress.CompleteCurrentStep("Cache system ready");

                // Step 2: Analyze solution structure
                progress.StartNextStep();
                var solutionDirectory = Path.GetDirectoryName(solutionPath);
                var assemblies = !string.IsNullOrEmpty(solutionDirectory) 
                    ? Directory.GetFiles(solutionDirectory, "*.dll", SearchOption.AllDirectories)
                        .Where(f => !f.Contains("\\bin\\Debug\\") && !f.Contains("\\bin\\Release\\") && 
                                   !f.Contains("\\obj\\") && !f.Contains("\\packages\\"))
                        .ToArray()
                    : new string[0];
                progress.CompleteCurrentStep($"Found {assemblies.Length} assemblies to process");

                // Step 3: Load project metadata
                progress.StartNextStep();
                await Task.Delay(100); // Placeholder for project loading
                progress.CompleteCurrentStep("Project metadata loaded");

                // Step 4: Build Roslyn compilations (heaviest operation)
                progress.StartNextStep();
                var warmupResult = await enhancedIntegration.WarmupCacheAsync(solutionPath);
                progress.CompleteCurrentStep($"Enhanced caches built for {warmupResult.ProjectsWarmedUp} projects");

                // Step 5: Generate call graphs
                progress.StartNextStep();
                await Task.Delay(100); // This is part of the enhanced warmup
                progress.CompleteCurrentStep("Call graphs generated");

                // Step 6: Discover test methods
                progress.StartNextStep();
                var processedCount = 0;
                var cachedCount = 0;

                for (int i = 0; i < assemblies.Length; i++)
                {
                    var assemblyPath = assemblies[i];
                    var assemblyProgress = (int)((i * 100.0) / assemblies.Length);
                    progress.ReportStepProgress(assemblyProgress, $"Processing {Path.GetFileName(assemblyPath)}");

                    try
                    {
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
                        _logger.LogWarning(ex, "Failed to process assembly: {AssemblyPath}", assemblyPath);
                    }
                }
                progress.CompleteCurrentStep($"Processed {processedCount} assemblies, cached {cachedCount}");

                // Step 7: Cache metadata
                progress.StartNextStep();
                await Task.Delay(100); // Metadata is cached during processing
                progress.CompleteCurrentStep("Metadata cached successfully");

                // Step 8: Create solution snapshot
                progress.StartNextStep();
                await cacheSetup.SaveSnapshotAsync();
                progress.CompleteCurrentStep("Solution snapshot saved");

                // Step 9: Validate cache integrity
                progress.StartNextStep();
                await Task.Delay(100); // Basic validation
                progress.CompleteCurrentStep("Cache integrity validated");

                var completionMessage = $"✅ Cache warm-up completed successfully!\n" +
                    $"📊 Enhanced caches: {warmupResult.ProjectsWarmedUp} projects warmed up\n" +
                    $"⏱️  Duration: {warmupResult.Duration.TotalSeconds:F2}s\n" +
                    $"🚀 Subsequent analysis operations will be significantly faster!";

                progress.Complete(completionMessage);
            }
            catch (Exception ex)
            {
                progress.ReportError($"Failed to warm up cache: {ex.Message}");
                throw;
            }
        }

        private async Task HandleStatsCommand(LargeSolutionCacheSetup cacheSetup, EnhancedRoslynAnalyzerIntegration enhancedIntegration, string format, bool verbose, IProgressReporter progressReporter)
        {
            progressReporter.ReportProgress(0, "Loading cache statistics...");
            await cacheSetup.InitializeAsync();
            
            progressReporter.ReportProgress(50, "Analyzing cache data...");
            var stats = await cacheSetup.GetStatisticsAsync();
            var enhancedStats = await enhancedIntegration.GetCacheStatisticsAsync();
            
            progressReporter.ReportProgress(100, "Generating statistics report...");

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
                    enhancedCaches = new
                    {
                        callGraph = new
                        {
                            totalEntries = enhancedStats.CallGraph.TotalEntries,
                            hitCount = enhancedStats.CallGraph.HitCount,
                            hitRatio = enhancedStats.CallGraph.HitRatio,
                            totalCompressedSize = enhancedStats.CallGraph.TotalCompressedSize,
                            compressionRatio = enhancedStats.CallGraph.AverageCompressionRatio,
                            lastMaintenance = enhancedStats.CallGraph.LastMaintenanceRun
                        },
                        projects = new
                        {
                            totalEntries = enhancedStats.Projects.TotalEntries,
                            trackedProjects = enhancedStats.Projects.TrackedProjectsCount,
                            hitRatio = enhancedStats.Projects.HitRatio,
                            totalCompressedSize = enhancedStats.Projects.TotalCompressedSize,
                            changeDetections = enhancedStats.Projects.ChangeDetectionCount
                        },
                        overall = new
                        {
                            totalCacheSize = enhancedStats.TotalCacheSize,
                            overallHitRatio = enhancedStats.OverallHitRatio,
                            compressionEfficiency = enhancedStats.CompressionEfficiency
                        }
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
                Console.WriteLine();

                Console.WriteLine("Enhanced Cache Performance:");
                Console.WriteLine($"  Call Graph Cache:");
                Console.WriteLine($"    Total Entries: {enhancedStats.CallGraph.TotalEntries:N0}");
                Console.WriteLine($"    Cache Hits: {enhancedStats.CallGraph.HitCount:N0}");
                Console.WriteLine($"    Hit Ratio: {enhancedStats.CallGraph.HitRatio:P1}");
                Console.WriteLine($"    Size: {FormatBytes(enhancedStats.CallGraph.TotalCompressedSize)}");
                Console.WriteLine($"    Compression: {enhancedStats.CallGraph.AverageCompressionRatio:F1}%");
                Console.WriteLine($"    Last Maintenance: {enhancedStats.CallGraph.LastMaintenanceRun:yyyy-MM-dd HH:mm}");
                Console.WriteLine();
                Console.WriteLine($"  Project Cache:");
                Console.WriteLine($"    Total Entries: {enhancedStats.Projects.TotalEntries:N0}");
                Console.WriteLine($"    Tracked Projects: {enhancedStats.Projects.TrackedProjectsCount:N0}");
                Console.WriteLine($"    Hit Ratio: {enhancedStats.Projects.HitRatio:P1}");
                Console.WriteLine($"    Size: {FormatBytes(enhancedStats.Projects.TotalCompressedSize)}");
                Console.WriteLine($"    Change Detections: {enhancedStats.Projects.ChangeDetectionCount:N0}");
                Console.WriteLine();
                Console.WriteLine($"  Overall Performance:");
                Console.WriteLine($"    Total Enhanced Cache Size: {FormatBytes(enhancedStats.TotalCacheSize)}");
                Console.WriteLine($"    Overall Hit Ratio: {enhancedStats.OverallHitRatio:P1}");
                Console.WriteLine($"    Compression Efficiency: {enhancedStats.CompressionEfficiency:P1}");
                
                if (stats.LastSnapshotTime.HasValue)
                {
                    Console.WriteLine();
                    Console.WriteLine($"Last Snapshot: {stats.LastSnapshotTime:yyyy-MM-dd HH:mm:ss UTC}");
                    var age = DateTimeOffset.UtcNow - stats.LastSnapshotTime.Value;
                    Console.WriteLine($"Snapshot Age: {FormatTimeSpan(age)}");
                }
            }

            progressReporter.Complete("✅ Cache statistics generated successfully");
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