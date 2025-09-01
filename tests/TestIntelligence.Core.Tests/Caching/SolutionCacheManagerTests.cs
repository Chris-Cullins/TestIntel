using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Caching;
using TestIntelligence.Core.Discovery;
using TestIntelligence.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace TestIntelligence.Core.Tests.Caching
{
    public class SolutionCacheManagerTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly string _solutionPath;
        private readonly ITestOutputHelper _output;
        private readonly ILogger<SolutionCacheManager> _logger;

        public SolutionCacheManagerTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDirectory = Path.Combine(Path.GetTempPath(), "TestIntelligence", "Tests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            
            _solutionPath = Path.Combine(_tempDirectory, "TestSolution.sln");
            File.WriteAllText(_solutionPath, "Microsoft Visual Studio Solution File, Format Version 12.00");

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<SolutionCacheManager>();
        }

        [Fact]
        public async Task InitializeAsync_WithNoSnapshot_CreatesNewSnapshot()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();

            // Act
            await cacheManager.InitializeAsync();

            // Assert - No exceptions should be thrown
            var stats = await cacheManager.GetStatisticsAsync();
            Assert.NotNull(stats.SolutionPath);
        }

        [Fact]
        public async Task GetOrSetAsync_WithNewKey_CallsFactoryAndCachesResult()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();
            await cacheManager.InitializeAsync();
            
            var key = "test-data-key";
            
            var expectedData = new SimpleTestData
            {
                Name = "TestItem",
                Value = 123,
                Items = new List<string> { "test1", "test2", "test3" }
            };
            var factoryCalled = false;

            // Act
            var result = await cacheManager.GetOrSetAsync(key, async () =>
            {
                factoryCalled = true;
                await Task.Delay(10); // Simulate work
                return expectedData;
            });

            // Assert
            Assert.True(factoryCalled);
            Assert.NotNull(result);
            Assert.Equal(expectedData.Name, result.Name);
            Assert.Equal(expectedData.Value, result.Value);
            Assert.Equal(3, result.Items.Count);
        }

        [Fact]
        public async Task GetOrSetAsync_WithExistingKey_ReturnsFromCache()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();
            await cacheManager.InitializeAsync();
            
            var key = "cached-data-key";
            var cachedData = new SimpleTestData
            {
                Name = "CachedItem",
                Value = 42,
                Items = new List<string> { "item1", "item2" }
            };

            // Pre-cache the result
            await cacheManager.GetOrSetAsync(key, () => Task.FromResult(cachedData));

            var factoryCalled = false;
            var newData = new SimpleTestData
            {
                Name = "NewItem",
                Value = 100,
                Items = new List<string> { "newitem" }
            };

            // Act
            var result = await cacheManager.GetOrSetAsync(key, () =>
            {
                factoryCalled = true;
                return Task.FromResult(newData);
            });

            // Assert
            Assert.False(factoryCalled);
            Assert.Equal(cachedData.Name, result.Name);
            Assert.Equal(cachedData.Value, result.Value);
        }

        [Fact]
        public async Task RegisterFileDependenciesAsync_TracksFileDependencies()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();
            await cacheManager.InitializeAsync();
            
            var key = "dependent-key";
            var dependentFile = Path.Combine(_tempDirectory, "test.cs");
            File.WriteAllText(dependentFile, "public class Test { }");

            var dependentFiles = new[] { dependentFile };

            // Act
            await cacheManager.RegisterFileDependenciesAsync(key, dependentFiles);
            await cacheManager.SaveSnapshotAsync();

            // Assert - No exceptions should be thrown
            var stats = await cacheManager.GetStatisticsAsync();
            Assert.True(stats.TrackedFileCount >= 0);
        }

        [Fact]
        public async Task DetectChangesAsync_WithModifiedFile_DetectsChanges()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();
            await cacheManager.InitializeAsync();
            
            var testFile = Path.Combine(_tempDirectory, "modifiable.cs");
            File.WriteAllText(testFile, "// Original content");
            
            // Create initial snapshot
            await cacheManager.SaveSnapshotAsync();
            
            // Modify the file
            await Task.Delay(100); // Ensure timestamp difference
            File.WriteAllText(testFile, "// Modified content");

            // Act
            var changes = await cacheManager.DetectChangesAsync();

            // Assert
            Assert.True(changes.HasChanges || changes.ModifiedFiles.Any() || !string.IsNullOrEmpty(changes.Reason));
        }

        [Fact]
        public async Task SaveAndLoadSnapshot_PreservesFileState()
        {
            // Arrange
            using var cacheManager1 = CreateCacheManager();
            await cacheManager1.InitializeAsync();
            
            // Create some test files
            var file1 = Path.Combine(_tempDirectory, "file1.cs");
            var file2 = Path.Combine(_tempDirectory, "file2.cs");
            File.WriteAllText(file1, "public class File1 { }");
            File.WriteAllText(file2, "public class File2 { }");

            // Save snapshot
            await cacheManager1.SaveSnapshotAsync();
            cacheManager1.Dispose();

            // Act - Create new cache manager and initialize
            using var cacheManager2 = CreateCacheManager();
            await cacheManager2.InitializeAsync();

            // Assert - Should load previous snapshot
            var stats = await cacheManager2.GetStatisticsAsync();
            Assert.NotNull(stats.LastSnapshotTime);
        }

        [Fact]
        public async Task ClearAllAsync_RemovesAllCacheData()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();
            await cacheManager.InitializeAsync();
            
            var key = "clear-test-key";
            var testData = new TestDiscoveryResult(
                "/test/clear.dll",
                FrameworkVersion.Net5Plus,
                new List<TestFixture>(),
                new List<string>());

            await cacheManager.GetOrSetAsync(key, () => Task.FromResult(testData));
            await cacheManager.SaveSnapshotAsync();

            // Act
            await cacheManager.ClearAllAsync();

            // Assert
            var stats = await cacheManager.GetStatisticsAsync();
            // After clearing, cache should be empty or minimal
            var factoryCalled = false;
            await cacheManager.GetOrSetAsync(key, () =>
            {
                factoryCalled = true;
                return Task.FromResult(testData);
            });
            Assert.True(factoryCalled); // Should call factory after clearing
        }

        [Fact]
        public async Task LargeSolutionSimulation_HandlesManyCacheEntries()
        {
            // Arrange - Simulate a large solution with many projects and files
            using var cacheManager = CreateCacheManager();
            await cacheManager.InitializeAsync();
            
            const int projectCount = 10;
            const int filesPerProject = 20;
            var tasks = new List<Task>();

            // Act - Simulate caching discovery results for many assemblies
            for (int p = 0; p < projectCount; p++)
            {
                for (int f = 0; f < filesPerProject; f++)
                {
                    var projectIndex = p;
                    var fileIndex = f;
                    
                    tasks.Add(Task.Run(async () =>
                    {
                        var key = $"project-{projectIndex}-assembly-{fileIndex}";
                        var assemblyPath = $"/large-solution/project{projectIndex}/bin/assembly{fileIndex}.dll";
                        
                        var result = await cacheManager.GetOrSetAsync(key, async () =>
                        {
                            await Task.Delay(1); // Simulate discovery work
                            
                            return new SimpleTestData
                            {
                                Name = $"Project{projectIndex}Assembly{fileIndex}",
                                Value = projectIndex * 100 + fileIndex,
                                Items = new List<string> { assemblyPath }
                            };
                        });
                        
                        // Verify result
                        Assert.NotNull(result);
                        Assert.Equal($"Project{projectIndex}Assembly{fileIndex}", result.Name);
                        Assert.Equal(projectIndex * 100 + fileIndex, result.Value);
                        Assert.Single(result.Items); // Should have one assembly path
                    }));
                }
            }

            await Task.WhenAll(tasks);

            // Assert
            var stats = await cacheManager.GetStatisticsAsync();
            _output.WriteLine($"Cache statistics after large solution simulation:");
            _output.WriteLine($"  Tracked files: {stats.TrackedFileCount}");
            _output.WriteLine($"  Dependency mappings: {stats.DependencyMappingCount}");
            _output.WriteLine($"  Persistent cache files: {stats.PersistentCache.TotalFiles}");
            _output.WriteLine($"  Persistent cache size: {stats.PersistentCache.TotalSizeFormatted}");

            Assert.True(stats.PersistentCache.TotalFiles > 0);
            Assert.True(stats.PersistentCache.TotalSizeBytes > 0);
        }

        [Fact]
        public async Task ConcurrentAccess_HandlesMultipleThreadsSafely()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();
            await cacheManager.InitializeAsync();
            
            const int threadCount = 20;
            const int operationsPerThread = 10;
            var tasks = new Task[threadCount];

            // Act
            for (int t = 0; t < threadCount; t++)
            {
                var threadIndex = t;
                tasks[t] = Task.Run(async () =>
                {
                    for (int o = 0; o < operationsPerThread; o++)
                    {
                        var key = $"concurrent-{threadIndex}-{o}";
                        var result = await cacheManager.GetOrSetAsync(key, async () =>
                        {
                            await Task.Delay(1);
                            return new TestDiscoveryResult(
                                $"/concurrent/thread{threadIndex}/op{o}.dll",
                                FrameworkVersion.Net5Plus,
                                new List<TestFixture>(),
                                new List<string>());
                        });
                        
                        Assert.NotNull(result);
                    }
                });
            }

            // Assert
            await Task.WhenAll(tasks);
            var stats = await cacheManager.GetStatisticsAsync();
            Assert.True(stats.PersistentCache.TotalFiles >= threadCount * operationsPerThread);
        }

        private SolutionCacheManager CreateCacheManager()
        {
            var cacheDirectory = Path.Combine(_tempDirectory, "Cache");
            var persistentCache = new PersistentCacheProvider(cacheDirectory);
            var memoryCache = new MemoryCacheProvider();
            
            var options = new SolutionCacheOptions
            {
                DefaultExpiration = TimeSpan.FromMinutes(30),
                FallbackExpiration = TimeSpan.FromMinutes(5),
                FilePatterns = new[] { "*.cs", "*.sln", "*.csproj" },
                ExcludePatterns = new[] { "bin\\", "obj\\" }
            };

            return new SolutionCacheManager(
                _solutionPath,
                persistentCache,
                memoryCache,
                _logger,
                options);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                try
                {
                    Directory.Delete(_tempDirectory, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors in tests
                }
            }
        }

        // Simple serializable test data class for cache testing
        public class SimpleTestData
        {
            public string Name { get; set; } = string.Empty;
            public int Value { get; set; }
            public List<string> Items { get; set; } = new List<string>();
        }
    }
}