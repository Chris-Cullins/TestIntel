using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Caching;
using Xunit;
using Xunit.Abstractions;

namespace TestIntelligence.ImpactAnalyzer.Tests.Performance
{
    public class CompilationCacheIntegrationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly ILogger<OptimizedRoslynAnalyzer> _analyzerLogger;
        private readonly ILogger<EnhancedCompilationCache> _cacheLogger;
        private readonly ILogger<FileSystemCache> _fsLogger;
        private readonly IMemoryCache _memoryCache;
        private readonly IFileSystemCache _fileSystemCache;
        private readonly string _tempCacheDir;

        public CompilationCacheIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _analyzerLogger = new TestLogger<OptimizedRoslynAnalyzer>(output);
            _cacheLogger = new TestLogger<EnhancedCompilationCache>(output);
            _fsLogger = new TestLogger<FileSystemCache>(output);
            _memoryCache = new MemoryCache(new MemoryCacheOptions());
            _tempCacheDir = Path.Combine(Path.GetTempPath(), "TestIntelCache", Guid.NewGuid().ToString());
            _fileSystemCache = new FileSystemCache(_tempCacheDir, _fsLogger);
        }

        [Fact]
        public async Task OptimizedAnalyzer_ShowsPerformanceImprovement()
        {
            // Create test files
            var testFiles = GenerateTestFiles(20, 100);

            try
            {
                // Test original analyzer
                var originalAnalyzer = new RoslynAnalyzer(new TestLogger<RoslynAnalyzer>(_output));
                var originalStats = await MeasureAnalyzerPerformance(originalAnalyzer, testFiles, "Original");

                // Test optimized analyzer
                var compilationCache = new EnhancedCompilationCache(
                    _memoryCache, 
                    _fileSystemCache, 
                    _cacheLogger,
                    null, // No distributed cache for test
                    new EnhancedCompilationCacheOptions
                    {
                        MemoryCacheExpiration = TimeSpan.FromMinutes(10),
                        FileSystemCacheExpiration = TimeSpan.FromHours(1)
                    });

                var optimizedAnalyzer = new OptimizedRoslynAnalyzer(_analyzerLogger, compilationCache, maxParallelism: 2);
                var optimizedStats = await MeasureAnalyzerPerformance(optimizedAnalyzer, testFiles, "Optimized");

                // Validate performance improvement
                _output.WriteLine("\nPerformance Comparison:");
                _output.WriteLine($"Original Time: {originalStats.TotalTime}ms");
                _output.WriteLine($"Optimized Time: {optimizedStats.TotalTime}ms");
                
                if (originalStats.TotalTime > 0)
                {
                    var improvement = (1.0 - (double)optimizedStats.TotalTime / originalStats.TotalTime) * 100;
                    _output.WriteLine($"Performance Improvement: {improvement:F1}%");
                }

                _output.WriteLine($"Original Methods Found: {originalStats.MethodsFound}");
                _output.WriteLine($"Optimized Methods Found: {optimizedStats.MethodsFound}");

                // Validate correctness (same number of methods found)
                Assert.Equal(originalStats.MethodsFound, optimizedStats.MethodsFound);
                
                // Display cache statistics
                var cacheStats = compilationCache.GetStatistics();
                _output.WriteLine($"\nCache Statistics:");
                _output.WriteLine($"Total Requests: {cacheStats.TotalRequests}");
                _output.WriteLine($"Cache Hit Ratio: {cacheStats.HitRatio:F2}");
                _output.WriteLine($"Memory Hits: {cacheStats.MemoryCacheHits}");
                _output.WriteLine($"FileSystem Hits: {cacheStats.FileSystemCacheHits}");
                _output.WriteLine($"Cache Misses: {cacheStats.CacheMisses}");

                compilationCache.Dispose();
                optimizedAnalyzer.Dispose();
            }
            finally
            {
                CleanupTestFiles(testFiles);
            }
        }

        [Fact]
        public async Task EnhancedCache_MultiLevelCaching_WorksCorrectly()
        {
            var compilationCache = new EnhancedCompilationCache(
                _memoryCache,
                _fileSystemCache,
                _cacheLogger);

            var testFile = GenerateTestFiles(1, 50)[0];

            try
            {
                // First access - should be cache miss
                var compilation1 = await compilationCache.GetOrCreateCompilationAsync(testFile, async () =>
                {
                    var sourceCode = await File.ReadAllTextAsync(testFile);
                    var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode, path: testFile);
                    return Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                        "TestAssembly",
                        new[] { syntaxTree },
                        Array.Empty<Microsoft.CodeAnalysis.MetadataReference>());
                });

                Assert.NotNull(compilation1);

                // Second access - should be memory cache hit
                var compilation2 = await compilationCache.GetOrCreateCompilationAsync(testFile, () =>
                {
                    throw new InvalidOperationException("Factory should not be called on cache hit");
                });

                Assert.NotNull(compilation2);
                Assert.Same(compilation1, compilation2);

                var stats = compilationCache.GetStatistics();
                Assert.Equal(2, stats.TotalRequests);
                Assert.Equal(1, stats.MemoryCacheHits);
                Assert.Equal(1, stats.CacheMisses);

                compilationCache.Dispose();
            }
            finally
            {
                CleanupTestFiles(new[] { testFile });
            }
        }

        [Fact]
        public void SyntaxTreePool_ShowsMemoryOptimization()
        {
            var pool = new SyntaxTreePool(new TestLogger<SyntaxTreePool>(_output));

            try
            {
                var testCode = @"
                using System;
                public class TestClass
                {
                    public void TestMethod()
                    {
                        Console.WriteLine(""Hello World"");
                    }
                }";

                // Get multiple syntax trees
                var tree1 = pool.GetOrParse(testCode, "test1.cs");
                var tree2 = pool.GetOrParse(testCode, "test1.cs"); // Same content, should hit cache
                var tree3 = pool.GetOrParse(testCode + " // different", "test2.cs");

                Assert.NotNull(tree1);
                Assert.NotNull(tree2);
                Assert.NotNull(tree3);

                // Return trees to pool
                pool.Return(tree1);
                pool.Return(tree3);

                var stats = pool.GetStatistics();
                _output.WriteLine($"Pool Statistics:");
                _output.WriteLine($"Total Requests: {stats.TotalRequests}");
                _output.WriteLine($"Cache Hits: {stats.CacheHits}");
                _output.WriteLine($"Pool Hits: {stats.PoolHits}");
                _output.WriteLine($"New Creations: {stats.NewCreations}");
                _output.WriteLine($"Cache Hit Ratio: {stats.CacheHitRatio:F2}");

                Assert.Equal(3, stats.TotalRequests);
                Assert.Equal(1, stats.CacheHits); // tree2 should be cache hit
                Assert.Equal(2, stats.NewCreations);
            }
            finally
            {
                pool.Dispose();
            }
        }

        private async Task<AnalyzerPerformanceStats> MeasureAnalyzerPerformance(
            IRoslynAnalyzer analyzer, 
            string[] testFiles, 
            string analyzerName)
        {
            var startTime = DateTime.UtcNow;
            var totalMethods = 0;

            foreach (var file in testFiles)
            {
                var methods = await analyzer.ExtractMethodsFromFileAsync(file);
                totalMethods += methods.Count;
            }

            var endTime = DateTime.UtcNow;
            var totalTime = (long)(endTime - startTime).TotalMilliseconds;

            _output.WriteLine($"{analyzerName} Analyzer: {totalTime}ms, Methods: {totalMethods}");

            return new AnalyzerPerformanceStats
            {
                TotalTime = totalTime,
                MethodsFound = totalMethods
            };
        }

        private string[] GenerateTestFiles(int fileCount, int linesPerFile)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "CacheTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            
            var files = new string[fileCount];
            
            for (int i = 0; i < fileCount; i++)
            {
                var filePath = Path.Combine(tempDir, $"TestClass{i:D4}.cs");
                var content = GenerateTestFileContent($"TestClass{i:D4}", linesPerFile);
                
                File.WriteAllText(filePath, content);
                files[i] = filePath;
            }
            
            return files;
        }

        private string GenerateTestFileContent(string className, int targetLines)
        {
            var lines = new System.Text.StringBuilder();
            lines.AppendLine("using System;");
            lines.AppendLine("using System.Collections.Generic;");
            lines.AppendLine($"namespace TestIntelligence.Tests {{ public class {className} {{");

            for (int i = 0; i < targetLines / 5; i++)
            {
                lines.AppendLine($"    public void Method{i}() {{ var x = {i}; }}");
            }

            lines.AppendLine("}}");
            return lines.ToString();
        }

        private void CleanupTestFiles(string[] files)
        {
            if (files?.Length > 0)
            {
                var baseDir = Path.GetDirectoryName(files[0]);
                if (Directory.Exists(baseDir))
                {
                    Directory.Delete(baseDir, true);
                }
            }
        }

        public void Dispose()
        {
            _memoryCache?.Dispose();
            
            if (Directory.Exists(_tempCacheDir))
            {
                Directory.Delete(_tempCacheDir, true);
            }
        }

        private class AnalyzerPerformanceStats
        {
            public long TotalTime { get; set; }
            public int MethodsFound { get; set; }
        }
    }
}