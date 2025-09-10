using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace TestIntelligence.ImpactAnalyzer.Caching
{
    public interface IEnhancedCompilationCache : IDisposable
    {
        Task<Compilation?> GetOrCreateCompilationAsync(string key, Func<Task<Compilation>> factory, CancellationToken cancellationToken = default);
        Task<SemanticModel?> GetOrCreateSemanticModelAsync(string filePath, Func<Task<SemanticModel>> factory, CancellationToken cancellationToken = default);
        Task InvalidateAsync(string key, CancellationToken cancellationToken = default);
        Task ClearAsync(CancellationToken cancellationToken = default);
        CompilationCacheStatistics GetStatistics();
    }

    public class EnhancedCompilationCache : IEnhancedCompilationCache, IDisposable
    {
        private readonly IMemoryCache _memoryCache;
        private readonly IDistributedCache? _distributedCache;
        private readonly IFileSystemCache _fileSystemCache;
        private readonly ILogger<EnhancedCompilationCache> _logger;
        private readonly EnhancedCompilationCacheOptions _options;
        private readonly CompilationCacheStatistics _statistics = new();
        
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _compilationSemaphores = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _semanticModelSemaphores = new();
        private readonly Timer _cleanupTimer;

        public EnhancedCompilationCache(
            IMemoryCache memoryCache,
            IFileSystemCache fileSystemCache,
            ILogger<EnhancedCompilationCache> logger,
            IDistributedCache? distributedCache = null,
            EnhancedCompilationCacheOptions? options = null)
        {
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _fileSystemCache = fileSystemCache ?? throw new ArgumentNullException(nameof(fileSystemCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _distributedCache = distributedCache;
            _options = options ?? new EnhancedCompilationCacheOptions();

            // Setup periodic cleanup
            _cleanupTimer = new Timer(PerformCleanup, null, _options.CleanupInterval, _options.CleanupInterval);
        }

        public async Task<Compilation?> GetOrCreateCompilationAsync(
            string key,
            Func<Task<Compilation>> factory,
            CancellationToken cancellationToken = default)
        {
            var cacheKey = GetCompilationCacheKey(key);
            var semaphore = _compilationSemaphores.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));

            await semaphore.WaitAsync(cancellationToken);
            try
            {
                _statistics.TotalRequests++;

                // Level 1: Memory Cache
                if (_memoryCache.TryGetValue(cacheKey, out CachedCompilation? cached) && cached != null)
                {
                    if (IsCompilationValid(cached, key))
                    {
                        _statistics.MemoryCacheHits++;
                        _logger.LogDebug("Compilation cache hit (memory): {Key}", key);
                        return cached.Compilation;
                    }
                    else
                    {
                        _memoryCache.Remove(cacheKey);
                    }
                }

                // Level 2: Distributed Cache (if available)
                if (_distributedCache != null)
                {
                    try
                    {
                        var serializedData = await _distributedCache.GetStringAsync(cacheKey, cancellationToken);
                        if (!string.IsNullOrEmpty(serializedData))
                        {
                            var cacheEntry = JsonConvert.DeserializeObject<SerializableCompilationCacheEntry>(serializedData);
                            if (cacheEntry != null && IsValidCacheEntry(cacheEntry, key))
                            {
                                var compilation = await DeserializeCompilationAsync(cacheEntry, cancellationToken);
                                if (compilation != null)
                                {
                                    _statistics.DistributedCacheHits++;
                                    _logger.LogDebug("Compilation cache hit (distributed): {Key}", key);
                                    
                                    // Promote to memory cache
                                    var memoryCached = new CachedCompilation(compilation, File.GetLastWriteTimeUtc(key), key);
                                    _memoryCache.Set(cacheKey, memoryCached, _options.MemoryCacheExpiration);
                                    
                                    return compilation;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error accessing distributed cache for key: {Key}", key);
                    }
                }

                // Level 3: File System Cache
                try
                {
                    var fsData = await _fileSystemCache.GetAsync<SerializableCompilationCacheEntry>(cacheKey, cancellationToken);
                    if (fsData != null && IsValidCacheEntry(fsData, key))
                    {
                        var compilation = await DeserializeCompilationAsync(fsData, cancellationToken);
                        if (compilation != null)
                        {
                            _statistics.FileSystemCacheHits++;
                            _logger.LogDebug("Compilation cache hit (filesystem): {Key}", key);
                            
                            // Promote to memory cache and distributed cache
                            var memoryCached = new CachedCompilation(compilation, File.GetLastWriteTimeUtc(key), key);
                            _memoryCache.Set(cacheKey, memoryCached, _options.MemoryCacheExpiration);
                            
                            if (_distributedCache != null)
                            {
                                var serialized = JsonConvert.SerializeObject(fsData);
                                await _distributedCache.SetStringAsync(cacheKey, serialized, 
                                    new DistributedCacheEntryOptions
                                    {
                                        AbsoluteExpirationRelativeToNow = _options.DistributedCacheExpiration
                                    }, cancellationToken);
                            }
                            
                            return compilation;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error accessing filesystem cache for key: {Key}", key);
                }

                // Level 4: Create new compilation
                _statistics.CacheMisses++;
                _logger.LogDebug("Compilation cache miss, creating new: {Key}", key);
                
                var newCompilation = await factory();
                if (newCompilation != null)
                {
                    await CacheCompilationAsync(cacheKey, key, newCompilation, cancellationToken);
                }
                
                return newCompilation;
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task<SemanticModel?> GetOrCreateSemanticModelAsync(
            string filePath,
            Func<Task<SemanticModel>> factory,
            CancellationToken cancellationToken = default)
        {
            var cacheKey = GetSemanticModelCacheKey(filePath);
            var semaphore = _semanticModelSemaphores.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));

            await semaphore.WaitAsync(cancellationToken);
            try
            {
                // Check memory cache first
                if (_memoryCache.TryGetValue(cacheKey, out CachedSemanticModel? cached) && cached != null)
                {
                    if (IsSemanticModelValid(cached, filePath))
                    {
                        _logger.LogDebug("Semantic model cache hit: {FilePath}", filePath);
                        return cached.SemanticModel;
                    }
                    else
                    {
                        _memoryCache.Remove(cacheKey);
                    }
                }

                // Create new semantic model
                var newSemanticModel = await factory();
                if (newSemanticModel != null)
                {
                    var cachedModel = new CachedSemanticModel(newSemanticModel, File.GetLastWriteTimeUtc(filePath), filePath);
                    _memoryCache.Set(cacheKey, cachedModel, _options.SemanticModelCacheExpiration);
                }
                
                return newSemanticModel;
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task InvalidateAsync(string key, CancellationToken cancellationToken = default)
        {
            var cacheKey = GetCompilationCacheKey(key);
            
            _memoryCache.Remove(cacheKey);
            
            if (_distributedCache != null)
            {
                await _distributedCache.RemoveAsync(cacheKey, cancellationToken);
            }
            
            await _fileSystemCache.RemoveAsync(cacheKey, cancellationToken);
        }

        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            // Clear memory cache
            if (_memoryCache is MemoryCache mc)
            {
                mc.Compact(1.0); // Remove all entries
            }
            
            // Clear file system cache
            await _fileSystemCache.ClearAsync(cancellationToken);
            
            _logger.LogInformation("Compilation cache cleared");
        }

        public CompilationCacheStatistics GetStatistics()
        {
            return new CompilationCacheStatistics
            {
                TotalRequests = _statistics.TotalRequests,
                MemoryCacheHits = _statistics.MemoryCacheHits,
                DistributedCacheHits = _statistics.DistributedCacheHits,
                FileSystemCacheHits = _statistics.FileSystemCacheHits,
                CacheMisses = _statistics.CacheMisses
            };
        }

        private Task CacheCompilationAsync(string cacheKey, string key, Compilation compilation, CancellationToken cancellationToken)
        {
            var fileInfo = new FileInfo(key);
            var lastWriteTime = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.UtcNow;
            
            // Cache in memory
            var memoryCached = new CachedCompilation(compilation, lastWriteTime, key);
            _memoryCache.Set(cacheKey, memoryCached, _options.MemoryCacheExpiration);
            
            // Cache in background for filesystem and distributed cache
            _ = Task.Run(async () =>
            {
                try
                {
                    var serializableEntry = await SerializeCompilationAsync(compilation, lastWriteTime, key, cancellationToken);
                    
                    // Store in filesystem cache
                    await _fileSystemCache.SetAsync(cacheKey, serializableEntry, _options.FileSystemCacheExpiration, cancellationToken);
                    
                    // Store in distributed cache if available
                    if (_distributedCache != null)
                    {
                        var serialized = JsonConvert.SerializeObject(serializableEntry);
                        await _distributedCache.SetStringAsync(cacheKey, serialized,
                            new DistributedCacheEntryOptions
                            {
                                AbsoluteExpirationRelativeToNow = _options.DistributedCacheExpiration
                            }, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error caching compilation for key: {Key}", key);
                }
            }, cancellationToken);
            
            return Task.CompletedTask;
        }

        private Task<SerializableCompilationCacheEntry> SerializeCompilationAsync(
            Compilation compilation, 
            DateTime lastWriteTime, 
            string key, 
            CancellationToken cancellationToken)
        {
            // Capture enough information to reconstruct a usable compilation
            var sourceFiles = compilation.SyntaxTrees
                .Select(t => t.FilePath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct()
                .ToList();

            var sourceFileTimes = new Dictionary<string, DateTime>();
            foreach (var path in sourceFiles)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        sourceFileTimes[path] = File.GetLastWriteTimeUtc(path);
                    }
                }
                catch
                {
                    // ignore per-file failures; validation will handle it
                }
            }

            var referencePaths = new List<string>();
            foreach (var reference in compilation.References)
            {
                try
                {
                    if (reference is PortableExecutableReference pe)
                    {
                        // Prefer FilePath when available; fallback to Display if it looks like a path
                        var path = pe.FilePath;
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            var display = reference.Display;
                            if (!string.IsNullOrWhiteSpace(display) && File.Exists(display))
                            {
                                path = display;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                        {
                            referencePaths.Add(path);
                        }
                    }
                }
                catch
                {
                    // Best-effort collection of reference paths
                }
            }

            var result = new SerializableCompilationCacheEntry
            {
                Key = key,
                LastWriteTime = lastWriteTime,
                AssemblyName = compilation.AssemblyName ?? string.Empty,
                SyntaxTreeCount = compilation.SyntaxTrees.Count(),
                CreatedAt = DateTime.UtcNow,
                SourceFiles = sourceFiles,
                SourceFileTimes = sourceFileTimes,
                ReferencePaths = referencePaths,
                Language = compilation.Language
            };
            return Task.FromResult(result);
        }

        private Task<Compilation?> DeserializeCompilationAsync(
            SerializableCompilationCacheEntry cacheEntry, 
            CancellationToken cancellationToken)
        {
            try
            {
                // Validate source files still exist and have not changed
                if (cacheEntry.SourceFiles != null && cacheEntry.SourceFiles.Count > 0)
                {
                    foreach (var path in cacheEntry.SourceFiles)
                    {
                        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                        {
                            return Task.FromResult<Compilation?>(null);
                        }

                        if (cacheEntry.SourceFileTimes != null && cacheEntry.SourceFileTimes.TryGetValue(path, out var cachedTime))
                        {
                            var current = File.GetLastWriteTimeUtc(path);
                            if (current > cachedTime)
                            {
                                return Task.FromResult<Compilation?>(null);
                            }
                        }
                    }
                }
                else
                {
                    // Without source file info, don't attempt to restore
                    return Task.FromResult<Compilation?>(null);
                }

                // Build syntax trees
                var trees = new List<SyntaxTree>();
                foreach (var path in cacheEntry.SourceFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var text = File.ReadAllText(path);
                        if (cacheEntry.Language == LanguageNames.CSharp)
                        {
                            trees.Add(CSharpSyntaxTree.ParseText(text, path: path));
                        }
                        else
                        {
                            // Only C# supported currently
                            return Task.FromResult<Compilation?>(null);
                        }
                    }
                    catch
                    {
                        return Task.FromResult<Compilation?>(null);
                    }
                }

                // Build metadata references
                var references = new List<MetadataReference>();
                if (cacheEntry.ReferencePaths != null && cacheEntry.ReferencePaths.Count > 0)
                {
                    foreach (var r in cacheEntry.ReferencePaths)
                    {
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(r) && File.Exists(r))
                            {
                                references.Add(MetadataReference.CreateFromFile(r));
                            }
                        }
                        catch
                        {
                            // ignore invalid reference
                        }
                    }
                }

                if (references.Count == 0)
                {
                    references.AddRange(GetDefaultReferences());
                }

                // Create compilation
                if (cacheEntry.Language == LanguageNames.CSharp)
                {
                    var compilation = CSharpCompilation.Create(
                        string.IsNullOrWhiteSpace(cacheEntry.AssemblyName) ? "CachedAssembly" : cacheEntry.AssemblyName,
                        trees,
                        references);
                    return Task.FromResult<Compilation?>(compilation);
                }

                return Task.FromResult<Compilation?>(null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error deserializing compilation for key: {Key}", cacheEntry.Key);
                return Task.FromResult<Compilation?>(null);
            }
        }

        private bool IsCompilationValid(CachedCompilation cached, string key)
        {
            if (!File.Exists(key)) return false;
            
            var fileLastWrite = File.GetLastWriteTimeUtc(key);
            return cached.LastWriteTime >= fileLastWrite;
        }

        private bool IsSemanticModelValid(CachedSemanticModel cached, string filePath)
        {
            if (!File.Exists(filePath)) return false;
            
            var fileLastWrite = File.GetLastWriteTimeUtc(filePath);
            return cached.LastWriteTime >= fileLastWrite;
        }

        private bool IsValidCacheEntry(SerializableCompilationCacheEntry cacheEntry, string key)
        {
            // Validate primary key path when it points to a file
            try
            {
                if (File.Exists(key))
                {
                    var fileLastWrite = File.GetLastWriteTimeUtc(key);
                    if (cacheEntry.LastWriteTime < fileLastWrite)
                        return false;
                }
            }
            catch
            {
                return false;
            }

            // Validate all recorded source files
            if (cacheEntry.SourceFiles != null && cacheEntry.SourceFiles.Count > 0 && cacheEntry.SourceFileTimes != null)
            {
                foreach (var path in cacheEntry.SourceFiles)
                {
                    try
                    {
                        if (!File.Exists(path)) return false;
                        var current = File.GetLastWriteTimeUtc(path);
                        if (cacheEntry.SourceFileTimes.TryGetValue(path, out var cachedTime))
                        {
                            if (current > cachedTime) return false;
                        }
                        else
                        {
                            // No recorded time; be conservative
                            return false;
                        }
                    }
                    catch
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private string GetCompilationCacheKey(string key) => $"compilation:{ComputeStableHash(key)}";
        private string GetSemanticModelCacheKey(string filePath) => $"semantic:{ComputeStableHash(filePath)}";

        private static string ComputeStableHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
            var hash = sha.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", string.Empty);
        }

        private static IEnumerable<MetadataReference> GetDefaultReferences()
        {
            // Best-effort default references to allow basic compilation scenarios
            var refs = new List<MetadataReference>();
            try
            {
                var coreLib = typeof(object).GetTypeInfo().Assembly.Location;
                if (File.Exists(coreLib)) refs.Add(MetadataReference.CreateFromFile(coreLib));

                var assemblies = new[]
                {
                    typeof(Enumerable).GetTypeInfo().Assembly.Location, // System.Linq
                    typeof(Console).GetTypeInfo().Assembly.Location,     // System.Console
                    typeof(Uri).GetTypeInfo().Assembly.Location,         // System.Private.Uri / System
                };

                foreach (var loc in assemblies)
                {
                    if (!string.IsNullOrWhiteSpace(loc) && File.Exists(loc))
                    {
                        refs.Add(MetadataReference.CreateFromFile(loc));
                    }
                }
            }
            catch
            {
                // ignore
            }
            return refs;
        }

        private void PerformCleanup(object? state)
        {
            try
            {
                // Clean up semaphores for non-existent files
                var toRemove = new List<string>();
                foreach (var kvp in _compilationSemaphores)
                {
                    // Simple cleanup logic - in production you'd want more sophisticated cleanup
                    if (kvp.Value.CurrentCount == 1) // Not in use
                    {
                        toRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in toRemove.Take(100)) // Limit cleanup per cycle
                {
                    if (_compilationSemaphores.TryRemove(key, out var semaphore))
                    {
                        semaphore.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during cache cleanup");
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            
            foreach (var semaphore in _compilationSemaphores.Values)
            {
                semaphore.Dispose();
            }
            _compilationSemaphores.Clear();
            
            foreach (var semaphore in _semanticModelSemaphores.Values)
            {
                semaphore.Dispose();
            }
            _semanticModelSemaphores.Clear();
        }
    }

    public class CachedCompilation
    {
        public CachedCompilation(Compilation compilation, DateTime lastWriteTime, string key)
        {
            Compilation = compilation;
            LastWriteTime = lastWriteTime;
            Key = key;
        }

        public Compilation Compilation { get; }
        public DateTime LastWriteTime { get; }
        public string Key { get; }
    }

    public class CachedSemanticModel
    {
        public CachedSemanticModel(SemanticModel semanticModel, DateTime lastWriteTime, string filePath)
        {
            SemanticModel = semanticModel;
            LastWriteTime = lastWriteTime;
            FilePath = filePath;
        }

        public SemanticModel SemanticModel { get; }
        public DateTime LastWriteTime { get; }
        public string FilePath { get; }
    }

    public class SerializableCompilationCacheEntry
    {
        public string Key { get; set; } = string.Empty;
        public DateTime LastWriteTime { get; set; }
        public string AssemblyName { get; set; } = string.Empty;
        public int SyntaxTreeCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<string> SourceFiles { get; set; } = new();
        public Dictionary<string, DateTime> SourceFileTimes { get; set; } = new();
        public List<string> ReferencePaths { get; set; } = new();
        public string Language { get; set; } = LanguageNames.CSharp;
    }

    public class CompilationCacheStatistics
    {
        public long TotalRequests { get; set; }
        public long MemoryCacheHits { get; set; }
        public long DistributedCacheHits { get; set; }
        public long FileSystemCacheHits { get; set; }
        public long CacheMisses { get; set; }

        public double HitRatio => TotalRequests > 0 ? (double)(MemoryCacheHits + DistributedCacheHits + FileSystemCacheHits) / TotalRequests : 0;
        public double MemoryHitRatio => TotalRequests > 0 ? (double)MemoryCacheHits / TotalRequests : 0;
    }

    public class EnhancedCompilationCacheOptions
    {
        public TimeSpan MemoryCacheExpiration { get; set; } = TimeSpan.FromMinutes(30);
        public TimeSpan DistributedCacheExpiration { get; set; } = TimeSpan.FromHours(4);
        public TimeSpan FileSystemCacheExpiration { get; set; } = TimeSpan.FromDays(1);
        public TimeSpan SemanticModelCacheExpiration { get; set; } = TimeSpan.FromMinutes(15);
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(10);
    }
}
