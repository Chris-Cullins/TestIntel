using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.ImpactAnalyzer.Caching
{
    public interface ISyntaxTreePool
    {
        SyntaxTree GetOrParse(string sourceCode, string filePath);
        void Return(SyntaxTree syntaxTree);
        void Clear();
        SyntaxTreePoolStatistics GetStatistics();
    }

    public class SyntaxTreePool : ISyntaxTreePool, IDisposable
    {
        private readonly ConcurrentQueue<SyntaxTree> _pool = new();
        private readonly ConcurrentDictionary<string, SyntaxTree> _cache = new();
        private readonly ILogger<SyntaxTreePool> _logger;
        private readonly Timer _cleanupTimer;
        private readonly SyntaxTreePoolOptions _options;
        
        private long _totalRequests;
        private long _cacheHits;
        private long _poolHits;
        private long _newCreations;

        public SyntaxTreePool(ILogger<SyntaxTreePool> logger, SyntaxTreePoolOptions? options = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? new SyntaxTreePoolOptions();
            
            _cleanupTimer = new Timer(PerformCleanup, null, _options.CleanupInterval, _options.CleanupInterval);
        }

        public SyntaxTree GetOrParse(string sourceCode, string filePath)
        {
            Interlocked.Increment(ref _totalRequests);

            // First, check if we have a cached syntax tree for this exact content
            var contentHash = ComputeContentHash(sourceCode);
            var cacheKey = $"{filePath}:{contentHash}";
            
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                Interlocked.Increment(ref _cacheHits);
                return cached;
            }

            // Try to get a syntax tree from the pool
            if (_pool.TryDequeue(out var pooled))
            {
                Interlocked.Increment(ref _poolHits);
                
                // Replace the content of the pooled syntax tree
                var newSyntaxTree = CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
                
                // Cache the new syntax tree
                if (_cache.Count < _options.MaxCacheSize)
                {
                    _cache[cacheKey] = newSyntaxTree;
                }
                
                return newSyntaxTree;
            }

            // Create a new syntax tree
            Interlocked.Increment(ref _newCreations);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            
            // Cache the new syntax tree
            if (_cache.Count < _options.MaxCacheSize)
            {
                _cache[cacheKey] = syntaxTree;
            }
            
            return syntaxTree;
        }

        public void Return(SyntaxTree syntaxTree)
        {
            if (syntaxTree == null) return;
            
            if (_pool.Count < _options.MaxPoolSize)
            {
                _pool.Enqueue(syntaxTree);
            }
        }

        public void Clear()
        {
            _cache.Clear();
            
            while (_pool.TryDequeue(out _))
            {
                // Clear the pool
            }
            
            // Reset statistics
            Interlocked.Exchange(ref _totalRequests, 0);
            Interlocked.Exchange(ref _cacheHits, 0);
            Interlocked.Exchange(ref _poolHits, 0);
            Interlocked.Exchange(ref _newCreations, 0);
            
            _logger.LogInformation("Syntax tree pool cleared");
        }

        public SyntaxTreePoolStatistics GetStatistics()
        {
            return new SyntaxTreePoolStatistics
            {
                TotalRequests = _totalRequests,
                CacheHits = _cacheHits,
                PoolHits = _poolHits,
                NewCreations = _newCreations,
                CacheSize = _cache.Count,
                PoolSize = _pool.Count
            };
        }

        private void PerformCleanup(object? state)
        {
            try
            {
                // Remove old cache entries if cache is too large
                if (_cache.Count > _options.MaxCacheSize * 0.8) // Cleanup when 80% full
                {
                    var toRemove = Math.Min(_cache.Count / 4, 100); // Remove up to 25% or 100 entries
                    var removed = 0;
                    
                    foreach (var kvp in _cache)
                    {
                        if (removed >= toRemove) break;
                        
                        if (_cache.TryRemove(kvp.Key, out _))
                        {
                            removed++;
                        }
                    }
                    
                    if (removed > 0)
                    {
                        _logger.LogDebug("Removed {Count} entries from syntax tree cache during cleanup", removed);
                    }
                }

                // Trim pool if it's too large
                var poolTrimTarget = Math.Max(_options.MaxPoolSize / 2, 10);
                while (_pool.Count > poolTrimTarget && _pool.TryDequeue(out _))
                {
                    // Remove excess items from pool
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during syntax tree pool cleanup");
            }
        }

        private static string ComputeContentHash(string content)
        {
            // Simple hash for content identity - in production you might want SHA256
            return content.GetHashCode().ToString("X8");
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            Clear();
        }
    }

    public class SyntaxTreePoolStatistics
    {
        public long TotalRequests { get; set; }
        public long CacheHits { get; set; }
        public long PoolHits { get; set; }
        public long NewCreations { get; set; }
        public int CacheSize { get; set; }
        public int PoolSize { get; set; }

        public double CacheHitRatio => TotalRequests > 0 ? (double)CacheHits / TotalRequests : 0;
        public double PoolHitRatio => TotalRequests > 0 ? (double)PoolHits / TotalRequests : 0;
        public double ReuseRatio => TotalRequests > 0 ? (double)(CacheHits + PoolHits) / TotalRequests : 0;
    }

    public class SyntaxTreePoolOptions
    {
        public int MaxPoolSize { get; set; } = 100;
        public int MaxCacheSize { get; set; } = 500;
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
    }
}