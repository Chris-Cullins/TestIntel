using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Models;

namespace TestIntelligence.Core.Caching
{
    /// <summary>
    /// Manages persistent caching for entire solutions with diff-based invalidation.
    /// Tracks file changes and dependencies to efficiently invalidate cache entries.
    /// </summary>
    public class SolutionCacheManager : IDisposable
    {
        private readonly PersistentCacheProvider _persistentCache;
        private readonly ICacheProvider _fallbackCache;
        private readonly ILogger<SolutionCacheManager>? _logger;
        private readonly string _solutionPath;
        private readonly SolutionCacheOptions _options;
        private readonly SemaphoreSlim _cacheSemaphore;
        private volatile bool _disposed = false;

        private SolutionCacheSnapshot? _lastSnapshot;
        private readonly Dictionary<string, HashSet<string>> _fileDependencies = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new TypeIgnoreConverter() }
        };

        /// <summary>
        /// Custom JsonConverter that ignores System.Type properties during serialization
        /// </summary>
        private class TypeIgnoreConverter : JsonConverter<Type>
        {
            public override Type? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                // Skip reading the value
                reader.Skip();
                return null;
            }

            public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
            {
                // Write null instead of trying to serialize the Type
                writer.WriteNullValue();
            }
        }

        public SolutionCacheManager(
            string solutionPath,
            PersistentCacheProvider? persistentCache = null,
            ICacheProvider? fallbackCache = null,
            ILogger<SolutionCacheManager>? logger = null,
            SolutionCacheOptions? options = null)
        {
            _solutionPath = Path.GetFullPath(solutionPath);
            _persistentCache = persistentCache ?? new PersistentCacheProvider(GetDefaultCacheDirectory(_solutionPath), logger as ILogger<PersistentCacheProvider>);
            _fallbackCache = fallbackCache ?? new MemoryCacheProvider();
            _logger = logger;
            _options = options ?? new SolutionCacheOptions();
            _cacheSemaphore = new SemaphoreSlim(1, 1);

            _logger?.LogInformation("Solution cache manager initialized for: {SolutionPath}", _solutionPath);
        }

        /// <summary>
        /// Initializes the cache by loading the previous snapshot and checking for changes.
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _cacheSemaphore.WaitAsync(cancellationToken);
            try
            {
                // Load previous snapshot
                _lastSnapshot = await LoadSnapshotAsync(cancellationToken);
                
                if (_lastSnapshot != null)
                {
                    _logger?.LogInformation("Loaded cache snapshot with {FileCount} files from {SnapshotTime}", 
                        _lastSnapshot.FileHashes.Count, _lastSnapshot.CreatedAt);
                    
                    // Check for changes since last snapshot
                    var changes = await DetectChangesAsync(_lastSnapshot, cancellationToken);
                    if (changes.HasChanges)
                    {
                        await InvalidateChangedEntriesAsync(changes, cancellationToken);
                    }
                }
                else
                {
                    _logger?.LogInformation("No previous cache snapshot found, starting fresh");
                }
            }
            finally
            {
                _cacheSemaphore.Release();
            }
        }

        /// <summary>
        /// Gets or caches a value with automatic dependency tracking and invalidation.
        /// </summary>
        public async Task<T> GetOrSetAsync<T>(
            string key, 
            Func<Task<T>> factory, 
            IEnumerable<string>? dependentFiles = null,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default) where T : class
        {
            ThrowIfDisposed();

            var fullKey = GetFullCacheKey(key);
            
            // Track file dependencies if provided
            if (dependentFiles != null)
            {
                await RegisterFileDependenciesAsync(fullKey, dependentFiles, cancellationToken);
            }

            // Try persistent cache first
            var result = await _persistentCache.GetAsync<T>(fullKey, cancellationToken);
            if (result != null)
            {
                _logger?.LogDebug("Cache hit (persistent) for key: {Key}", key);
                return result;
            }

            // Try fallback cache
            result = await _fallbackCache.GetAsync<T>(fullKey, cancellationToken);
            if (result != null)
            {
                _logger?.LogDebug("Cache hit (fallback) for key: {Key}", key);
                
                // Promote to persistent cache
                await _persistentCache.SetAsync(fullKey, result, expiration ?? _options.DefaultExpiration, cancellationToken);
                return result;
            }

            // Cache miss - create new value
            _logger?.LogDebug("Cache miss for key: {Key}, creating new value", key);
            var newValue = await factory();
            
            if (newValue != null)
            {
                // Store in both caches
                await Task.WhenAll(
                    _persistentCache.SetAsync(fullKey, newValue, expiration ?? _options.DefaultExpiration, cancellationToken),
                    _fallbackCache.SetAsync(fullKey, newValue, expiration ?? _options.FallbackExpiration, cancellationToken)
                );
            }

            return newValue!;
        }

        /// <summary>
        /// Registers file dependencies for a cache key.
        /// </summary>
        public async Task RegisterFileDependenciesAsync(string key, IEnumerable<string> dependentFiles, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _cacheSemaphore.WaitAsync(cancellationToken);
            try
            {
                var fullKey = GetFullCacheKey(key);
                var normalizedFiles = new HashSet<string>(
                    dependentFiles
                        .Select(Path.GetFullPath)
                        .Where(File.Exists),
                    StringComparer.OrdinalIgnoreCase);

                _fileDependencies[fullKey] = normalizedFiles;
                
                _logger?.LogDebug("Registered {0} file dependencies for key: {1}", normalizedFiles.Count, key);
            }
            finally
            {
                _cacheSemaphore.Release();
            }
        }

        /// <summary>
        /// Creates and saves a snapshot of the current solution state.
        /// </summary>
        public async Task SaveSnapshotAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _cacheSemaphore.WaitAsync(cancellationToken);
            try
            {
                var snapshot = await CreateCurrentSnapshotAsync(cancellationToken);
                await SaveSnapshotAsync(snapshot, cancellationToken);
                
                _lastSnapshot = snapshot;
                _logger?.LogInformation("Saved cache snapshot with {FileCount} files", snapshot.FileHashes.Count);
            }
            finally
            {
                _cacheSemaphore.Release();
            }
        }

        /// <summary>
        /// Detects changes in the solution since the last snapshot.
        /// </summary>
        public async Task<SolutionChanges> DetectChangesAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (_lastSnapshot == null)
            {
                return new SolutionChanges { HasChanges = true, Reason = "No previous snapshot" };
            }

            return await DetectChangesAsync(_lastSnapshot, cancellationToken);
        }

        /// <summary>
        /// Clears all cached data and snapshots.
        /// </summary>
        public async Task ClearAllAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await Task.WhenAll(
                _persistentCache.ClearAsync(cancellationToken),
                _fallbackCache.ClearAsync(cancellationToken)
            );

            // Clear snapshot
            var snapshotPath = GetSnapshotPath();
            if (File.Exists(snapshotPath))
            {
                File.Delete(snapshotPath);
            }

            _lastSnapshot = null;
            _fileDependencies.Clear();

            _logger?.LogInformation("All cache data cleared");
        }

        /// <summary>
        /// Gets cache statistics.
        /// </summary>
        public async Task<SolutionCacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var persistentStats = await _persistentCache.GetStatisticsAsync(cancellationToken);
            
            return new SolutionCacheStatistics
            {
                SolutionPath = _solutionPath,
                PersistentCache = persistentStats,
                LastSnapshotTime = _lastSnapshot?.CreatedAt,
                TrackedFileCount = _lastSnapshot?.FileHashes.Count ?? 0,
                DependencyMappingCount = _fileDependencies.Count
            };
        }

        private async Task<SolutionCacheSnapshot?> LoadSnapshotAsync(CancellationToken cancellationToken)
        {
            var snapshotPath = GetSnapshotPath();
            
            if (!File.Exists(snapshotPath))
                return null;

            try
            {
                using var fileStream = new FileStream(snapshotPath, FileMode.Open, FileAccess.Read);
                var snapshot = await JsonSerializer.DeserializeAsync<SolutionCacheSnapshot>(fileStream, JsonOptions, cancellationToken);
                return snapshot;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load cache snapshot from: {SnapshotPath}", snapshotPath);
                return null;
            }
        }

        private async Task SaveSnapshotAsync(SolutionCacheSnapshot snapshot, CancellationToken cancellationToken)
        {
            var snapshotPath = GetSnapshotPath();
            var directory = Path.GetDirectoryName(snapshotPath);
            
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var fileStream = new FileStream(snapshotPath, FileMode.Create, FileAccess.Write);
            await JsonSerializer.SerializeAsync(fileStream, snapshot, JsonOptions, cancellationToken);
        }

        private async Task<SolutionCacheSnapshot> CreateCurrentSnapshotAsync(CancellationToken cancellationToken)
        {
            var fileHashes = new Dictionary<string, string>();
            var solutionDirectory = Path.GetDirectoryName(_solutionPath);
            
            if (string.IsNullOrEmpty(solutionDirectory))
                throw new InvalidOperationException("Invalid solution path");

            // Find all relevant files in the solution
            var patterns = _options.FilePatterns;
            var allFiles = new List<string>();

            foreach (var pattern in patterns)
            {
                try
                {
                    var files = Directory.GetFiles(solutionDirectory, pattern, SearchOption.AllDirectories)
                        .Where(f => !IsExcluded(f))
                        .ToArray();
                    allFiles.AddRange(files);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to enumerate files with pattern: {Pattern}", pattern);
                }
            }

            // Calculate hashes for all files
            var tasks = allFiles.Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(async file =>
                {
                    try
                    {
                        var hash = await CalculateFileHashAsync(file, cancellationToken);
                        return new KeyValuePair<string, string>(file, hash);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "Failed to hash file: {FilePath}", file);
                        return new KeyValuePair<string, string>(file, string.Empty);
                    }
                })
                .ToArray();

            var results = await Task.WhenAll(tasks);
            
            foreach (var result in results.Where(r => !string.IsNullOrEmpty(r.Value)))
            {
                fileHashes[result.Key] = result.Value;
            }

            return new SolutionCacheSnapshot
            {
                SolutionPath = _solutionPath,
                CreatedAt = DateTimeOffset.UtcNow,
                FileHashes = fileHashes,
                FileDependencies = new Dictionary<string, HashSet<string>>(_fileDependencies)
            };
        }

        private async Task<SolutionChanges> DetectChangesAsync(SolutionCacheSnapshot lastSnapshot, CancellationToken cancellationToken)
        {
            var currentSnapshot = await CreateCurrentSnapshotAsync(cancellationToken);
            var changes = new SolutionChanges();

            // Check for modified files
            foreach (var kvp in currentSnapshot.FileHashes)
            {
                var filePath = kvp.Key;
                var currentHash = kvp.Value;
                
                if (lastSnapshot.FileHashes.TryGetValue(filePath, out var lastHash))
                {
                    if (currentHash != lastHash)
                    {
                        changes.ModifiedFiles.Add(filePath);
                    }
                }
                else
                {
                    changes.AddedFiles.Add(filePath);
                }
            }

            // Check for deleted files
            foreach (var filePath in lastSnapshot.FileHashes.Keys)
            {
                if (!currentSnapshot.FileHashes.ContainsKey(filePath))
                {
                    changes.DeletedFiles.Add(filePath);
                }
            }

            changes.HasChanges = changes.ModifiedFiles.Any() || changes.AddedFiles.Any() || changes.DeletedFiles.Any();
            
            if (changes.HasChanges)
            {
                _logger?.LogInformation("Detected changes: {ModifiedCount} modified, {AddedCount} added, {DeletedCount} deleted", 
                    changes.ModifiedFiles.Count, changes.AddedFiles.Count, changes.DeletedFiles.Count);
            }

            return changes;
        }

        private async Task InvalidateChangedEntriesAsync(SolutionChanges changes, CancellationToken cancellationToken)
        {
            var invalidatedKeys = new HashSet<string>();
            var allChangedFiles = changes.ModifiedFiles.Concat(changes.AddedFiles).Concat(changes.DeletedFiles).ToList();

            // Direct dependency invalidation
            foreach (var changedFile in allChangedFiles)
            {
                foreach (var kvp in _fileDependencies)
                {
                    var cacheKey = kvp.Key;
                    var dependentFiles = kvp.Value;
                    
                    if (dependentFiles.Contains(changedFile, StringComparer.OrdinalIgnoreCase))
                    {
                        invalidatedKeys.Add(cacheKey);
                    }
                }
            }

            // Pattern-based invalidation for files without explicit dependencies tracked
            var patternInvalidatedKeys = await InvalidateCacheEntriesByPatternAsync(allChangedFiles, cancellationToken);
            foreach (var key in patternInvalidatedKeys)
            {
                invalidatedKeys.Add(key);
            }

            // Invalidate cache entries
            var invalidationTasks = invalidatedKeys.Select(async key =>
            {
                await Task.WhenAll(
                    _persistentCache.RemoveAsync(key, cancellationToken),
                    _fallbackCache.RemoveAsync(key, cancellationToken)
                );
                
                // Also remove the file dependency tracking for invalidated entries
                _fileDependencies.Remove(key);
            });

            await Task.WhenAll(invalidationTasks);

            if (invalidatedKeys.Count > 0)
            {
                _logger?.LogInformation("Invalidated {CacheEntryCount} cache entries due to file changes: {ChangedFiles}", 
                    invalidatedKeys.Count, string.Join(", ", allChangedFiles.Take(5)));
            }
        }

        private async Task<List<string>> InvalidateCacheEntriesByPatternAsync(List<string> changedFiles, CancellationToken cancellationToken)
        {
            var patternInvalidatedKeys = new List<string>();
            var cacheStats = await _persistentCache.GetStatisticsAsync(cancellationToken);
            
            // For changed project files, invalidate all related cache entries
            var changedProjects = changedFiles.Where(f => f.EndsWith(".csproj") || f.EndsWith(".vbproj") || f.EndsWith(".fsproj"));
            foreach (var projectFile in changedProjects)
            {
                var projectName = Path.GetFileNameWithoutExtension(projectFile);
                
                // Find cache keys that likely belong to this project
                foreach (var kvp in _fileDependencies)
                {
                    var cacheKey = kvp.Key;
                    if (cacheKey.IndexOf(projectName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        patternInvalidatedKeys.Add(cacheKey);
                    }
                }
            }
            
            // For changed source files, invalidate compilation and analysis caches
            var changedSourceFiles = changedFiles.Where(f => f.EndsWith(".cs") || f.EndsWith(".vb") || f.EndsWith(".fs"));
            if (changedSourceFiles.Any())
            {
                // Invalidate cache entries that are likely related to compilation or analysis
                foreach (var kvp in _fileDependencies)
                {
                    var cacheKey = kvp.Key;
                    if (cacheKey.IndexOf("compilation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        cacheKey.IndexOf("analysis", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        cacheKey.IndexOf("callgraph", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        patternInvalidatedKeys.Add(cacheKey);
                    }
                }
            }

            return patternInvalidatedKeys;
        }

        private async Task<string> CalculateFileHashAsync(string filePath, CancellationToken cancellationToken)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sha256 = SHA256.Create();
            var hashBytes = await Task.Run(() => sha256.ComputeHash(stream), cancellationToken);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        private bool IsExcluded(string filePath)
        {
            // Use manual relative path calculation for .NET Standard 2.0 compatibility
            var relativePath = GetRelativePath(_solutionPath, filePath);
            return _options.ExcludePatterns.Any(pattern => 
                relativePath.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string GetRelativePath(string basePath, string targetPath)
        {
            var baseUri = new Uri(basePath + Path.DirectorySeparatorChar);
            var targetUri = new Uri(targetPath);
            return baseUri.MakeRelativeUri(targetUri).ToString().Replace('/', Path.DirectorySeparatorChar);
        }

        private string GetFullCacheKey(string key)
        {
            return $"solution:{Path.GetFileName(_solutionPath)}:{key}";
        }

        private string GetSnapshotPath()
        {
            var cacheDir = GetDefaultCacheDirectory(_solutionPath);
            return Path.Combine(cacheDir, "solution-snapshot.json");
        }

        private static string GetDefaultCacheDirectory(string solutionPath)
        {
            var solutionName = Path.GetFileNameWithoutExtension(solutionPath);
            return Path.Combine(Path.GetTempPath(), "TestIntelligence", "SolutionCache", solutionName);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SolutionCacheManager));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cacheSemaphore?.Dispose();
            _persistentCache?.Dispose();
            _fallbackCache?.Dispose();
        }
    }

    /// <summary>
    /// Options for solution-level caching behavior.
    /// </summary>
    public class SolutionCacheOptions
    {
        public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromDays(7);
        public TimeSpan FallbackExpiration { get; set; } = TimeSpan.FromHours(1);
        
        public string[] FilePatterns { get; set; } = 
        {
            "*.cs", "*.csproj", "*.sln", "*.config", "*.json", "*.xml"
        };
        
        public string[] ExcludePatterns { get; set; } = 
        {
            "bin\\", "obj\\", ".vs\\", ".git\\", "packages\\", "node_modules\\"
        };
    }

    /// <summary>
    /// Snapshot of a solution's file state for change detection.
    /// </summary>
    public class SolutionCacheSnapshot
    {
        public string SolutionPath { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public Dictionary<string, string> FileHashes { get; set; } = new();
        public Dictionary<string, HashSet<string>> FileDependencies { get; set; } = new();
    }

    /// <summary>
    /// Represents detected changes in a solution.
    /// </summary>
    public class SolutionChanges
    {
        public bool HasChanges { get; set; }
        public string Reason { get; set; } = string.Empty;
        public List<string> ModifiedFiles { get; set; } = new();
        public List<string> AddedFiles { get; set; } = new();
        public List<string> DeletedFiles { get; set; } = new();
    }

    /// <summary>
    /// Statistics for solution-level caching.
    /// </summary>
    public class SolutionCacheStatistics
    {
        public string SolutionPath { get; set; } = string.Empty;
        public PersistentCacheStatistics PersistentCache { get; set; } = new();
        public DateTimeOffset? LastSnapshotTime { get; set; }
        public int TrackedFileCount { get; set; }
        public int DependencyMappingCount { get; set; }
    }
}