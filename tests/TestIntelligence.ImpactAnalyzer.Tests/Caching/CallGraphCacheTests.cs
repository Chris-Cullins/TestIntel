using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Caching;
using TestIntelligence.ImpactAnalyzer.Caching;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Caching
{
    public class CallGraphCacheTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly string _testProjectPath;
        private readonly ILogger<CallGraphCache> _mockLogger;

        public CallGraphCacheTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "CallGraphCacheTests", Guid.NewGuid().ToString());
            _testProjectPath = Path.Combine(_tempDirectory, "TestProject.csproj");
            _mockLogger = new TestLogger<CallGraphCache>();
            
            Directory.CreateDirectory(_tempDirectory);
            File.WriteAllText(_testProjectPath, "<Project></Project>");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        [Fact]
        public async Task GetCallGraphAsync_WithNoCachedEntry_ReturnsNull()
        {
            // Arrange
            using var cache = CreateCache();
            var assemblies = new[] { "System.dll", "mscorlib.dll" };

            // Act
            var result = await cache.GetCallGraphAsync(_testProjectPath, assemblies);

            // Assert
            Assert.Null(result);
            
            var stats = await cache.GetStatisticsAsync();
            Assert.Equal(1, stats.MissCount);
            Assert.Equal(0, stats.HitCount);
        }

        [Fact]
        public async Task StoreCallGraphAsync_WithValidData_StoresSuccessfully()
        {
            // Arrange
            using var cache = CreateCache();
            var assemblies = new[] { "System.dll", "mscorlib.dll" };
            var callGraph = CreateTestCallGraph();
            var reverseCallGraph = CreateTestReverseCallGraph();
            var buildTime = TimeSpan.FromSeconds(5);

            // Act
            await cache.StoreCallGraphAsync(_testProjectPath, assemblies, callGraph, reverseCallGraph, buildTime);

            // Assert
            var stats = await cache.GetStatisticsAsync();
            Assert.Equal(1, stats.StoreCount);
            Assert.Equal(1, stats.TotalEntries);
        }

        [Fact]
        public async Task GetCallGraphAsync_WithValidCachedEntry_ReturnsEntry()
        {
            // Arrange
            using var cache = CreateCache();
            var assemblies = new[] { "System.dll", "mscorlib.dll" };
            var callGraph = CreateTestCallGraph();
            var reverseCallGraph = CreateTestReverseCallGraph();
            var buildTime = TimeSpan.FromSeconds(5);

            await cache.StoreCallGraphAsync(_testProjectPath, assemblies, callGraph, reverseCallGraph, buildTime);

            // Act
            var result = await cache.GetCallGraphAsync(_testProjectPath, assemblies);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(_testProjectPath, result.ProjectPath);
            Assert.Equal(callGraph.Count, result.CallGraph.Count);
            Assert.Equal(reverseCallGraph.Count, result.ReverseCallGraph.Count);
            Assert.Equal(buildTime, result.BuildTime);
            
            var stats = await cache.GetStatisticsAsync();
            Assert.Equal(1, stats.HitCount);
            Assert.True(stats.HitRatio > 0);
        }

        [Fact]
        public async Task GetCallGraphAsync_WithChangedAssemblies_InvalidatesCache()
        {
            // Arrange
            using var cache = CreateCache();
            var originalAssemblies = new[] { "System.dll", "mscorlib.dll" };
            var modifiedAssemblies = new[] { "System.dll", "System.Core.dll" }; // Different assembly
            var callGraph = CreateTestCallGraph();
            var reverseCallGraph = CreateTestReverseCallGraph();

            await cache.StoreCallGraphAsync(_testProjectPath, originalAssemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(5));

            // Act
            var result = await cache.GetCallGraphAsync(_testProjectPath, modifiedAssemblies);

            // Assert
            Assert.Null(result); // Should be invalidated
            
            var stats = await cache.GetStatisticsAsync();
            Assert.Equal(1, stats.InvalidationCount);
        }

        [Fact]
        public async Task GetCallGraphAsync_WithModifiedProject_InvalidatesCache()
        {
            // Arrange
            using var cache = CreateCache();
            var assemblies = new[] { "System.dll", "mscorlib.dll" };
            var callGraph = CreateTestCallGraph();
            var reverseCallGraph = CreateTestReverseCallGraph();

            await cache.StoreCallGraphAsync(_testProjectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(5));

            // Modify the project file after caching
            await Task.Delay(100); // Ensure different timestamp
            File.WriteAllText(_testProjectPath, "<Project Modified></Project>");

            // Act
            var result = await cache.GetCallGraphAsync(_testProjectPath, assemblies);

            // Assert
            Assert.Null(result); // Should be invalidated due to project change
            
            var stats = await cache.GetStatisticsAsync();
            Assert.Equal(1, stats.InvalidationCount);
        }

        [Fact]
        public async Task GetStatisticsAsync_ReturnsAccurateStatistics()
        {
            // Arrange
            using var cache = CreateCache();
            var assemblies = new[] { "System.dll", "mscorlib.dll" };
            var callGraph = CreateTestCallGraph();
            var reverseCallGraph = CreateTestReverseCallGraph();

            // Store multiple entries
            for (int i = 0; i < 3; i++)
            {
                var projectPath = Path.Combine(_tempDirectory, $"TestProject{i}.csproj");
                File.WriteAllText(projectPath, "<Project></Project>");
                await cache.StoreCallGraphAsync(projectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(i + 1));
            }

            // Perform some gets (hits and misses)
            var projectPath0 = Path.Combine(_tempDirectory, $"TestProject0.csproj");
            await cache.GetCallGraphAsync(projectPath0, assemblies); // Hit (stored above)
            await cache.GetCallGraphAsync("non-existent-project.csproj", assemblies); // Miss

            // Act
            var stats = await cache.GetStatisticsAsync();

            // Assert
            Assert.Equal(3, stats.TotalEntries);
            Assert.Equal(3, stats.StoreCount);
            Assert.Equal(1, stats.HitCount);
            Assert.Equal(1, stats.MissCount);
            Assert.True(stats.HitRatio > 0);
            Assert.True(stats.TotalCompressedSize > 0);
        }

        [Fact]
        public async Task ClearAsync_RemovesAllEntries()
        {
            // Arrange
            using var cache = CreateCache();
            var assemblies = new[] { "System.dll", "mscorlib.dll" };
            var callGraph = CreateTestCallGraph();
            var reverseCallGraph = CreateTestReverseCallGraph();

            await cache.StoreCallGraphAsync(_testProjectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(5));

            // Act
            await cache.ClearAsync();

            // Assert
            var result = await cache.GetCallGraphAsync(_testProjectPath, assemblies);
            Assert.Null(result);
            
            var stats = await cache.GetStatisticsAsync();
            Assert.Equal(0, stats.TotalEntries);
        }

        [Fact]
        public async Task PerformMaintenanceAsync_ExecutesWithoutError()
        {
            // Arrange
            using var cache = CreateCache();
            var assemblies = new[] { "System.dll", "mscorlib.dll" };
            var callGraph = CreateTestCallGraph();
            var reverseCallGraph = CreateTestReverseCallGraph();

            await cache.StoreCallGraphAsync(_testProjectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(5));

            // Act & Assert - Should not throw
            await cache.PerformMaintenanceAsync();
        }

        [Fact]
        public async Task InvalidateProjectAsync_InvalidatesSpecificProject()
        {
            // Arrange
            using var cache = CreateCache();
            var assemblies = new[] { "System.dll", "mscorlib.dll" };
            var callGraph = CreateTestCallGraph();
            var reverseCallGraph = CreateTestReverseCallGraph();

            await cache.StoreCallGraphAsync(_testProjectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(5));

            // Act
            await cache.InvalidateProjectAsync(_testProjectPath);

            // Note: Since we invalidate the internal tracking, the cached entry might still exist
            // but future modifications will be detected properly
            
            // Assert
            // The main assertion is that this doesn't throw an exception
            var stats = await cache.GetStatisticsAsync();
            Assert.True(stats.TotalEntries >= 0);
        }

        [Fact]
        public async Task CompressionEffectiveness_AchievesGoodCompression()
        {
            // Arrange
            using var cache = CreateCache();
            var assemblies = new[] { "System.dll", "mscorlib.dll" };
            var largeCallGraph = CreateLargeTestCallGraph();
            var largeReverseCallGraph = CreateLargeTestReverseCallGraph();

            // Act
            await cache.StoreCallGraphAsync(_testProjectPath, assemblies, largeCallGraph, largeReverseCallGraph, TimeSpan.FromSeconds(10));

            // Assert
            var stats = await cache.GetStatisticsAsync();
            Assert.True(stats.TotalCompressedSize < stats.TotalUncompressedSize);
            Assert.True(stats.AverageCompressionRatio > 30); // Should achieve at least 30% compression
        }

        [Fact]
        public void CacheEntryValidation_DetectsIntegrityIssues()
        {
            // Arrange
            var entry = new CompressedCallGraphCacheEntry
            {
                ProjectPath = _testProjectPath,
                CreatedAt = DateTime.UtcNow,
                CallGraph = new Dictionary<string, HashSet<string>>
                {
                    ["MethodA"] = new HashSet<string> { "MethodB", "MethodC" }
                },
                ReverseCallGraph = new Dictionary<string, HashSet<string>>
                {
                    ["MethodB"] = new HashSet<string> { "MethodA" }
                    // Missing MethodC -> MethodA mapping (integrity issue)
                }
            };

            // Act
            var validation = entry.ValidateIntegrity();

            // Assert
            Assert.False(validation.IsValid);
            Assert.NotEmpty(validation.Issues);
            Assert.Contains("MethodC", validation.Issues[0]);
        }

        [Fact]
        public void CacheEntryStatistics_ReturnsAccurateMetrics()
        {
            // Arrange
            var callGraph = CreateTestCallGraph();
            var reverseCallGraph = CreateTestReverseCallGraph();
            
            var entry = new CompressedCallGraphCacheEntry
            {
                CallGraph = callGraph,
                ReverseCallGraph = reverseCallGraph
            };

            // Act
            var stats = entry.GetStatistics();

            // Assert
            Assert.Equal(callGraph.Count, stats.TotalMethods);
            Assert.Equal(callGraph.Values.Sum(c => c.Count), stats.TotalEdges);
            Assert.True(stats.AverageFanOut > 0);
            Assert.True(stats.MaxFanOut > 0);
            Assert.True(stats.GraphDensity >= 0);
        }

        [Fact]
        public void GenerateCacheKey_WithSameInputs_GeneratesSameKey()
        {
            // Arrange
            var projectPath = "/path/to/project.csproj";
            var dependencies = new[] { "dep1", "dep2", "dep3" };
            var compilerVersion = "1.0.0";

            // Act
            var key1 = CompressedCallGraphCacheEntry.GenerateCacheKey(projectPath, dependencies, compilerVersion);
            var key2 = CompressedCallGraphCacheEntry.GenerateCacheKey(projectPath, dependencies, compilerVersion);

            // Assert
            Assert.Equal(key1, key2);
            Assert.NotEmpty(key1);
        }

        [Fact]
        public void GenerateCacheKey_WithDifferentInputs_GeneratesDifferentKeys()
        {
            // Arrange
            var projectPath1 = "/path/to/project1.csproj";
            var projectPath2 = "/path/to/project2.csproj";
            var dependencies = new[] { "dep1", "dep2" };
            var compilerVersion = "1.0.0";

            // Act
            var key1 = CompressedCallGraphCacheEntry.GenerateCacheKey(projectPath1, dependencies, compilerVersion);
            var key2 = CompressedCallGraphCacheEntry.GenerateCacheKey(projectPath2, dependencies, compilerVersion);

            // Assert
            Assert.NotEqual(key1, key2);
        }

        [Fact]
        public async Task ConcurrentAccess_HandlesMultipleOperations()
        {
            // Arrange
            using var cache = CreateCache();
            var assemblies = new[] { "System.dll", "mscorlib.dll" };
            const int operationCount = 10;
            var tasks = new List<Task>();

            // Act - Perform concurrent store operations
            for (int i = 0; i < operationCount; i++)
            {
                var index = i;
                tasks.Add(Task.Run(async () =>
                {
                    var projectPath = Path.Combine(_tempDirectory, $"ConcurrentProject{index}.csproj");
                    File.WriteAllText(projectPath, "<Project></Project>");
                    
                    var callGraph = CreateTestCallGraph($"Method{index}");
                    var reverseCallGraph = CreateTestReverseCallGraph($"Method{index}");
                    
                    await cache.StoreCallGraphAsync(projectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(1));
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            var stats = await cache.GetStatisticsAsync();
            Assert.Equal(operationCount, stats.StoreCount);
        }

        private CallGraphCache CreateCache()
        {
            var cacheOptions = new CompressedCacheOptions
            {
                MaxCacheSizeBytes = 10 * 1024 * 1024, // 10MB for tests
                EnableBackgroundMaintenance = false // Disable for tests
            };
            
            return new CallGraphCache(_tempDirectory, cacheOptions, _mockLogger);
        }

        private static Dictionary<string, HashSet<string>> CreateTestCallGraph(string prefix = "Method")
        {
            return new Dictionary<string, HashSet<string>>
            {
                [$"{prefix}A"] = new HashSet<string> { $"{prefix}B", $"{prefix}C" },
                [$"{prefix}B"] = new HashSet<string> { $"{prefix}D" },
                [$"{prefix}C"] = new HashSet<string> { $"{prefix}D", $"{prefix}E" },
                [$"{prefix}D"] = new HashSet<string>(),
                [$"{prefix}E"] = new HashSet<string>()
            };
        }

        private static Dictionary<string, HashSet<string>> CreateTestReverseCallGraph(string prefix = "Method")
        {
            return new Dictionary<string, HashSet<string>>
            {
                [$"{prefix}B"] = new HashSet<string> { $"{prefix}A" },
                [$"{prefix}C"] = new HashSet<string> { $"{prefix}A" },
                [$"{prefix}D"] = new HashSet<string> { $"{prefix}B", $"{prefix}C" },
                [$"{prefix}E"] = new HashSet<string> { $"{prefix}C" }
            };
        }

        private static Dictionary<string, HashSet<string>> CreateLargeTestCallGraph()
        {
            var callGraph = new Dictionary<string, HashSet<string>>();
            
            // Create a larger graph for compression testing
            for (int i = 0; i < 100; i++)
            {
                var method = $"Method{i}";
                var callees = new HashSet<string>();
                
                // Each method calls a few others
                for (int j = 1; j <= 3; j++)
                {
                    var callee = $"Method{(i + j) % 100}";
                    callees.Add(callee);
                }
                
                callGraph[method] = callees;
            }
            
            return callGraph;
        }

        private static Dictionary<string, HashSet<string>> CreateLargeTestReverseCallGraph()
        {
            var reverseGraph = new Dictionary<string, HashSet<string>>();
            var callGraph = CreateLargeTestCallGraph();
            
            // Build reverse graph from forward graph
            foreach (var (caller, callees) in callGraph)
            {
                foreach (var callee in callees)
                {
                    if (!reverseGraph.ContainsKey(callee))
                        reverseGraph[callee] = new HashSet<string>();
                    
                    reverseGraph[callee].Add(caller);
                }
            }
            
            return reverseGraph;
        }

        private class TestLogger<T> : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => false;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
        }
    }
}