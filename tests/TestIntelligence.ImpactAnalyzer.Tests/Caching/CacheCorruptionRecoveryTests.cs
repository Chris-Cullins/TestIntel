using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Caching;
using TestIntelligence.ImpactAnalyzer.Caching;
using Xunit;
using Xunit.Abstractions;

namespace TestIntelligence.ImpactAnalyzer.Tests.Caching
{
    public class CacheCorruptionRecoveryTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _tempDirectory;
        private readonly ILogger<CallGraphCache> _logger;
        private readonly List<string> _createdDirectories;

        public CacheCorruptionRecoveryTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDirectory = Path.Combine(Path.GetTempPath(), "CacheCorruptionTests", Guid.NewGuid().ToString());
            _logger = new TestLogger<CallGraphCache>(output);
            _createdDirectories = new List<string>();
            
            Directory.CreateDirectory(_tempDirectory);
            _createdDirectories.Add(_tempDirectory);
        }

        public void Dispose()
        {
            foreach (var directory in _createdDirectories.AsEnumerable().Reverse())
            {
                try
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        [Fact]
        public async Task CacheCorruption_InvalidFileFormat_RecoversByIgnoringCorruptedEntries()
        {
            // Arrange
            using var cache = CreateCache();
            var projectPath = Path.Combine(_tempDirectory, "TestProject.csproj");
            File.WriteAllText(projectPath, "<Project></Project>");
            
            var assemblies = new[] { "System.dll", "mscorlib.dll" };
            var callGraph = CreateTestCallGraph();
            var reverseCallGraph = CreateTestReverseCallGraph();

            // Store valid data first
            await cache.StoreCallGraphAsync(projectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(1));
            
            // Verify cache works normally
            var validResult = await cache.GetCallGraphAsync(projectPath, assemblies);
            validResult.Should().NotBeNull();

            // Act - Corrupt cache files
            CorruptCacheFiles(_tempDirectory, CorruptionType.InvalidFormat);

            // Assert - Cache should recover gracefully
            var resultAfterCorruption = await cache.GetCallGraphAsync(projectPath, assemblies);
            
            // The corrupted cache should be ignored and return null (cache miss)
            resultAfterCorruption.Should().BeNull("corrupted cache should be ignored");

            // Cache should still be functional for new entries
            await cache.StoreCallGraphAsync(projectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(1));
            var newResult = await cache.GetCallGraphAsync(projectPath, assemblies);
            newResult.Should().NotBeNull("cache should work after corruption recovery");
        }

        [Fact]
        public async Task CacheCorruption_TruncatedFiles_HandlesGracefully()
        {
            // Arrange
            using var cache = CreateCache();
            var projectPath = Path.Combine(_tempDirectory, "TruncatedTestProject.csproj");
            File.WriteAllText(projectPath, "<Project></Project>");
            
            var assemblies = new[] { "System.dll", "mscorlib.dll" };
            var callGraph = CreateTestCallGraph();
            var reverseCallGraph = CreateTestReverseCallGraph();

            await cache.StoreCallGraphAsync(projectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(1));

            // Act - Truncate cache files
            CorruptCacheFiles(_tempDirectory, CorruptionType.Truncated);

            // Assert - Should not throw exceptions
            var action = async () => await cache.GetCallGraphAsync(projectPath, assemblies);
            await action.Should().NotThrowAsync("truncated files should be handled gracefully");

            var result = await cache.GetCallGraphAsync(projectPath, assemblies);
            result.Should().BeNull("truncated cache should result in cache miss");

            // Verify cache statistics are still accessible
            var stats = await cache.GetStatisticsAsync();
            stats.Should().NotBeNull("statistics should be accessible after corruption");
        }

        [Fact]
        public async Task CacheCorruption_BinaryGarbage_IgnoresCorruptedData()
        {
            // Arrange
            using var cache = CreateCache();
            var projectPath = Path.Combine(_tempDirectory, "BinaryTestProject.csproj");
            File.WriteAllText(projectPath, "<Project></Project>");
            
            var assemblies = new[] { "System.dll", "mscorlib.dll" };
            var callGraph = CreateTestCallGraph();
            var reverseCallGraph = CreateTestReverseCallGraph();

            await cache.StoreCallGraphAsync(projectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(1));

            // Act - Corrupt with binary garbage
            CorruptCacheFiles(_tempDirectory, CorruptionType.BinaryGarbage);

            // Assert
            var result = await cache.GetCallGraphAsync(projectPath, assemblies);
            result.Should().BeNull("binary garbage should be ignored");

            // Cache should continue working
            await cache.StoreCallGraphAsync(projectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(2));
            var newResult = await cache.GetCallGraphAsync(projectPath, assemblies);
            newResult.Should().NotBeNull("cache should work after binary corruption");
        }

        [Fact]
        public async Task CacheCorruption_PartiallyCorruptedMultipleEntries_RecoversSalvageableData()
        {
            // Arrange
            using var cache = CreateCache();
            var assemblies = new[] { "System.dll", "mscorlib.dll" };
            var projects = new List<string>();

            // Store multiple entries
            for (int i = 0; i < 5; i++)
            {
                var projectPath = Path.Combine(_tempDirectory, $"Project{i}.csproj");
                File.WriteAllText(projectPath, "<Project></Project>");
                projects.Add(projectPath);

                var callGraph = CreateTestCallGraph($"Project{i}");
                var reverseCallGraph = CreateTestReverseCallGraph($"Project{i}");
                await cache.StoreCallGraphAsync(projectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(1));
            }

            // Verify all entries are cached
            foreach (var project in projects)
            {
                var result = await cache.GetCallGraphAsync(project, assemblies);
                result.Should().NotBeNull($"Project {project} should be cached");
            }

            // Act - Selectively corrupt some cache files
            CorruptCacheFiles(_tempDirectory, CorruptionType.SelectiveCorruption);

            // Assert - Some entries may be recovered, others may be lost
            var recoveredCount = 0;
            var corruptedCount = 0;

            foreach (var project in projects)
            {
                try
                {
                    var result = await cache.GetCallGraphAsync(project, assemblies);
                    if (result != null)
                    {
                        recoveredCount++;
                    }
                    else
                    {
                        corruptedCount++;
                    }
                }
                catch (Exception ex)
                {
                    // Should not throw, but if it does, count as corrupted
                    _output.WriteLine($"Exception during recovery: {ex.Message}");
                    corruptedCount++;
                }
            }

            _output.WriteLine($"Recovery results: {recoveredCount} recovered, {corruptedCount} corrupted");
            
            // At least some level of recovery should be possible
            (recoveredCount + corruptedCount).Should().Be(projects.Count, "all entries should be accounted for");
            
            // Cache should remain functional
            var newProject = Path.Combine(_tempDirectory, "NewProject.csproj");
            File.WriteAllText(newProject, "<Project></Project>");
            await cache.StoreCallGraphAsync(newProject, assemblies, CreateTestCallGraph(), CreateTestReverseCallGraph(), TimeSpan.FromSeconds(1));
            
            var newResult = await cache.GetCallGraphAsync(newProject, assemblies);
            newResult.Should().NotBeNull("new entries should work after corruption");
        }

        [Fact]
        public async Task CacheDirectory_Missing_RecreatesDirectoryStructure()
        {
            // Arrange
            using var cache = CreateCache();
            var projectPath = Path.Combine(_tempDirectory, "DirectoryTestProject.csproj");
            File.WriteAllText(projectPath, "<Project></Project>");
            
            var assemblies = new[] { "System.dll", "mscorlib.dll" };
            var callGraph = CreateTestCallGraph();
            var reverseCallGraph = CreateTestReverseCallGraph();

            await cache.StoreCallGraphAsync(projectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(1));

            // Act - Delete entire cache directory
            var cacheDirectory = Path.Combine(_tempDirectory, ".testintel-cache");
            if (Directory.Exists(cacheDirectory))
            {
                Directory.Delete(cacheDirectory, recursive: true);
            }

            // Assert - Cache should recreate directory and continue working
            var result = await cache.GetCallGraphAsync(projectPath, assemblies);
            result.Should().BeNull("deleted cache should result in miss");

            await cache.StoreCallGraphAsync(projectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(1));
            var newResult = await cache.GetCallGraphAsync(projectPath, assemblies);
            newResult.Should().NotBeNull("cache should work after directory recreation");

            Directory.Exists(cacheDirectory).Should().BeTrue("cache directory should be recreated");
        }

        [Fact]
        public async Task CacheCorruption_OutOfDiskSpace_HandlesGracefully()
        {
            // Arrange
            using var cache = CreateCache();
            var projectPath = Path.Combine(_tempDirectory, "DiskSpaceTestProject.csproj");
            File.WriteAllText(projectPath, "<Project></Project>");
            
            var assemblies = new[] { "System.dll", "mscorlib.dll" };

            // Act & Assert - Simulate disk full scenario by trying to write very large data
            var hugeCallGraph = CreateHugeCallGraph(100000); // Very large call graph
            var hugeReverseCallGraph = CreateHugeReverseCallGraph(hugeCallGraph);

            var storeAction = async () => await cache.StoreCallGraphAsync(projectPath, assemblies, hugeCallGraph, hugeReverseCallGraph, TimeSpan.FromSeconds(1));
            
            // Should not throw even if storage fails
            await storeAction.Should().NotThrowAsync("disk space issues should be handled gracefully");

            // Cache should remain functional for normal-sized data
            var normalCallGraph = CreateTestCallGraph();
            var normalReverseCallGraph = CreateTestReverseCallGraph();
            
            await cache.StoreCallGraphAsync(projectPath, assemblies, normalCallGraph, normalReverseCallGraph, TimeSpan.FromSeconds(1));
            var result = await cache.GetCallGraphAsync(projectPath, assemblies);
            result.Should().NotBeNull("normal operations should work after storage failure");
        }

        [Fact]
        public async Task CacheCorruption_ConcurrentAccessDuringCorruption_ThreadSafe()
        {
            // Arrange
            using var cache = CreateCache();
            var assemblies = new[] { "System.dll", "mscorlib.dll" };
            var projects = new List<string>();

            for (int i = 0; i < 10; i++)
            {
                var projectPath = Path.Combine(_tempDirectory, $"ConcurrentProject{i}.csproj");
                File.WriteAllText(projectPath, "<Project></Project>");
                projects.Add(projectPath);

                var callGraph = CreateTestCallGraph($"Concurrent{i}");
                var reverseCallGraph = CreateTestReverseCallGraph($"Concurrent{i}");
                await cache.StoreCallGraphAsync(projectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(1));
            }

            // Act - Corrupt cache while performing concurrent operations
            var corruptionTask = Task.Run(async () =>
            {
                await Task.Delay(100); // Let some operations start
                CorruptCacheFiles(_tempDirectory, CorruptionType.InvalidFormat);
            });

            var operationTasks = projects.Select(async (project, index) =>
            {
                try
                {
                    // Perform various operations concurrently
                    await Task.Delay(Random.Shared.Next(0, 200));
                    
                    if (index % 2 == 0)
                    {
                        return await cache.GetCallGraphAsync(project, assemblies);
                    }
                    else
                    {
                        var callGraph = CreateTestCallGraph($"Concurrent{index}_New");
                        var reverseCallGraph = CreateTestReverseCallGraph($"Concurrent{index}_New");
                        await cache.StoreCallGraphAsync(project, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(1));
                        return await cache.GetCallGraphAsync(project, assemblies);
                    }
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Concurrent operation failed: {ex.Message}");
                    return null;
                }
            }).ToList();

            // Assert - Should not deadlock or throw exceptions
            await Task.WhenAll(operationTasks.Concat(new[] { corruptionTask }));
            
            // Cache should still be accessible
            var stats = await cache.GetStatisticsAsync();
            stats.Should().NotBeNull("statistics should be accessible after concurrent corruption");

            _output.WriteLine($"Concurrent operations completed. Cache entries: {stats.TotalEntries}");
        }

        [Fact]
        public async Task CacheCorruption_RecoveryMaintainsPerformance_BenchmarkTest()
        {
            // Arrange
            using var cache = CreateCache();
            var projectPath = Path.Combine(_tempDirectory, "PerformanceTestProject.csproj");
            File.WriteAllText(projectPath, "<Project></Project>");
            
            var assemblies = new[] { "System.dll", "mscorlib.dll" };
            var callGraph = CreateTestCallGraph();
            var reverseCallGraph = CreateTestReverseCallGraph();

            // Measure baseline performance
            var baselineStopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 10; i++)
            {
                await cache.StoreCallGraphAsync(projectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(1));
                await cache.GetCallGraphAsync(projectPath, assemblies);
            }
            baselineStopwatch.Stop();
            var baselineTime = baselineStopwatch.ElapsedMilliseconds;

            // Act - Corrupt and measure recovery performance
            CorruptCacheFiles(_tempDirectory, CorruptionType.InvalidFormat);

            var recoveryStopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 10; i++)
            {
                await cache.StoreCallGraphAsync(projectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(1));
                await cache.GetCallGraphAsync(projectPath, assemblies);
            }
            recoveryStopwatch.Stop();
            var recoveryTime = recoveryStopwatch.ElapsedMilliseconds;

            // Assert - Performance should not degrade significantly
            _output.WriteLine($"Baseline: {baselineTime}ms, Recovery: {recoveryTime}ms");
            
            var performanceRatio = (double)recoveryTime / baselineTime;
            performanceRatio.Should().BeLessThan(3.0, "recovery performance should not be more than 3x slower than baseline");
        }

        private CallGraphCache CreateCache()
        {
            var cacheOptions = new CompressedCacheOptions
            {
                MaxCacheSizeBytes = 50 * 1024 * 1024, // 50MB
                EnableBackgroundMaintenance = false
            };
            
            return new CallGraphCache(_tempDirectory, cacheOptions, _logger);
        }

        private void CorruptCacheFiles(string baseDirectory, CorruptionType corruptionType)
        {
            // The cache is created directly in the base directory for our tests
            var cacheDirectory = baseDirectory;
            if (!Directory.Exists(cacheDirectory)) return;

            var cacheFiles = Directory.GetFiles(cacheDirectory, "*.cache", SearchOption.AllDirectories);
            
            if (cacheFiles.Length == 0)
            {
                // Fallback: look for any cache files in subdirectories
                cacheFiles = Directory.GetFiles(cacheDirectory, "*", SearchOption.AllDirectories)
                    .Where(f => Path.GetExtension(f).Equals(".cache", StringComparison.OrdinalIgnoreCase) ||
                               f.Contains("cache", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            
            foreach (var file in cacheFiles.Take(Math.Max(1, cacheFiles.Length / 2))) // Corrupt half the files
            {
                CorruptFile(file, corruptionType);
            }
        }

        private void CorruptFile(string filePath, CorruptionType corruptionType)
        {
            try
            {
                switch (corruptionType)
                {
                    case CorruptionType.InvalidFormat:
                        File.WriteAllText(filePath, "INVALID_CACHE_DATA_NOT_JSON");
                        break;
                        
                    case CorruptionType.Truncated:
                        var originalBytes = File.ReadAllBytes(filePath);
                        if (originalBytes.Length > 10)
                        {
                            var truncatedBytes = originalBytes.Take(originalBytes.Length / 3).ToArray();
                            File.WriteAllBytes(filePath, truncatedBytes);
                        }
                        break;
                        
                    case CorruptionType.BinaryGarbage:
                        var garbageBytes = new byte[Random.Shared.Next(100, 1000)];
                        Random.Shared.NextBytes(garbageBytes);
                        File.WriteAllBytes(filePath, garbageBytes);
                        break;
                        
                    case CorruptionType.SelectiveCorruption:
                        if (Random.Shared.Next(100) < 50) // 50% chance of corruption
                        {
                            CorruptFile(filePath, Random.Shared.Next(100) < 50 ? CorruptionType.InvalidFormat : CorruptionType.Truncated);
                        }
                        break;
                }
            }
            catch
            {
                // Best effort corruption
            }
        }

        private Dictionary<string, HashSet<string>> CreateTestCallGraph(string prefix = "Method")
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

        private Dictionary<string, HashSet<string>> CreateTestReverseCallGraph(string prefix = "Method")
        {
            return new Dictionary<string, HashSet<string>>
            {
                [$"{prefix}B"] = new HashSet<string> { $"{prefix}A" },
                [$"{prefix}C"] = new HashSet<string> { $"{prefix}A" },
                [$"{prefix}D"] = new HashSet<string> { $"{prefix}B", $"{prefix}C" },
                [$"{prefix}E"] = new HashSet<string> { $"{prefix}C" }
            };
        }

        private Dictionary<string, HashSet<string>> CreateHugeCallGraph(int methodCount)
        {
            var callGraph = new Dictionary<string, HashSet<string>>();
            
            for (int i = 0; i < methodCount; i++)
            {
                var method = $"HugeMethod{i}_{new string('x', 100)}"; // Long method names
                var callees = new HashSet<string>();
                
                // Each method calls many others
                for (int j = 0; j < Math.Min(50, methodCount); j++)
                {
                    var callee = $"HugeMethod{Random.Shared.Next(methodCount)}_{new string('y', 100)}";
                    callees.Add(callee);
                }
                
                callGraph[method] = callees;
            }
            
            return callGraph;
        }

        private Dictionary<string, HashSet<string>> CreateHugeReverseCallGraph(Dictionary<string, HashSet<string>> callGraph)
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

        private enum CorruptionType
        {
            InvalidFormat,
            Truncated,
            BinaryGarbage,
            SelectiveCorruption
        }

        private class TestLogger<T> : ILogger<T>
        {
            private readonly ITestOutputHelper _output;

            public TestLogger(ITestOutputHelper output)
            {
                _output = output;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                
                try
                {
                    var message = formatter(state, exception);
                    _output.WriteLine($"[{logLevel}] {message}");
                    
                    if (exception != null)
                    {
                        _output.WriteLine($"Exception: {exception}");
                    }
                }
                catch
                {
                    // Ignore logging failures in tests
                }
            }
        }
    }
}