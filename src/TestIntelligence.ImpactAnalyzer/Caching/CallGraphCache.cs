using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Caching;

namespace TestIntelligence.ImpactAnalyzer.Caching
{
    /// <summary>
    /// Manages compressed caching of call graph analysis results with intelligent invalidation.
    /// </summary>
    public class CallGraphCache : IDisposable
    {
        private readonly ICompressedCache<CompressedCallGraphCacheEntry> _cache;
        private readonly ILogger<CallGraphCache>? _logger;
        private FileSystemWatcher? _fileWatcher;
        private readonly Dictionary<string, DateTime> _lastModifiedTimes = new();
        private readonly HashSet<string> _cachedProjectPaths = new();
        private readonly SemaphoreSlim _invalidationLock = new(1, 1);
        private readonly object _statsLock = new();
        private volatile bool _disposed = false;

        private CallGraphCacheStatistics _statistics = new();

        public CallGraphCache(
            string? cacheDirectory = null,
            CompressedCacheOptions? options = null,
            ILogger<CallGraphCache>? logger = null)
        {
            _logger = logger;
            
            var cacheOptions = options ?? new CompressedCacheOptions();
            var callGraphCacheDir = cacheDirectory ?? Path.Combine(Path.GetTempPath(), "TestIntelligence", "CallGraphCache");
            
            _cache = new CompressedCacheProvider<CompressedCallGraphCacheEntry>(callGraphCacheDir, cacheOptions, 
                logger as ILogger<CompressedCacheProvider<CompressedCallGraphCacheEntry>>);

            InitializeFileWatcher();
        }

        /// <summary>
        /// Gets a cached call graph result if available and valid.
        /// </summary>
        /// <param name="projectPath">Path to the project file.</param>
        /// <param name="referencedAssemblies">Referenced assemblies with their paths.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Cached call graph entry if valid, null otherwise.</returns>
        public async Task<CompressedCallGraphCacheEntry?> GetCallGraphAsync(
            string projectPath,
            IEnumerable<string> referencedAssemblies,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var dependencyHashes = await ComputeDependencyHashesAsync(referencedAssemblies, cancellationToken);
            var compilerVersion = GetCompilerVersion();
            var cacheKey = CompressedCallGraphCacheEntry.GenerateCacheKey(projectPath, dependencyHashes, compilerVersion);

            var cachedEntry = await _cache.GetAsync(cacheKey, cancellationToken);
            
            if (cachedEntry == null)
            {
                // Check if we have any entries for this project with different dependencies
                // This would indicate an invalidation scenario rather than a pure miss
                var hasProjectEntry = await HasAnyEntryForProjectAsync(projectPath, cancellationToken);
                if (hasProjectEntry)
                {
                    IncrementInvalidation();
                }
                else
                {
                    IncrementMiss();
                }
                return null;
            }

            // Validate that the cached entry is still valid
            if (!cachedEntry.IsValidForContext(projectPath, dependencyHashes, compilerVersion))
            {
                _logger?.LogDebug("Cache entry invalid for project: {ProjectPath}", projectPath);
                await _cache.RemoveAsync(cacheKey, cancellationToken);
                RemoveProjectFromTracking(projectPath);
                IncrementInvalidation();
                return null;
            }

            // Check if project files have been modified
            if (await HasProjectChangedAsync(projectPath, cachedEntry.CreatedAt, cancellationToken))
            {
                _logger?.LogDebug("Project files changed since cache creation: {ProjectPath}", projectPath);
                await _cache.RemoveAsync(cacheKey, cancellationToken);
                RemoveProjectFromTracking(projectPath);
                IncrementInvalidation();
                return null;
            }

            // Validate data integrity
            var validation = cachedEntry.ValidateIntegrity();
            if (!validation.IsValid)
            {
                _logger?.LogWarning("Cache entry failed integrity check: {Issues}", string.Join(", ", validation.Issues));
                await _cache.RemoveAsync(cacheKey, cancellationToken);
                RemoveProjectFromTracking(projectPath);
                IncrementCorruption();
                return null;
            }

            IncrementHit();
            _logger?.LogDebug("Cache hit for project: {ProjectPath}", projectPath);
            return cachedEntry;
        }

        /// <summary>
        /// Stores a call graph analysis result in the cache.
        /// </summary>
        /// <param name="projectPath">Path to the project file.</param>
        /// <param name="referencedAssemblies">Referenced assemblies with their paths.</param>
        /// <param name="callGraph">Forward call graph (caller -> callees).</param>
        /// <param name="reverseCallGraph">Reverse call graph (callee -> callers).</param>
        /// <param name="buildTime">Time taken to build the call graph.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task StoreCallGraphAsync(
            string projectPath,
            IEnumerable<string> referencedAssemblies,
            Dictionary<string, HashSet<string>> callGraph,
            Dictionary<string, HashSet<string>> reverseCallGraph,
            TimeSpan buildTime,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var dependencyHashes = await ComputeDependencyHashesAsync(referencedAssemblies, cancellationToken);
            var compilerVersion = GetCompilerVersion();
            var cacheKey = CompressedCallGraphCacheEntry.GenerateCacheKey(projectPath, dependencyHashes, compilerVersion);

            var entry = new CompressedCallGraphCacheEntry
            {
                ProjectPath = projectPath,
                CreatedAt = DateTime.UtcNow,
                DependenciesHash = ComputeHash(string.Join("|", dependencyHashes)),
                CompilerVersion = compilerVersion,
                CallGraph = callGraph,
                ReverseCallGraph = reverseCallGraph,
                BuildTime = buildTime,
                Metadata = new Dictionary<string, object>
                {
                    ["TotalMethods"] = callGraph.Count,
                    ["TotalEdges"] = callGraph.Values.Sum(callees => callees.Count),
                    ["ReferencedAssembliesCount"] = referencedAssemblies.Count()
                }
            };

            // Estimate uncompressed size
            entry.UncompressedSize = CacheCompressionUtilities.EstimateCompressionRatio(entry) > 0 
                ? (long)(System.Text.Json.JsonSerializer.Serialize(entry).Length)
                : 1024; // Default estimate

            await _cache.SetAsync(cacheKey, entry, TimeSpan.FromDays(30), cancellationToken);
            
            // Update last modified times for invalidation tracking
            await UpdateProjectModificationTimeAsync(projectPath, cancellationToken);
            
            // Track that we have an entry for this project
            lock (_statsLock)
            {
                _cachedProjectPaths.Add(projectPath);
            }
            
            IncrementStore();
            _logger?.LogDebug("Stored call graph cache for project: {ProjectPath}, build time: {BuildTime}", 
                projectPath, buildTime);
        }

        /// <summary>
        /// Invalidates cache entries for a specific project.
        /// </summary>
        /// <param name="projectPath">Path to the project file.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task InvalidateProjectAsync(string projectPath, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _invalidationLock.WaitAsync(cancellationToken);
            try
            {
                var invalidatedCount = 0;
                
                // Remove from modification time tracking
                _lastModifiedTimes.Remove(projectPath);
                
                // Remove from cached project paths
                lock (_statsLock)
                {
                    _cachedProjectPaths.Remove(projectPath);
                }
                
                // Since we use hashed cache keys, we need to invalidate by checking all assemblies for this project
                // This is a limitation of the current design, but we'll do our best
                var projectDirectory = Path.GetDirectoryName(projectPath);
                if (!string.IsNullOrEmpty(projectDirectory))
                {
                    var assemblyFiles = Directory.GetFiles(projectDirectory, "*.dll", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(projectDirectory, "*.exe", SearchOption.AllDirectories))
                        .ToList();
                    
                    foreach (var assemblyFile in assemblyFiles.Take(10)) // Limit to prevent excessive processing
                    {
                        try
                        {
                            var dependencyHashes = await ComputeDependencyHashesAsync(new[] { assemblyFile }, cancellationToken);
                            var compilerVersion = GetCompilerVersion();
                            var possibleCacheKey = CompressedCallGraphCacheEntry.GenerateCacheKey(projectPath, dependencyHashes, compilerVersion);
                            
                            if (await _cache.RemoveAsync(possibleCacheKey, cancellationToken))
                            {
                                invalidatedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogDebug(ex, "Failed to invalidate cache for assembly: {AssemblyFile}", assemblyFile);
                        }
                    }
                }
                
                if (invalidatedCount > 0)
                {
                    IncrementInvalidation();
                    _logger?.LogInformation("Invalidated {Count} cache entries for project: {ProjectPath}", invalidatedCount, projectPath);
                }
                else
                {
                    _logger?.LogDebug("No cache entries found to invalidate for project: {ProjectPath}", projectPath);
                }
            }
            finally
            {
                _invalidationLock.Release();
            }
        }

        /// <summary>
        /// Gets cache statistics including compression and hit rates.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Cache statistics.</returns>
        public async Task<CallGraphCacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var compressionStats = await _cache.GetStatsAsync(cancellationToken);
            
            lock (_statsLock)
            {
                return new CallGraphCacheStatistics
                {
                    TotalEntries = compressionStats.TotalEntries,
                    TotalCompressedSize = compressionStats.TotalCompressedSize,
                    TotalUncompressedSize = compressionStats.TotalUncompressedSize,
                    AverageCompressionRatio = compressionStats.AverageCompressionRatio,
                    HitCount = _statistics.HitCount,
                    MissCount = _statistics.MissCount,
                    InvalidationCount = _statistics.InvalidationCount,
                    CorruptionCount = _statistics.CorruptionCount,
                    StoreCount = _statistics.StoreCount,
                    HitRatio = (_statistics.HitCount + _statistics.MissCount) > 0 
                        ? (double)_statistics.HitCount / (_statistics.HitCount + _statistics.MissCount) 
                        : 0,
                    LastMaintenanceRun = compressionStats.LastMaintenanceRun
                };
            }
        }

        /// <summary>
        /// Performs cache maintenance including cleanup and compression optimization.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task PerformMaintenanceAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _cache.PerformMaintenanceAsync(cancellationToken);
            _logger?.LogInformation("Call graph cache maintenance completed");
        }

        /// <summary>
        /// Clears all cached call graph entries.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _cache.ClearAsync(cancellationToken);
            _lastModifiedTimes.Clear();
            
            lock (_statsLock)
            {
                _statistics = new CallGraphCacheStatistics();
                _cachedProjectPaths.Clear();
            }
            
            _logger?.LogInformation("Call graph cache cleared");
        }

        private void InitializeFileWatcher()
        {
            // File watching is optional and may not work in all environments
            try
            {
                _fileWatcher = new FileSystemWatcher
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime
                };
                
                _fileWatcher.Changed += OnFileChanged;
                _fileWatcher.Created += OnFileChanged;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to initialize file system watcher");
            }
        }

        private async void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (IsRelevantFileForCacheInvalidation(e.FullPath))
            {
                // Mark for invalidation on next access
                _lastModifiedTimes[e.FullPath] = DateTime.UtcNow;
                _logger?.LogDebug("File change detected: {FilePath}", e.FullPath);
                
                // Proactively invalidate cache entries for the affected project
                var projectPath = FindProjectFileForSourceFile(e.FullPath);
                if (!string.IsNullOrEmpty(projectPath))
                {
                    await InvalidateProjectAsync(projectPath!);
                }
            }
        }

        private static bool IsRelevantFileForCacheInvalidation(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension is ".cs" or ".vb" or ".fs" or ".csproj" or ".vbproj" or ".fsproj";
        }

        private string? FindProjectFileForSourceFile(string sourceFilePath)
        {
            var directory = Path.GetDirectoryName(sourceFilePath);
            
            while (!string.IsNullOrEmpty(directory))
            {
                var projectFiles = Directory.GetFiles(directory, "*.csproj")
                    .Concat(Directory.GetFiles(directory, "*.vbproj"))
                    .Concat(Directory.GetFiles(directory, "*.fsproj"));
                
                var projectFile = projectFiles.FirstOrDefault();
                if (projectFile != null)
                {
                    return projectFile;
                }
                
                directory = Path.GetDirectoryName(directory);
            }
            
            return null;
        }

        private Task<List<string>> ComputeDependencyHashesAsync(IEnumerable<string> assemblies, CancellationToken cancellationToken)
        {
            var hashes = new List<string>();
            
            foreach (var assembly in assemblies)
            {
                try
                {
                    if (File.Exists(assembly))
                    {
                        var fileInfo = new FileInfo(assembly);
                        var hashInput = $"{assembly}|{fileInfo.LastWriteTimeUtc:O}|{fileInfo.Length}";
                        hashes.Add(ComputeHash(hashInput));
                    }
                    else
                    {
                        // For GAC or other assemblies, use name and version
                        hashes.Add(ComputeHash(assembly));
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to compute hash for assembly: {Assembly}", assembly);
                    hashes.Add(ComputeHash(assembly)); // Fallback to path hash
                }
            }
            
            return Task.FromResult(hashes);
        }

        private string GetCompilerVersion()
        {
            try
            {
                var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
                return assemblyVersion?.ToString() ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private Task<bool> HasProjectChangedAsync(string projectPath, DateTime cacheCreationTime, CancellationToken cancellationToken)
        {
            try
            {
                if (File.Exists(projectPath))
                {
                    var projectInfo = new FileInfo(projectPath);
                    if (projectInfo.LastWriteTimeUtc > cacheCreationTime)
                        return Task.FromResult(true);
                }

                // Check for source file changes in the project directory
                var projectDirectory = Path.GetDirectoryName(projectPath);
                if (!string.IsNullOrEmpty(projectDirectory) && Directory.Exists(projectDirectory))
                {
                    var sourceFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(projectDirectory, "*.vb", SearchOption.AllDirectories));
                    
                    foreach (var sourceFile in sourceFiles)
                    {
                        var fileInfo = new FileInfo(sourceFile);
                        if (fileInfo.LastWriteTimeUtc > cacheCreationTime)
                            return Task.FromResult(true);
                    }
                }

                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to check project modification time: {ProjectPath}", projectPath);
                return Task.FromResult(true); // Assume changed if we can't check
            }
        }

        private async Task UpdateProjectModificationTimeAsync(string projectPath, CancellationToken cancellationToken)
        {
            await _invalidationLock.WaitAsync(cancellationToken);
            try
            {
                _lastModifiedTimes[projectPath] = DateTime.UtcNow;
            }
            finally
            {
                _invalidationLock.Release();
            }
        }

        private static string ComputeHash(string input)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        private void IncrementHit()
        {
            lock (_statsLock)
            {
                _statistics.HitCount++;
            }
        }

        private void IncrementMiss()
        {
            lock (_statsLock)
            {
                _statistics.MissCount++;
            }
        }

        private void IncrementInvalidation()
        {
            lock (_statsLock)
            {
                _statistics.InvalidationCount++;
            }
        }

        private void IncrementCorruption()
        {
            lock (_statsLock)
            {
                _statistics.CorruptionCount++;
            }
        }

        private void IncrementStore()
        {
            lock (_statsLock)
            {
                _statistics.StoreCount++;
            }
        }

        private Task<bool> HasAnyEntryForProjectAsync(string projectPath, CancellationToken cancellationToken)
        {
            lock (_statsLock)
            {
                return Task.FromResult(_cachedProjectPaths.Contains(projectPath));
            }
        }

        private void RemoveProjectFromTracking(string projectPath)
        {
            lock (_statsLock)
            {
                _cachedProjectPaths.Remove(projectPath);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CallGraphCache));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _fileWatcher?.Dispose();
            _cache?.Dispose();
            _invalidationLock?.Dispose();
        }
    }

    /// <summary>
    /// Statistics for call graph cache performance and usage.
    /// </summary>
    public class CallGraphCacheStatistics
    {
        public int TotalEntries { get; set; }
        public long TotalCompressedSize { get; set; }
        public long TotalUncompressedSize { get; set; }
        public long AverageCompressionRatio { get; set; }
        public int HitCount { get; set; }
        public int MissCount { get; set; }
        public int InvalidationCount { get; set; }
        public int CorruptionCount { get; set; }
        public int StoreCount { get; set; }
        public double HitRatio { get; set; }
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