using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Caching;

namespace TestIntelligence.ImpactAnalyzer.Caching
{
    /// <summary>
    /// Demonstrates integration of enhanced caching with RoslynAnalyzer for improved performance.
    /// </summary>
    public class EnhancedRoslynAnalyzerIntegration : IDisposable
    {
        private readonly CallGraphCache _callGraphCache;
        private readonly ProjectCacheManager _projectCache;
        private readonly ILogger<EnhancedRoslynAnalyzerIntegration>? _logger;
        private volatile bool _disposed = false;

        public EnhancedRoslynAnalyzerIntegration(
            string? cacheDirectory = null,
            ILogger<EnhancedRoslynAnalyzerIntegration>? logger = null)
        {
            _logger = logger;
            
            var cacheOptions = new CompressedCacheOptions
            {
                MaxCacheSizeBytes = 100 * 1024 * 1024, // 100MB
                EnableBackgroundMaintenance = true,
                CompressionLevel = System.IO.Compression.CompressionLevel.Optimal
            };

            var callGraphCacheDir = cacheDirectory != null 
                ? Path.Combine(cacheDirectory, "CallGraph")
                : null;
            var projectCacheDir = cacheDirectory != null 
                ? Path.Combine(cacheDirectory, "Projects")
                : null;

            _callGraphCache = new CallGraphCache(callGraphCacheDir, cacheOptions, 
                logger as ILogger<CallGraphCache>);
            _projectCache = new ProjectCacheManager(projectCacheDir, cacheOptions,
                logger as ILogger<ProjectCacheManager>);
        }

        /// <summary>
        /// Analyzes a solution with enhanced caching for improved performance.
        /// </summary>
        /// <param name="solutionPath">Path to the solution file.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Analysis results with caching statistics.</returns>
        public async Task<EnhancedAnalysisResult> AnalyzeSolutionAsync(
            string solutionPath,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(solutionPath) || !File.Exists(solutionPath))
                throw new ArgumentException($"Solution file not found: {solutionPath}");

            _logger?.LogInformation("Starting enhanced analysis of solution: {SolutionPath}", solutionPath);

            var startTime = DateTime.UtcNow;
            var result = new EnhancedAnalysisResult
            {
                SolutionPath = solutionPath,
                StartTime = startTime
            };

            try
            {
                // Step 1: Discover and cache project metadata
                var projectPaths = DiscoverProjects(solutionPath);
                _logger?.LogDebug("Discovered {Count} projects in solution", projectPaths.Count);

                var projectCacheResults = await CacheProjectMetadataAsync(projectPaths, cancellationToken);
                result.ProjectsCached = projectCacheResults.Count(kvp => kvp.Value);
                result.ProjectsProcessed = projectCacheResults.Count;

                // Step 2: Build call graphs with caching
                var callGraphResults = await BuildCallGraphsAsync(projectPaths, cancellationToken);
                result.CallGraphsCached = callGraphResults.Count(cg => cg.WasCached);
                result.CallGraphsBuilt = callGraphResults.Count;

                // Step 3: Gather statistics
                await GatherCacheStatisticsAsync(result, cancellationToken);

                result.EndTime = DateTime.UtcNow;
                result.TotalDuration = result.EndTime - result.StartTime;
                result.Success = true;

                _logger?.LogInformation("Enhanced analysis completed in {Duration}ms with {CacheHitRatio:P1} cache hit ratio",
                    result.TotalDuration.TotalMilliseconds, result.OverallCacheHitRatio);

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Enhanced analysis failed for solution: {SolutionPath}", solutionPath);
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.EndTime = DateTime.UtcNow;
                result.TotalDuration = result.EndTime - result.StartTime;
                return result;
            }
        }

        /// <summary>
        /// Warms up the cache for a solution to improve subsequent analysis performance.
        /// </summary>
        /// <param name="solutionPath">Path to the solution file.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Cache warming results.</returns>
        public async Task<CacheWarmupResult> WarmupCacheAsync(
            string solutionPath,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            _logger?.LogInformation("Starting cache warmup for solution: {SolutionPath}", solutionPath);

            var startTime = DateTime.UtcNow;
            var result = new CacheWarmupResult
            {
                SolutionPath = solutionPath,
                StartTime = startTime
            };

            try
            {
                // Pre-populate project cache
                var projectPaths = DiscoverProjects(solutionPath);
                _logger?.LogInformation("Warm-up discovered {ProjectCount} projects from solution: {SolutionPath}", projectPaths.Count, solutionPath);
                
                if (projectPaths.Count == 0)
                {
                    _logger?.LogWarning("No projects found in solution for warm-up: {SolutionPath}", solutionPath);
                }
                
                // Warmup project cache
                var projectWarmupTasks = projectPaths.Select(async projectPath =>
                {
                    try
                    {
                        var entry = await _projectCache.CreateProjectEntryAsync(projectPath, cancellationToken: cancellationToken);
                        await _projectCache.StoreProjectAsync(entry, cancellationToken: cancellationToken);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to warmup project cache for: {ProjectPath}", projectPath);
                        return false;
                    }
                }).ToArray();

                var projectWarmupResults = await Task.WhenAll(projectWarmupTasks);
                var projectsWarmedUp = projectWarmupResults.Count(success => success);

                // Warmup call graph cache  
                var callGraphResults = await BuildCallGraphsAsync(projectPaths, cancellationToken);
                var callGraphsWarmedUp = callGraphResults.Count(cg => !cg.WasCached); // Count newly built graphs

                // Report the number of projects processed, not the sum of individual cache types
                result.ProjectsWarmedUp = projectsWarmedUp;
                result.Success = true;
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;

                _logger?.LogInformation("Cache warmup completed: {ProjectsWarmedUp} projects ({ProjectCacheCount} project cache, {CallGraphsWarmedUp} call graphs) in {Duration}ms",
                    projectsWarmedUp, projectsWarmedUp, callGraphsWarmedUp, result.Duration.TotalMilliseconds);

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Cache warmup failed for solution: {SolutionPath}", solutionPath);
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
                return result;
            }
        }

        /// <summary>
        /// Gets comprehensive cache statistics for monitoring and optimization.
        /// </summary>
        public async Task<EnhancedCacheStatistics> GetCacheStatisticsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var callGraphStats = await _callGraphCache.GetStatisticsAsync(cancellationToken);
            var projectStats = await _projectCache.GetStatisticsAsync(cancellationToken);

            return new EnhancedCacheStatistics
            {
                CallGraph = callGraphStats,
                Projects = projectStats,
                TotalCacheSize = callGraphStats.TotalCompressedSize + projectStats.TotalCompressedSize,
                OverallHitRatio = CalculateOverallHitRatio(callGraphStats, projectStats),
                CompressionEfficiency = CalculateCompressionEfficiency(callGraphStats, projectStats)
            };
        }

        /// <summary>
        /// Performs maintenance on all cache components.
        /// </summary>
        public async Task PerformMaintenanceAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            _logger?.LogInformation("Performing cache maintenance");

            var tasks = new[]
            {
                _callGraphCache.PerformMaintenanceAsync(cancellationToken),
                _projectCache.PerformMaintenanceAsync(cancellationToken)
            };

            await Task.WhenAll(tasks);
            _logger?.LogInformation("Cache maintenance completed");
        }

        private List<string> DiscoverProjects(string solutionPath)
        {
            var projects = new List<string>();
            
            // Get the absolute path first to handle relative solution paths correctly
            var fullSolutionPath = Path.GetFullPath(solutionPath);
            var solutionDirectory = Path.GetDirectoryName(fullSolutionPath);

            if (string.IsNullOrEmpty(solutionDirectory))
                return projects;

            // Enhanced solution parsing to properly handle .NET solution file format
            try
            {
                var solutionContent = File.ReadAllText(fullSolutionPath);
                var lines = solutionContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    
                    // Look for project lines: Project("{PROJECT-GUID}") = "ProjectName", "RelativePath", "{PROJECT-GUID}"
                    if (trimmedLine.StartsWith("Project(\"{", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            // Parse the project line format: Project("{GUID}") = "Name", "Path", "{GUID}"
                            if (!trimmedLine.Contains("="))
                                continue;
                                
                            var afterEquals = trimmedLine.Substring(trimmedLine.IndexOf('=') + 1).Trim();
                            var parts = afterEquals.Split(',');
                            
                            if (parts.Length >= 2)
                            {
                                // The second part is the relative path to the project file
                                var relativePath = parts[1].Trim(' ', '"', '\t');
                                
                                _logger?.LogDebug("Parsing project line - relative path: '{RelativePath}'", relativePath);
                                
                                // Only process C# project files
                                if (relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Normalize path separators for cross-platform compatibility
                                    relativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar);
                                    var fullPath = Path.GetFullPath(Path.Combine(solutionDirectory, relativePath));
                                    
                                    _logger?.LogDebug("Full project path: '{FullPath}'", fullPath);
                                    
                                    if (File.Exists(fullPath))
                                    {
                                        projects.Add(fullPath);
                                        _logger?.LogInformation("Discovered project: {ProjectPath}", fullPath);
                                    }
                                    else
                                    {
                                        _logger?.LogWarning("Project file not found: {ProjectPath}", fullPath);
                                    }
                                }
                                else
                                {
                                    _logger?.LogDebug("Skipping non-C# project: {RelativePath}", relativePath);
                                }
                            }
                            else
                            {
                                _logger?.LogDebug("Project line has insufficient parts: {PartCount}", parts.Length);
                            }
                        }
                        catch (Exception parseEx)
                        {
                            _logger?.LogWarning(parseEx, "Failed to parse project line: {Line}", trimmedLine);
                        }
                    }
                }
                
                _logger?.LogInformation("Discovered {ProjectCount} projects in solution: {SolutionPath}", projects.Count, fullSolutionPath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to parse solution file: {SolutionPath}", fullSolutionPath);
            }

            return projects;
        }

        private async Task<Dictionary<string, bool>> CacheProjectMetadataAsync(
            IEnumerable<string> projectPaths,
            CancellationToken cancellationToken)
        {
            var results = new Dictionary<string, bool>();

            foreach (var projectPath in projectPaths)
            {
                try
                {
                    var cachedEntry = await _projectCache.GetProjectAsync(projectPath, cancellationToken: cancellationToken);
                    if (cachedEntry != null)
                    {
                        results[projectPath] = true; // Cache hit
                        _logger?.LogDebug("Project cache hit: {ProjectPath}", projectPath);
                    }
                    else
                    {
                        // Cache miss - create and store entry
                        var entry = await _projectCache.CreateProjectEntryAsync(projectPath, cancellationToken: cancellationToken);
                        await _projectCache.StoreProjectAsync(entry, cancellationToken: cancellationToken);
                        results[projectPath] = false; // Cache miss
                        _logger?.LogDebug("Project cached: {ProjectPath}", projectPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to process project: {ProjectPath}", projectPath);
                    results[projectPath] = false;
                }
            }

            return results;
        }

        private async Task<List<CallGraphBuildResult>> BuildCallGraphsAsync(
            IEnumerable<string> projectPaths,
            CancellationToken cancellationToken)
        {
            var results = new List<CallGraphBuildResult>();

            foreach (var projectPath in projectPaths)
            {
                try
                {
                    // In a real implementation, you'd get the actual referenced assemblies
                    var referencedAssemblies = new[] { "System.dll", "mscorlib.dll" };

                    var cachedCallGraph = await _callGraphCache.GetCallGraphAsync(
                        projectPath, referencedAssemblies, cancellationToken);

                    if (cachedCallGraph != null)
                    {
                        results.Add(new CallGraphBuildResult
                        {
                            ProjectPath = projectPath,
                            WasCached = true,
                            BuildTime = TimeSpan.Zero
                        });
                        _logger?.LogDebug("Call graph cache hit: {ProjectPath}", projectPath);
                    }
                    else
                    {
                        // Build call graph (simulated)
                        var buildStartTime = DateTime.UtcNow;
                        var callGraph = SimulateCallGraphBuilding(projectPath);
                        var buildTime = DateTime.UtcNow - buildStartTime;

                        await _callGraphCache.StoreCallGraphAsync(
                            projectPath, referencedAssemblies,
                            callGraph.callGraph, callGraph.reverseCallGraph, buildTime, cancellationToken);

                        results.Add(new CallGraphBuildResult
                        {
                            ProjectPath = projectPath,
                            WasCached = false,
                            BuildTime = buildTime
                        });
                        _logger?.LogDebug("Call graph built and cached: {ProjectPath} in {Duration}ms",
                            projectPath, buildTime.TotalMilliseconds);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to build call graph for project: {ProjectPath}", projectPath);
                    results.Add(new CallGraphBuildResult
                    {
                        ProjectPath = projectPath,
                        WasCached = false,
                        BuildTime = TimeSpan.Zero,
                        Failed = true
                    });
                }
            }

            return results;
        }

        private (Dictionary<string, HashSet<string>> callGraph, Dictionary<string, HashSet<string>> reverseCallGraph) 
            SimulateCallGraphBuilding(string projectPath)
        {
            // Simple simulation - in a real implementation, you'd use Roslyn to analyze the code
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var callGraph = new Dictionary<string, HashSet<string>>
            {
                [$"{projectName}.ClassA.MethodA"] = new HashSet<string> { $"{projectName}.ClassB.MethodB" },
                [$"{projectName}.ClassB.MethodB"] = new HashSet<string> { $"{projectName}.ClassC.MethodC" },
                [$"{projectName}.ClassC.MethodC"] = new HashSet<string>()
            };

            var reverseCallGraph = new Dictionary<string, HashSet<string>>
            {
                [$"{projectName}.ClassB.MethodB"] = new HashSet<string> { $"{projectName}.ClassA.MethodA" },
                [$"{projectName}.ClassC.MethodC"] = new HashSet<string> { $"{projectName}.ClassB.MethodB" }
            };

            return (callGraph, reverseCallGraph);
        }

        private async Task GatherCacheStatisticsAsync(EnhancedAnalysisResult result, CancellationToken cancellationToken)
        {
            var stats = await GetCacheStatisticsAsync(cancellationToken);
            
            result.TotalCacheHits = stats.CallGraph.HitCount + stats.Projects.HitCount;
            result.TotalCacheMisses = stats.CallGraph.MissCount + stats.Projects.MissCount;
            result.OverallCacheHitRatio = stats.OverallHitRatio;
            result.CompressionRatio = stats.CompressionEfficiency;
            result.TotalCacheSize = stats.TotalCacheSize;
        }

        private static double CalculateOverallHitRatio(CallGraphCacheStatistics callGraphStats, ProjectCacheStatistics projectStats)
        {
            var totalHits = callGraphStats.HitCount + projectStats.HitCount;
            var totalRequests = totalHits + callGraphStats.MissCount + projectStats.MissCount;
            
            return totalRequests > 0 ? (double)totalHits / totalRequests : 0;
        }

        private static double CalculateCompressionEfficiency(CallGraphCacheStatistics callGraphStats, ProjectCacheStatistics projectStats)
        {
            var totalUncompressed = callGraphStats.TotalUncompressedSize + projectStats.TotalUncompressedSize;
            var totalCompressed = callGraphStats.TotalCompressedSize + projectStats.TotalCompressedSize;
            
            return totalUncompressed > 0 ? 1.0 - ((double)totalCompressed / totalUncompressed) : 0;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(EnhancedRoslynAnalyzerIntegration));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _callGraphCache?.Dispose();
            _projectCache?.Dispose();
        }
    }

    /// <summary>
    /// Results from enhanced solution analysis with caching statistics.
    /// </summary>
    public class EnhancedAnalysisResult
    {
        public string SolutionPath { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        public int ProjectsProcessed { get; set; }
        public int ProjectsCached { get; set; }
        public int CallGraphsBuilt { get; set; }
        public int CallGraphsCached { get; set; }

        public int TotalCacheHits { get; set; }
        public int TotalCacheMisses { get; set; }
        public double OverallCacheHitRatio { get; set; }
        public double CompressionRatio { get; set; }
        public long TotalCacheSize { get; set; }

        public string TotalCacheSizeFormatted => FormatBytes(TotalCacheSize);

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

    /// <summary>
    /// Results from cache warmup operation.
    /// </summary>
    public class CacheWarmupResult
    {
        public string SolutionPath { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int ProjectsWarmedUp { get; set; }
    }

    /// <summary>
    /// Combined cache statistics from all cache components.
    /// </summary>
    public class EnhancedCacheStatistics
    {
        public CallGraphCacheStatistics CallGraph { get; set; } = new();
        public ProjectCacheStatistics Projects { get; set; } = new();
        public long TotalCacheSize { get; set; }
        public double OverallHitRatio { get; set; }
        public double CompressionEfficiency { get; set; }
    }

    /// <summary>
    /// Result of call graph building with caching information.
    /// </summary>
    internal class CallGraphBuildResult
    {
        public string ProjectPath { get; set; } = string.Empty;
        public bool WasCached { get; set; }
        public TimeSpan BuildTime { get; set; }
        public bool Failed { get; set; }
    }
}