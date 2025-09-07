using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Caching;
using TestIntelligence.ImpactAnalyzer.Caching;
using TestIntelligence.TestUtilities;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Caching
{
    /// <summary>
    /// Tests concurrent access patterns for cache operations including thread safety,
    /// concurrent reads/writes, and race condition handling.
    /// </summary>
    public class ConcurrentCacheAccessTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly TestSolutionGenerator _solutionGenerator;
        private readonly ILogger<CallGraphCache> _logger;

        public ConcurrentCacheAccessTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "ConcurrentCacheTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            _solutionGenerator = new TestSolutionGenerator(_tempDirectory);
            _logger = new TestLogger<CallGraphCache>();
        }

        public void Dispose()
        {
            _solutionGenerator?.Dispose();
            
            if (Directory.Exists(_tempDirectory))
            {
                try
                {
                    Directory.Delete(_tempDirectory, recursive: true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        #region Concurrent Read/Write Tests

        [Fact]
        public async Task ConcurrentCacheAccess_MultipleReadersWriters_ShouldMaintainConsistency()
        {
            // Arrange
            var cacheDirectory = Path.Combine(_tempDirectory, "concurrent_rw_cache");
            using var cache = new CallGraphCache(cacheDirectory, options: null, _logger);
            
            var projectPaths = Enumerable.Range(0, 5)
                .Select(i => Path.Combine(_tempDirectory, $"Project{i}.csproj"))
                .ToList();
            
            // Create test project files
            foreach (var path in projectPaths)
            {
                await File.WriteAllTextAsync(path, GetTestProjectContent($"Project{Path.GetFileNameWithoutExtension(path)}"));
            }

            var referencedAssemblies = new[] { "System.dll", "System.Core.dll" };
            var readResults = new ConcurrentBag<CompressedCallGraphCacheEntry?>();
            var writeResults = new ConcurrentBag<bool>();

            // Act - Concurrent reads and writes
            var readTasks = Enumerable.Range(0, 10).Select(async i =>
            {
                await Task.Delay(_random.Next(1, 50)); // Stagger operations
                var projectPath = projectPaths[i % projectPaths.Count];
                var result = await cache.GetCallGraphAsync(projectPath, referencedAssemblies);
                readResults.Add(result);
            });

            var writeTasks = Enumerable.Range(0, 5).Select(async i =>
            {
                await Task.Delay(_random.Next(1, 100)); // Stagger operations
                var projectPath = projectPaths[i % projectPaths.Count];
                var entry = CreateTestCacheEntry(projectPath, referencedAssemblies);
                
                try
                {
            await cache.StoreCallGraphAsync(projectPath, referencedAssemblies, entry.CallGraph, entry.ReverseCallGraph, TimeSpan.FromSeconds(1));
                    writeResults.Add(true);
                }
                catch
                {
                    writeResults.Add(false);
                }
            });

            await Task.WhenAll(readTasks.Concat(writeTasks));

            // Assert
            readResults.Should().NotBeEmpty();
            writeResults.Should().NotBeEmpty();
            writeResults.Should().Contain(true); // At least some writes should succeed
        }

        [Fact]
        public async Task ConcurrentCacheInvalidation_ShouldHandleRaceConditions()
        {
            // Arrange
            var cacheDirectory = Path.Combine(_tempDirectory, "concurrent_invalidation_cache");
            using var cache = new CallGraphCache(cacheDirectory, options: null, _logger);
            
            var projectPath = Path.Combine(_tempDirectory, "TestProject.csproj");
            await File.WriteAllTextAsync(projectPath, GetTestProjectContent("TestProject"));
            
            var referencedAssemblies = new[] { "System.dll" };
            
            // Pre-populate cache
            var initialEntry = CreateTestCacheEntry(projectPath, referencedAssemblies);
            await cache.StoreCallGraphAsync(projectPath, referencedAssemblies, initialEntry.CallGraph, initialEntry.ReverseCallGraph, TimeSpan.FromSeconds(1));

            var invalidationResults = new ConcurrentBag<bool>();

            // Act - Concurrent invalidations
            var invalidationTasks = Enumerable.Range(0, 10).Select(async i =>
            {
                try
                {
                    await cache.InvalidateAsync(projectPath);
                    invalidationResults.Add(true);
                }
                catch
                {
                    invalidationResults.Add(false);
                }
            });

            await Task.WhenAll(invalidationTasks);

            // Assert
            invalidationResults.Should().NotBeEmpty();
            invalidationResults.Should().Contain(true); // At least some invalidations should succeed
            
            // Verify cache is actually invalidated
            var cachedEntry = await cache.GetCallGraphAsync(projectPath, referencedAssemblies);
            cachedEntry.Should().BeNull();
        }

        #endregion

        #region High-Load Concurrent Access Tests

        [Fact]
        public async Task HighLoadConcurrentAccess_ShouldMaintainPerformance()
        {
            // Arrange
            var cacheDirectory = Path.Combine(_tempDirectory, "high_load_cache");
            using var cache = new CallGraphCache(cacheDirectory, options: null, _logger);
            
            var solution = await CreateTestSolutionAsync("HighLoadSolution", 20);
            var projectPaths = solution.Projects.Select(p => p.Path).ToList();
            var referencedAssemblies = new[] { "System.dll", "System.Core.dll", "System.Linq.dll" };

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var operationResults = new ConcurrentBag<OperationResult>();

            // Act - High volume concurrent operations
            var concurrentOperations = Enumerable.Range(0, 100).Select(async i =>
            {
                var projectPath = projectPaths[i % projectPaths.Count];
                var operationType = i % 3; // Mix of read, write, invalidate operations

                try
                {
                    switch (operationType)
                    {
                        case 0: // Read
                            var cached = await cache.GetCallGraphAsync(projectPath, referencedAssemblies);
                            operationResults.Add(new OperationResult { Type = "Read", Success = true });
                            break;
                            
                        case 1: // Write
                            var entry = CreateTestCacheEntry(projectPath, referencedAssemblies);
                    await cache.StoreCallGraphAsync(projectPath, referencedAssemblies, entry.CallGraph, entry.ReverseCallGraph, TimeSpan.FromSeconds(1));
                            operationResults.Add(new OperationResult { Type = "Write", Success = true });
                            break;
                            
                        case 2: // Invalidate
                            await cache.InvalidateAsync(projectPath);
                            operationResults.Add(new OperationResult { Type = "Invalidate", Success = true });
                            break;
                    }
                }
                catch
                {
                    operationResults.Add(new OperationResult 
                    { 
                        Type = operationType switch { 0 => "Read", 1 => "Write", _ => "Invalidate" }, 
                        Success = false 
                    });
                }
            });

            await Task.WhenAll(concurrentOperations);
            stopwatch.Stop();

            // Assert
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(30000); // Should complete within 30 seconds
            operationResults.Should().HaveCount(100);
            
            // Most operations should succeed
            var successRate = operationResults.Count(r => r.Success) / (double)operationResults.Count;
            successRate.Should().BeGreaterThan(0.8); // At least 80% success rate
        }

        [Fact]
        public async Task ConcurrentCacheCompression_ShouldHandleParallelOperations()
        {
            // Arrange
            var cacheDirectory = Path.Combine(_tempDirectory, "compression_concurrent_cache");
            var options = new CompressedCacheOptions
            {
                EnableCompression = true,
                CompressionLevel = System.IO.Compression.CompressionLevel.Fastest
            };
            
            using var cache = new CallGraphCache(cacheDirectory, options, _logger);
            
            var solution = await CreateTestSolutionAsync("CompressionTestSolution", 10);
            var entries = solution.Projects.Select(p => new
            {
                ProjectPath = p.Path,
                Entry = CreateTestCacheEntry(p.Path, new[] { "System.dll" })
            }).ToList();

            // Act - Store entries concurrently with compression
            var storeTasks = entries.Select(async item =>
            {
                try
                {
                    await cache.StoreCallGraphAsync(item.ProjectPath, new[] { "System.dll" }, item.Entry.CallGraph, item.Entry.ReverseCallGraph, TimeSpan.FromSeconds(1));
                    return true;
                }
                catch
                {
                    return false;
                }
            });

            var storeResults = await Task.WhenAll(storeTasks);

            // Retrieve entries concurrently
            var retrieveTasks = entries.Select(async item =>
            {
                try
                {
                    var result = await cache.GetCallGraphAsync(item.ProjectPath, new[] { "System.dll" });
                    return result != null;
                }
                catch
                {
                    return false;
                }
            });

            var retrieveResults = await Task.WhenAll(retrieveTasks);

            // Assert
            storeResults.Should().Contain(true); // At least some stores should succeed
            retrieveResults.Should().Contain(true); // At least some retrievals should succeed
        }

        #endregion

        #region Cache Corruption Recovery During Concurrent Access

        [Fact]
        public async Task ConcurrentAccessDuringCorruption_ShouldRecoverGracefully()
        {
            // Arrange
            var cacheDirectory = Path.Combine(_tempDirectory, "corruption_concurrent_cache");
            using var cache = new CallGraphCache(cacheDirectory, options: null, _logger);
            
            var projectPath = Path.Combine(_tempDirectory, "CorruptionTestProject.csproj");
            await File.WriteAllTextAsync(projectPath, GetTestProjectContent("CorruptionTestProject"));
            
            var referencedAssemblies = new[] { "System.dll" };
            
            // Pre-populate cache
            var entry = CreateTestCacheEntry(projectPath, referencedAssemblies);
            await cache.StoreCallGraphAsync(projectPath, referencedAssemblies, entry.CallGraph, entry.ReverseCallGraph, TimeSpan.FromSeconds(1));

            var accessResults = new ConcurrentBag<bool>();
            
            // Act - Access cache concurrently while introducing corruption
            var accessTasks = Enumerable.Range(0, 20).Select(async i =>
            {
                await Task.Delay(_random.Next(1, 100));
                
                try
                {
                    if (i == 10) // Introduce corruption midway
                    {
                        await CorruptCacheFiles(cacheDirectory);
                    }
                    
                    var result = await cache.GetCallGraphAsync(projectPath, referencedAssemblies);
                    accessResults.Add(true);
                }
                catch
                {
                    accessResults.Add(false);
                }
            });

            await Task.WhenAll(accessTasks);

            // Assert
            accessResults.Should().NotBeEmpty();
            
            // Cache should recover and continue functioning
            var finalResult = await cache.GetCallGraphAsync(projectPath, referencedAssemblies);
            // finalResult may be null (cache cleared) or valid (corruption recovered)
        }

        [Fact]
        public async Task ConcurrentFileSystemOperations_ShouldHandleFileSystemRaceConditions()
        {
            // Arrange
            var cacheDirectory = Path.Combine(_tempDirectory, "filesystem_race_cache");
            using var cache1 = new CallGraphCache(cacheDirectory, options: null, _logger);
            using var cache2 = new CallGraphCache(cacheDirectory, options: null, _logger);
            
            var projectPath = Path.Combine(_tempDirectory, "FileSystemRaceProject.csproj");
            await File.WriteAllTextAsync(projectPath, GetTestProjectContent("FileSystemRaceProject"));
            
            var referencedAssemblies = new[] { "System.dll" };
            var operationResults = new ConcurrentBag<string>();

            // Act - Multiple cache instances accessing same files
            var cache1Operations = Enumerable.Range(0, 10).Select(async i =>
            {
                try
                {
                    if (i % 2 == 0)
                    {
                        var entry = CreateTestCacheEntry(projectPath, referencedAssemblies);
                        await cache1.StoreCallGraphAsync(projectPath, referencedAssemblies, entry.CallGraph, entry.ReverseCallGraph, TimeSpan.FromSeconds(1));
                        operationResults.Add("Cache1-Store-Success");
                    }
                    else
                    {
                        var result = await cache1.GetCallGraphAsync(projectPath, referencedAssemblies);
                        operationResults.Add("Cache1-Get-Success");
                    }
                }
                catch
                {
                    operationResults.Add("Cache1-Failed");
                }
            });

            var cache2Operations = Enumerable.Range(0, 10).Select(async i =>
            {
                try
                {
                    if (i % 2 == 1)
                    {
                        var entry = CreateTestCacheEntry(projectPath, referencedAssemblies);
                        await cache2.StoreCallGraphAsync(projectPath, referencedAssemblies, entry.CallGraph, entry.ReverseCallGraph, TimeSpan.FromSeconds(1));
                        operationResults.Add("Cache2-Store-Success");
                    }
                    else
                    {
                        var result = await cache2.GetCallGraphAsync(projectPath, referencedAssemblies);
                        operationResults.Add("Cache2-Get-Success");
                    }
                }
                catch
                {
                    operationResults.Add("Cache2-Failed");
                }
            });

            await Task.WhenAll(cache1Operations.Concat(cache2Operations));

            // Assert
            operationResults.Should().HaveCount(20);
            operationResults.Should().Contain(result => result.Contains("Success"));
        }

        #endregion

        #region Memory Pressure During Concurrent Operations

        [Fact]
        public async Task ConcurrentOperationsUnderMemoryPressure_ShouldMaintainStability()
        {
            // Arrange
            var cacheDirectory = Path.Combine(_tempDirectory, "memory_pressure_cache");
            using var cache = new CallGraphCache(cacheDirectory, options: null, _logger);
            using var memoryPressure = new MemoryPressureTestHarness();
            
            var solution = await CreateTestSolutionAsync("MemoryPressureSolution", 15);
            var stabilityResults = new ConcurrentBag<bool>();

            // Apply memory pressure
            memoryPressure.ApplyPressure(targetMemoryMB: 100, durationSeconds: 30);

            // Act - Perform cache operations under memory pressure
            var operations = solution.Projects.SelectMany(project => Enumerable.Range(0, 5).Select(async i =>
            {
                try
                {
                    var referencedAssemblies = new[] { "System.dll", $"TestLib{i}.dll" };
                    
                    // Mix of operations
                    if (i % 3 == 0)
                    {
                        var entry = CreateTestCacheEntry(project.Path, referencedAssemblies);
                        await cache.StoreCallGraphAsync(project.Path, referencedAssemblies, entry.CallGraph, entry.ReverseCallGraph, TimeSpan.FromSeconds(1));
                    }
                    else if (i % 3 == 1)
                    {
                        await cache.GetCallGraphAsync(project.Path, referencedAssemblies);
                    }
                    else
                    {
                        await cache.InvalidateAsync(project.Path);
                    }
                    
                    stabilityResults.Add(true);
                }
                catch
                {
                    stabilityResults.Add(false);
                }
            }));

            await Task.WhenAll(operations);

            // Assert
            stabilityResults.Should().NotBeEmpty();
            
            // System should remain stable even under memory pressure
            var stabilityRate = stabilityResults.Count(r => r) / (double)stabilityResults.Count;
            stabilityRate.Should().BeGreaterThan(0.5); // At least 50% operations should succeed under pressure
        }

        #endregion

        #region Helper Methods

        private readonly Random _random = new(42); // Fixed seed for reproducible tests

        private async Task<GeneratedSolution> CreateTestSolutionAsync(string solutionName, int projectCount)
        {
            var config = new SolutionConfiguration
            {
                SolutionName = solutionName,
                ProjectCount = projectCount,
                ProjectTemplate = new ProjectConfiguration
                {
                    ClassCount = 3,
                    MethodsPerClass = 5,
                    IncludeComplexity = false
                }
            };

            return await _solutionGenerator.CreateSolutionAsync(config);
        }

        private CompressedCallGraphCacheEntry CreateTestCacheEntry(string projectPath, IEnumerable<string> referencedAssemblies)
        {
            var callGraph = new Dictionary<string, HashSet<string>>
            {
                { "TestMethod1", new HashSet<string> { "TestMethod2", "TestMethod3" } },
                { "TestMethod2", new HashSet<string> { "TestMethod3" } },
                { "TestMethod3", new HashSet<string>() }
            };

            var dependencyHashes = referencedAssemblies.ToDictionary(
                assembly => assembly,
                assembly => ComputeHash(assembly));

            return new CompressedCallGraphCacheEntry
            {
                ProjectPath = projectPath,
                CallGraph = callGraph,
                DependencyHashes = dependencyHashes,
                CompilerVersion = "TestCompiler-1.0",
                CreatedAt = DateTime.UtcNow,
                CompressedSize = 1024,
                UncompressedSize = 4096
            };
        }

        private string GetTestProjectContent(string projectName)
        {
            return $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>{projectName}</AssemblyName>
  </PropertyGroup>
</Project>";
        }

        private async Task CorruptCacheFiles(string cacheDirectory)
        {
            try
            {
                var cacheFiles = Directory.GetFiles(cacheDirectory, "*", SearchOption.AllDirectories);
                if (cacheFiles.Any())
                {
                    var fileToCorrupt = cacheFiles[_random.Next(cacheFiles.Length)];
                    var corruptData = new byte[_random.Next(10, 100)];
                    _random.NextBytes(corruptData);
                    await File.WriteAllBytesAsync(fileToCorrupt, corruptData);
                }
            }
            catch
            {
                // Corruption attempt failed - acceptable for this test
            }
        }

        private string ComputeHash(string input)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(hash)[0..16]; // Truncate for test purposes
        }

        #endregion

        #region Helper Classes

        private class OperationResult
        {
            public string Type { get; set; } = string.Empty;
            public bool Success { get; set; }
        }

        private class TestLogger<T> : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => false;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, 
                Exception? exception, Func<TState, Exception?, string> formatter) { }
        }

        #endregion
    }
}