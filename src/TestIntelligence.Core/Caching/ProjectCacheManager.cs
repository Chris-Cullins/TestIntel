using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.Core.Caching
{
    /// <summary>
    /// Manages project-level caching for metadata, build configurations, and dependencies.
    /// </summary>
    public class ProjectCacheManager : IDisposable
    {
        private readonly ICompressedCache<ProjectCacheEntry> _cache;
        private readonly ILogger<ProjectCacheManager>? _logger;
        private FileSystemWatcher? _fileWatcher;
        private readonly Dictionary<string, ProjectChangeTracker> _changeTrackers = new();
        private readonly SemaphoreSlim _trackerLock = new(1, 1);
        private readonly object _statsLock = new();
        private volatile bool _disposed = false;

        private ProjectCacheStatistics _statistics = new();

        public ProjectCacheManager(
            string? cacheDirectory = null,
            CompressedCacheOptions? options = null,
            ILogger<ProjectCacheManager>? logger = null)
        {
            _logger = logger;
            
            var cacheOptions = options ?? new CompressedCacheOptions();
            var projectCacheDir = cacheDirectory ?? Path.Combine(Path.GetTempPath(), "TestIntelligence", "ProjectCache");
            
            _cache = new CompressedCacheProvider<ProjectCacheEntry>(projectCacheDir, cacheOptions, 
                logger as ILogger<CompressedCacheProvider<ProjectCacheEntry>>);

            InitializeFileWatcher();
        }

        /// <summary>
        /// Gets cached project metadata if available and valid.
        /// </summary>
        /// <param name="projectPath">Path to the project file.</param>
        /// <param name="targetFramework">Target framework (optional filter).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Cached project entry if valid, null otherwise.</returns>
        public async Task<ProjectCacheEntry?> GetProjectAsync(
            string projectPath,
            string? targetFramework = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(projectPath) || !File.Exists(projectPath))
            {
                IncrementMiss();
                return null;
            }

            var cacheKey = GenerateProjectCacheKey(projectPath, targetFramework);
            var cachedEntry = await _cache.GetAsync(cacheKey, cancellationToken);
            
            if (cachedEntry == null)
            {
                return null;
            }

            // Validate that the cached entry is still valid
            if (!await IsProjectEntryValidAsync(cachedEntry, cancellationToken))
            {
                _logger?.LogDebug("Project cache entry invalid: {ProjectPath}", projectPath);
                await _cache.RemoveAsync(cacheKey, cancellationToken);
                IncrementInvalidation();
                return null;
            }

            IncrementHit();
            _logger?.LogDebug("Project cache hit: {ProjectPath}", projectPath);
            return cachedEntry;
        }

        /// <summary>
        /// Stores project metadata in the cache.
        /// </summary>
        /// <param name="projectEntry">Project metadata to cache.</param>
        /// <param name="expiration">Cache expiration time (optional).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task StoreProjectAsync(
            ProjectCacheEntry projectEntry,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (projectEntry == null)
                throw new ArgumentNullException(nameof(projectEntry));

            if (string.IsNullOrEmpty(projectEntry.ProjectPath))
                throw new ArgumentException("Project path cannot be null or empty", nameof(projectEntry));

            var cacheKey = GenerateProjectCacheKey(projectEntry.ProjectPath, projectEntry.TargetFramework);
            
            // Update metadata
            projectEntry.LastAnalyzed = DateTime.UtcNow;
            projectEntry.ContentHash = await ComputeProjectContentHashAsync(projectEntry.ProjectPath, cancellationToken);

            // Set default expiration if not specified
            var cacheExpiration = expiration ?? TimeSpan.FromDays(7);
            
            await _cache.SetAsync(cacheKey, projectEntry, cacheExpiration, cancellationToken);
            
            // Start tracking changes for this project
            await StartTrackingProjectAsync(projectEntry.ProjectPath, cancellationToken);
            
            IncrementStore();
            _logger?.LogDebug("Stored project cache entry: {ProjectPath}", projectEntry.ProjectPath);
        }

        /// <summary>
        /// Gets cached project metadata for multiple projects.
        /// </summary>
        /// <param name="projectPaths">Paths to project files.</param>
        /// <param name="targetFramework">Target framework (optional filter).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Dictionary of project paths to cached entries (excludes invalid/missing entries).</returns>
        public async Task<Dictionary<string, ProjectCacheEntry>> GetProjectsAsync(
            IEnumerable<string> projectPaths,
            string? targetFramework = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var results = new Dictionary<string, ProjectCacheEntry>();
            var tasks = projectPaths.Select(async path =>
            {
                var entry = await GetProjectAsync(path, targetFramework, cancellationToken);
                return (Path: path, Entry: entry);
            });

            var completedTasks = await Task.WhenAll(tasks);
            
            foreach (var (path, entry) in completedTasks)
            {
                if (entry != null)
                {
                    results[path] = entry;
                }
            }

            return results;
        }

        /// <summary>
        /// Invalidates cached entries for a specific project.
        /// </summary>
        /// <param name="projectPath">Path to the project file.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task InvalidateProjectAsync(string projectPath, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(projectPath))
                return;

            // Remove entries for all target frameworks
            var baseKey = GenerateProjectCacheKey(projectPath, null);
            var allKeys = new[]
            {
                baseKey,
                GenerateProjectCacheKey(projectPath, "net6.0"),
                GenerateProjectCacheKey(projectPath, "net7.0"),
                GenerateProjectCacheKey(projectPath, "net8.0"),
                GenerateProjectCacheKey(projectPath, "netstandard2.0"),
                GenerateProjectCacheKey(projectPath, "netstandard2.1"),
                GenerateProjectCacheKey(projectPath, "netframework4.8")
            };

            var removedCount = 0;
            foreach (var key in allKeys)
            {
                if (await _cache.RemoveAsync(key, cancellationToken))
                {
                    removedCount++;
                }
            }

            // Stop tracking changes
            await StopTrackingProjectAsync(projectPath, cancellationToken);
            
            IncrementInvalidation(removedCount);
            _logger?.LogInformation("Invalidated {Count} cache entries for project: {ProjectPath}", removedCount, projectPath);
        }

        /// <summary>
        /// Gets project cache statistics.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Cache statistics including compression and hit rates.</returns>
        public async Task<ProjectCacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var compressionStats = await _cache.GetStatsAsync(cancellationToken);
            
            lock (_statsLock)
            {
                return new ProjectCacheStatistics
                {
                    TotalEntries = compressionStats.TotalEntries,
                    TotalCompressedSize = compressionStats.TotalCompressedSize,
                    TotalUncompressedSize = compressionStats.TotalUncompressedSize,
                    AverageCompressionRatio = compressionStats.AverageCompressionRatio,
                    HitCount = _statistics.HitCount,
                    MissCount = _statistics.MissCount,
                    InvalidationCount = _statistics.InvalidationCount,
                    StoreCount = _statistics.StoreCount,
                    ChangeDetectionCount = _statistics.ChangeDetectionCount,
                    HitRatio = (_statistics.HitCount + _statistics.MissCount) > 0 
                        ? (double)_statistics.HitCount / (_statistics.HitCount + _statistics.MissCount) 
                        : 0,
                    LastMaintenanceRun = compressionStats.LastMaintenanceRun,
                    TrackedProjectsCount = _changeTrackers.Count
                };
            }
        }

        /// <summary>
        /// Performs cache maintenance including cleanup and validation.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task PerformMaintenanceAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _cache.PerformMaintenanceAsync(cancellationToken);
            await CleanupStaleTrackersAsync(cancellationToken);
            
            _logger?.LogInformation("Project cache maintenance completed");
        }

        /// <summary>
        /// Clears all cached project entries.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _cache.ClearAsync(cancellationToken);
            await ClearAllTrackersAsync(cancellationToken);
            
            lock (_statsLock)
            {
                _statistics = new ProjectCacheStatistics();
            }
            
            _logger?.LogInformation("Project cache cleared");
        }

        /// <summary>
        /// Creates a project cache entry from a project file path.
        /// </summary>
        /// <param name="projectPath">Path to the project file.</param>
        /// <param name="targetFramework">Target framework.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Project cache entry with discovered metadata.</returns>
        public async Task<ProjectCacheEntry> CreateProjectEntryAsync(
            string projectPath,
            string? targetFramework = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(projectPath) || !File.Exists(projectPath))
                throw new ArgumentException($"Project file not found: {projectPath}", nameof(projectPath));

            var projectDirectory = Path.GetDirectoryName(projectPath) ?? "";
            var entry = new ProjectCacheEntry
            {
                ProjectPath = projectPath,
                TargetFramework = targetFramework ?? "unknown"
            };

            // Discover source files
            if (Directory.Exists(projectDirectory))
            {
                var sourceFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(projectDirectory, "*.vb", SearchOption.AllDirectories))
                    .Select(f => GetRelativePath(projectDirectory, f))
                    .ToList();
                
                entry.SourceFiles = sourceFiles;
            }

            // Try to read project properties from the file
            try
            {
                var projectContent = File.ReadAllText(projectPath);
                entry.ProjectProperties = ParseProjectProperties(projectContent);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read project properties: {ProjectPath}", projectPath);
            }

            // Discover project references
            entry.ProjectReferences = DiscoverProjectReferences(projectPath);

            // Generate content hash
            entry.ContentHash = await ComputeProjectContentHashAsync(projectPath, cancellationToken);

            return entry;
        }

        private void InitializeFileWatcher()
        {
            try
            {
                _fileWatcher = new FileSystemWatcher
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName
                };
                
                _fileWatcher.Changed += OnFileChanged;
                _fileWatcher.Created += OnFileChanged;
                _fileWatcher.Deleted += OnFileChanged;
                _fileWatcher.Renamed += OnFileRenamed;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to initialize file system watcher for project cache");
            }
        }

        private async void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (IsProjectRelatedFile(e.FullPath))
            {
                await HandleProjectChangeAsync(e.FullPath);
            }
        }

        private async void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            if (IsProjectRelatedFile(e.FullPath) || IsProjectRelatedFile(e.OldFullPath))
            {
                await HandleProjectChangeAsync(e.FullPath);
                await HandleProjectChangeAsync(e.OldFullPath);
            }
        }

        private static bool IsProjectRelatedFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                ".csproj" => true,
                ".vbproj" => true,
                ".fsproj" => true,
                ".cs" => true,
                ".vb" => true,
                ".fs" => true,
                ".props" => true,
                ".targets" => true,
                _ => false
            };
        }

        private async Task HandleProjectChangeAsync(string filePath)
        {
            try
            {
                var projectPath = FindProjectForFile(filePath);
                if (!string.IsNullOrEmpty(projectPath))
                {
                    await InvalidateProjectAsync(projectPath!);
                    IncrementChangeDetection();
                    _logger?.LogDebug("Project change detected: {FilePath} -> {ProjectPath}", filePath, projectPath);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to handle project change: {FilePath}", filePath);
            }
        }

        private string? FindProjectForFile(string filePath)
        {
            if (filePath.EndsWith(".csproj") || filePath.EndsWith(".vbproj") || filePath.EndsWith(".fsproj"))
                return filePath;

            var directory = Path.GetDirectoryName(filePath);
            while (!string.IsNullOrEmpty(directory))
            {
                var projectFiles = Directory.GetFiles(directory, "*.csproj")
                    .Concat(Directory.GetFiles(directory, "*.vbproj"))
                    .Concat(Directory.GetFiles(directory, "*.fsproj"));
                
                var projectFile = projectFiles.FirstOrDefault();
                if (projectFile != null)
                    return projectFile;
                
                directory = Path.GetDirectoryName(directory);
            }

            return null;
        }

        private async Task<bool> IsProjectEntryValidAsync(ProjectCacheEntry entry, CancellationToken cancellationToken)
        {
            try
            {
                if (!File.Exists(entry.ProjectPath))
                    return false;

                var currentHash = await ComputeProjectContentHashAsync(entry.ProjectPath, cancellationToken);
                return currentHash == entry.ContentHash;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to validate project entry: {ProjectPath}", entry.ProjectPath);
                return false;
            }
        }

        private Task<string> ComputeProjectContentHashAsync(string projectPath, CancellationToken cancellationToken)
        {
            try
            {
                var projectInfo = new FileInfo(projectPath);
                var hashInput = $"{projectPath}|{projectInfo.LastWriteTimeUtc:O}|{projectInfo.Length}";
                
                // Include source files in hash for more sensitive change detection
                var projectDirectory = Path.GetDirectoryName(projectPath);
                if (!string.IsNullOrEmpty(projectDirectory) && Directory.Exists(projectDirectory))
                {
                    var sourceFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(projectDirectory, "*.vb", SearchOption.AllDirectories))
                        .OrderBy(f => f);
                    
                    foreach (var sourceFile in sourceFiles.Take(50)) // Limit to avoid excessive hashing
                    {
                        var fileInfo = new FileInfo(sourceFile);
                        hashInput += $"|{sourceFile}|{fileInfo.LastWriteTimeUtc:O}";
                    }
                }

                return Task.FromResult(ComputeHash(hashInput));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to compute project content hash: {ProjectPath}", projectPath);
                return Task.FromResult(ComputeHash($"{projectPath}|{DateTime.UtcNow:O}")); // Fallback
            }
        }

        private static Dictionary<string, object> ParseProjectProperties(string projectContent)
        {
            var properties = new Dictionary<string, object>();
            
            // Simple extraction of common properties
            // This is a basic implementation - could be enhanced with proper MSBuild parsing
            
            if (projectContent.Contains("<TargetFramework>"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    projectContent, @"<TargetFramework>(.*?)</TargetFramework>");
                if (match.Success)
                    properties["TargetFramework"] = match.Groups[1].Value;
            }

            if (projectContent.Contains("<OutputType>"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    projectContent, @"<OutputType>(.*?)</OutputType>");
                if (match.Success)
                    properties["OutputType"] = match.Groups[1].Value;
            }

            return properties;
        }

        private List<ProjectReference> DiscoverProjectReferences(string projectPath)
        {
            var references = new List<ProjectReference>();
            
            try
            {
                var projectContent = File.ReadAllText(projectPath);
                var projectDirectory = Path.GetDirectoryName(projectPath) ?? "";
                
                // Extract project references using regex (simple implementation)
                var matches = System.Text.RegularExpressions.Regex.Matches(
                    projectContent, @"<ProjectReference\s+Include=""([^""]+)""");
                
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var referencePath = match.Groups[1].Value;
                    var fullPath = Path.GetFullPath(Path.Combine(projectDirectory, referencePath));
                    
                    references.Add(new ProjectReference
                    {
                        ProjectPath = fullPath,
                        RelativePath = referencePath
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to discover project references: {ProjectPath}", projectPath);
            }
            
            return references;
        }

        private async Task StartTrackingProjectAsync(string projectPath, CancellationToken cancellationToken)
        {
            await _trackerLock.WaitAsync(cancellationToken);
            try
            {
                if (!_changeTrackers.ContainsKey(projectPath))
                {
                    _changeTrackers[projectPath] = new ProjectChangeTracker
                    {
                        ProjectPath = projectPath,
                        LastAccessed = DateTime.UtcNow,
                        ChangeCount = 0
                    };
                }
                else
                {
                    _changeTrackers[projectPath].LastAccessed = DateTime.UtcNow;
                }
            }
            finally
            {
                _trackerLock.Release();
            }
        }

        private async Task StopTrackingProjectAsync(string projectPath, CancellationToken cancellationToken)
        {
            await _trackerLock.WaitAsync(cancellationToken);
            try
            {
                _changeTrackers.Remove(projectPath);
            }
            finally
            {
                _trackerLock.Release();
            }
        }

        private async Task ClearAllTrackersAsync(CancellationToken cancellationToken)
        {
            await _trackerLock.WaitAsync(cancellationToken);
            try
            {
                _changeTrackers.Clear();
            }
            finally
            {
                _trackerLock.Release();
            }
        }

        private async Task CleanupStaleTrackersAsync(CancellationToken cancellationToken)
        {
            await _trackerLock.WaitAsync(cancellationToken);
            try
            {
                var staleThreshold = DateTime.UtcNow.AddDays(-7);
                var staleTrackers = _changeTrackers
                    .Where(kvp => kvp.Value.LastAccessed < staleThreshold)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var staleTracker in staleTrackers)
                {
                    _changeTrackers.Remove(staleTracker);
                }

                if (staleTrackers.Count > 0)
                {
                    _logger?.LogDebug("Cleaned up {Count} stale project trackers", staleTrackers.Count);
                }
            }
            finally
            {
                _trackerLock.Release();
            }
        }

        private static string GenerateProjectCacheKey(string projectPath, string? targetFramework)
        {
            var keyComponents = new List<string> { projectPath };
            if (!string.IsNullOrEmpty(targetFramework))
                keyComponents.Add(targetFramework!);
            
            var combinedKey = string.Join("|", keyComponents);
            return ComputeHash(combinedKey);
        }

        private static string ComputeHash(string input)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
            // Simple relative path implementation for older .NET frameworks
            var baseUri = new Uri(basePath.EndsWith(Path.DirectorySeparatorChar.ToString()) ? basePath : basePath + Path.DirectorySeparatorChar);
            var fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
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

        private void IncrementInvalidation(int count = 1)
        {
            lock (_statsLock)
            {
                _statistics.InvalidationCount += count;
            }
        }

        private void IncrementStore()
        {
            lock (_statsLock)
            {
                _statistics.StoreCount++;
            }
        }

        private void IncrementChangeDetection()
        {
            lock (_statsLock)
            {
                _statistics.ChangeDetectionCount++;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ProjectCacheManager));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _fileWatcher?.Dispose();
            _cache?.Dispose();
            _trackerLock?.Dispose();
        }
    }

    /// <summary>
    /// Represents cached project metadata and analysis results.
    /// </summary>
    public class ProjectCacheEntry
    {
        public string ProjectPath { get; set; } = string.Empty;
        public string TargetFramework { get; set; } = string.Empty;
        public HashSet<string> ReferencedAssemblies { get; set; } = new();
        public List<string> SourceFiles { get; set; } = new();
        public Dictionary<string, object> ProjectProperties { get; set; } = new();
        public List<ProjectReference> ProjectReferences { get; set; } = new();
        public DateTime LastAnalyzed { get; set; }
        public string ContentHash { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a reference to another project.
    /// </summary>
    public class ProjectReference
    {
        public string ProjectPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Tracks changes for a specific project.
    /// </summary>
    internal class ProjectChangeTracker
    {
        public string ProjectPath { get; set; } = string.Empty;
        public DateTime LastAccessed { get; set; }
        public int ChangeCount { get; set; }
    }

    /// <summary>
    /// Statistics for project cache performance and usage.
    /// </summary>
    public class ProjectCacheStatistics
    {
        public int TotalEntries { get; set; }
        public long TotalCompressedSize { get; set; }
        public long TotalUncompressedSize { get; set; }
        public long AverageCompressionRatio { get; set; }
        public int HitCount { get; set; }
        public int MissCount { get; set; }
        public int InvalidationCount { get; set; }
        public int StoreCount { get; set; }
        public int ChangeDetectionCount { get; set; }
        public double HitRatio { get; set; }
        public DateTime LastMaintenanceRun { get; set; }
        public int TrackedProjectsCount { get; set; }

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