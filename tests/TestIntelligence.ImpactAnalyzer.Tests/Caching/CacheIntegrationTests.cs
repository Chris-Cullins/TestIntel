using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Caching;
using TestIntelligence.ImpactAnalyzer.Caching;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Caching
{
    public class CacheIntegrationTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly string _solutionPath;
        private readonly List<string> _projectPaths;
        private readonly ILogger<CallGraphCache> _logger;

        public CacheIntegrationTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "CacheIntegrationTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);

            _solutionPath = Path.Combine(_tempDirectory, "TestSolution.sln");
            _projectPaths = new List<string>();
            _logger = new TestLogger<CallGraphCache>();

            SetupTestSolution();
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        [Fact]
        public async Task CrossLayerCaching_WorksCorrectlyAcrossAllCacheLayers()
        {
            // Arrange
            using var assemblyMetadataCache = CreateAssemblyMetadataCache();
            using var callGraphCache = CreateCallGraphCache();
            using var solutionCacheManager = CreateSolutionCacheManager();

            var assemblies = new[] { "System.dll", "mscorlib.dll" };

            // Act & Assert - Assembly Metadata Layer
            var assemblyData1 = await assemblyMetadataCache.GetOrCreateAsync("TestAssembly.dll", 
                () => Task.FromResult(CreateTestAssemblyMetadata()), CancellationToken.None);
            assemblyData1.Should().NotBeNull();

            var assemblyData2 = await assemblyMetadataCache.GetOrCreateAsync("TestAssembly.dll",
                () => Task.FromResult(CreateTestAssemblyMetadata()), CancellationToken.None);
            ReferenceEquals(assemblyData1, assemblyData2).Should().BeTrue("should return cached instance");

            // Act & Assert - Call Graph Layer
            var callGraph = CreateTestCallGraph();
            var reverseCallGraph = CreateTestReverseCallGraph();
            await callGraphCache.StoreCallGraphAsync(_projectPaths[0], assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(5));

            var cachedCallGraph = await callGraphCache.GetCallGraphAsync(_projectPaths[0], assemblies);
            cachedCallGraph.Should().NotBeNull();
            cachedCallGraph!.CallGraph.Should().HaveCount(callGraph.Count);

            // Act & Assert - Solution Cache Manager Layer
            var solutionStats1 = await solutionCacheManager.GetStatisticsAsync();
            var solutionStats2 = await solutionCacheManager.GetStatisticsAsync();
            solutionStats2.LastCacheSnapshot.Should().BeAfter(solutionStats1.LastCacheSnapshot.AddMilliseconds(-100));
        }

        [Fact]
        public async Task CacheInvalidation_PropagatesAcrossAllLayers()
        {
            // Arrange
            using var callGraphCache = CreateCallGraphCache();
            using var solutionCacheManager = CreateSolutionCacheManager();

            var assemblies = new[] { "System.dll", "mscorlib.dll" };
            var callGraph = CreateTestCallGraph();
            var reverseCallGraph = CreateTestReverseCallGraph();

            // Store initial data
            await callGraphCache.StoreCallGraphAsync(_projectPaths[0], assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(5));
            var initialCachedGraph = await callGraphCache.GetCallGraphAsync(_projectPaths[0], assemblies);
            initialCachedGraph.Should().NotBeNull();

            // Act - Modify project file to trigger invalidation
            await Task.Delay(100); // Ensure different timestamp
            File.WriteAllText(_projectPaths[0], "<Project><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");

            // Assert - Cache should be invalidated
            var invalidatedCachedGraph = await callGraphCache.GetCallGraphAsync(_projectPaths[0], assemblies);
            invalidatedCachedGraph.Should().BeNull("cache should be invalidated after project modification");

            var stats = await callGraphCache.GetStatisticsAsync();
            stats.InvalidationCount.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task CacheRecoveryFromCorruption_HandlesGracefully()
        {
            // Arrange
            using var callGraphCache = CreateCallGraphCache();
            var assemblies = new[] { "System.dll", "mscorlib.dll" };
            var callGraph = CreateTestCallGraph();
            var reverseCallGraph = CreateTestReverseCallGraph();

            // Store initial data
            await callGraphCache.StoreCallGraphAsync(_projectPaths[0], assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(5));

            // Simulate cache corruption by writing invalid data to cache files
            var cacheDirectory = Path.Combine(_tempDirectory, ".testintel-cache");
            if (Directory.Exists(cacheDirectory))
            {
                var cacheFiles = Directory.GetFiles(cacheDirectory, "*.cache", SearchOption.AllDirectories);
                foreach (var file in cacheFiles.Take(1)) // Corrupt just one file
                {
                    File.WriteAllText(file, "CORRUPTED_DATA");
                }
            }

            // Act & Assert - Cache should recover gracefully
            var result = await callGraphCache.GetCallGraphAsync(_projectPaths[0], assemblies);
            // The cache should either return null (indicating cache miss due to corruption)
            // or recover and return valid data, but should not throw an exception
            // This test primarily ensures no exceptions are thrown during corruption recovery
        }

        [Fact]
        public async Task LargeSolutionCachePerformance_MaintainsReasonablePerformance()
        {
            // Arrange
            const int projectCount = 10;
            const int methodsPerProject = 50;
            
            using var callGraphCache = CreateCallGraphCache();
            var assemblies = new[] { "System.dll", "mscorlib.dll", "System.Core.dll" };

            var projects = new List<string>();
            for (int i = 0; i < projectCount; i++)
            {
                var projectPath = Path.Combine(_tempDirectory, $"Project{i}.csproj");
                File.WriteAllText(projectPath, "<Project></Project>");
                projects.Add(projectPath);
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act - Store cache data for multiple projects
            var storeTasks = projects.Select(async (project, index) =>
            {
                var callGraph = CreateLargeTestCallGraph(methodsPerProject, $"Project{index}");
                var reverseCallGraph = CreateLargeTestReverseCallGraph(callGraph);
                await callGraphCache.StoreCallGraphAsync(project, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(1));
            });

            await Task.WhenAll(storeTasks);

            var storeTime = stopwatch.ElapsedMilliseconds;
            stopwatch.Restart();

            // Act - Retrieve cache data for all projects
            var retrieveTasks = projects.Select(project => callGraphCache.GetCallGraphAsync(project, assemblies));
            var results = await Task.WhenAll(retrieveTasks);

            var retrieveTime = stopwatch.ElapsedMilliseconds;

            // Assert - Performance should be reasonable
            storeTime.Should().BeLessThan(30000, "storing should complete within 30 seconds");
            retrieveTime.Should().BeLessThan(5000, "retrieval should complete within 5 seconds");
            
            results.Should().AllSatisfy(r => r.Should().NotBeNull());

            var stats = await callGraphCache.GetStatisticsAsync();
            stats.TotalEntries.Should().Be(projectCount);
            stats.HitCount.Should().Be(projectCount);
            stats.HitRatio.Should().BeGreaterThan(0.95);
        }

        [Fact]
        public async Task ConcurrentCacheAccess_HandlesMultipleClientsCorrectly()
        {
            // Arrange
            using var callGraphCache = CreateCallGraphCache();
            var assemblies = new[] { "System.dll", "mscorlib.dll" };
            const int concurrentOperations = 20;
            const int operationsPerClient = 5;

            // Act - Simulate multiple clients performing cache operations concurrently
            var tasks = new List<Task>();

            for (int clientId = 0; clientId < concurrentOperations; clientId++)
            {
                var client = clientId;
                tasks.Add(Task.Run(async () =>
                {
                    for (int op = 0; op < operationsPerClient; op++)
                    {
                        var projectPath = Path.Combine(_tempDirectory, $"ConcurrentProject_{client}_{op}.csproj");
                        File.WriteAllText(projectPath, $"<Project><PropertyGroup><ClientId>{client}</ClientId></PropertyGroup></Project>");

                        var callGraph = CreateTestCallGraph($"Client{client}_Op{op}");
                        var reverseCallGraph = CreateTestReverseCallGraph($"Client{client}_Op{op}");

                        // Store
                        await callGraphCache.StoreCallGraphAsync(projectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(1));

                        // Retrieve
                        var result = await callGraphCache.GetCallGraphAsync(projectPath, assemblies);
                        result.Should().NotBeNull();

                        // Small delay to increase concurrency overlap
                        await Task.Delay(Random.Shared.Next(1, 10));
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            var stats = await callGraphCache.GetStatisticsAsync();
            stats.TotalEntries.Should().Be(concurrentOperations * operationsPerClient);
            stats.StoreCount.Should().Be(concurrentOperations * operationsPerClient);
            stats.HitCount.Should().Be(concurrentOperations * operationsPerClient);
        }

        [Fact]
        public async Task MemoryPressureHandling_CleansUpCacheUnderPressure()
        {
            // Arrange
            var cacheOptions = new CompressedCacheOptions
            {
                MaxCacheSizeBytes = 1024 * 1024, // 1MB limit
                EnableBackgroundMaintenance = true,
                MaxMemoryUsageBytes = 512 * 1024 // 512KB memory limit
            };

            using var callGraphCache = new CallGraphCache(_tempDirectory, cacheOptions, _logger);
            var assemblies = new[] { "System.dll", "mscorlib.dll" };

            // Act - Fill cache beyond memory limit
            var projects = new List<string>();
            for (int i = 0; i < 20; i++) // Create enough projects to exceed memory limit
            {
                var projectPath = Path.Combine(_tempDirectory, $"MemoryProject{i}.csproj");
                File.WriteAllText(projectPath, "<Project></Project>");
                projects.Add(projectPath);

                var callGraph = CreateLargeTestCallGraph(100, $"Memory{i}"); // Large graphs
                var reverseCallGraph = CreateLargeTestReverseCallGraph(callGraph);
                
                await callGraphCache.StoreCallGraphAsync(projectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(1));
            }

            // Allow time for background maintenance
            await Task.Delay(2000);

            // Assert - Cache should have cleaned up some entries to stay within limits
            var stats = await callGraphCache.GetStatisticsAsync();
            stats.TotalCompressedSize.Should().BeLessOrEqualTo((long)(cacheOptions.MaxCacheSizeBytes * 1.1)); // Allow 10% overflow tolerance
        }

        [Fact]
        public async Task CacheCompression_AchievesSignificantSpaceSavings()
        {
            // Arrange
            using var callGraphCache = CreateCallGraphCache();
            var assemblies = new[] { "System.dll", "mscorlib.dll" };

            // Create a large, repetitive call graph (should compress well)
            var largeCallGraph = new Dictionary<string, HashSet<string>>();
            var largeReverseCallGraph = new Dictionary<string, HashSet<string>>();

            // Create patterns that should compress well
            for (int i = 0; i < 1000; i++)
            {
                var methodName = $"TestMethod_{i % 50}"; // Repetitive names
                var callees = new HashSet<string>();
                
                for (int j = 0; j < 5; j++)
                {
                    var callee = $"TargetMethod_{j % 10}"; // Repetitive targets
                    callees.Add(callee);
                    
                    if (!largeReverseCallGraph.ContainsKey(callee))
                        largeReverseCallGraph[callee] = new HashSet<string>();
                    largeReverseCallGraph[callee].Add(methodName);
                }
                
                largeCallGraph[methodName] = callees;
            }

            // Act
            await callGraphCache.StoreCallGraphAsync(_projectPaths[0], assemblies, largeCallGraph, largeReverseCallGraph, TimeSpan.FromSeconds(10));

            // Assert
            var stats = await callGraphCache.GetStatisticsAsync();
            stats.TotalUncompressedSize.Should().BeGreaterThan(stats.TotalCompressedSize);
            stats.AverageCompressionRatio.Should().BeGreaterThan(20, "should achieve at least 20% compression");
            
            var compressionRatio = (double)(stats.TotalUncompressedSize - stats.TotalCompressedSize) / stats.TotalUncompressedSize * 100;
            compressionRatio.Should().BeGreaterThan(20, "compression ratio should be significant");
        }

        private void SetupTestSolution()
        {
            // Create solution file
            File.WriteAllText(_solutionPath, @"
Microsoft Visual Studio Solution File, Format Version 12.00
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""Project1"", ""Project1.csproj"", ""{11111111-1111-1111-1111-111111111111}""
EndProject
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""Project2"", ""Project2.csproj"", ""{22222222-2222-2222-2222-222222222222}""
EndProject
");

            // Create test projects
            for (int i = 1; i <= 2; i++)
            {
                var projectPath = Path.Combine(_tempDirectory, $"Project{i}.csproj");
                File.WriteAllText(projectPath, $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>");
                _projectPaths.Add(projectPath);
            }
        }

        private AssemblyMetadataCache CreateAssemblyMetadataCache()
        {
            return new AssemblyMetadataCache(defaultExpiration: TimeSpan.FromMinutes(30));
        }

        private CallGraphCache CreateCallGraphCache()
        {
            var cacheOptions = new CompressedCacheOptions
            {
                MaxCacheSizeBytes = 50 * 1024 * 1024, // 50MB for tests
                EnableBackgroundMaintenance = false // Disable for tests
            };
            
            return new CallGraphCache(_tempDirectory, cacheOptions, _logger);
        }

        private SolutionCacheManager CreateSolutionCacheManager()
        {
            var logger = new TestLogger<SolutionCacheManager>();
            return new SolutionCacheManager(_solutionPath, logger: logger);
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

        private static Dictionary<string, HashSet<string>> CreateLargeTestCallGraph(int methodCount, string prefix = "Method")
        {
            var callGraph = new Dictionary<string, HashSet<string>>();
            
            for (int i = 0; i < methodCount; i++)
            {
                var method = $"{prefix}{i}";
                var callees = new HashSet<string>();
                
                // Each method calls 2-5 others
                var calleeCount = Random.Shared.Next(2, 6);
                for (int j = 0; j < calleeCount; j++)
                {
                    var callee = $"{prefix}{Random.Shared.Next(methodCount)}";
                    callees.Add(callee);
                }
                
                callGraph[method] = callees;
            }
            
            return callGraph;
        }

        private static Dictionary<string, HashSet<string>> CreateLargeTestReverseCallGraph(Dictionary<string, HashSet<string>> callGraph)
        {
            var reverseGraph = new Dictionary<string, HashSet<string>>();
            
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

        private static string CreateTestAssemblyMetadata()
        {
            return "TestAssemblyMetadata_" + Guid.NewGuid();
        }

        private class TestLogger<T> : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => false;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
        }
    }
}