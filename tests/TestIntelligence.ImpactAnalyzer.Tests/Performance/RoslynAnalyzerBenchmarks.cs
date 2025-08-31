using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.ImpactAnalyzer.Analysis;
using Xunit;
using Xunit.Abstractions;

namespace TestIntelligence.ImpactAnalyzer.Tests.Performance
{
    public class RoslynAnalyzerBenchmarks
    {
        private readonly ITestOutputHelper _output;
        private readonly ILogger<RoslynAnalyzer> _logger;

        public RoslynAnalyzerBenchmarks(ITestOutputHelper output)
        {
            _output = output;
            _logger = new TestLogger<RoslynAnalyzer>(output);
        }

        [Fact]
        public async Task BenchmarkCompilationCaching_SmallCodebase()
        {
            var analyzer = new RoslynAnalyzer(_logger);
            var testFiles = GenerateTestFiles(10, 50); // 10 files, 50 lines each
            
            await RunCompilationBenchmark(analyzer, testFiles, "Small Codebase (10 files)");
            
            CleanupTestFiles(testFiles);
        }

        [Fact]
        public async Task BenchmarkCompilationCaching_MediumCodebase()
        {
            var analyzer = new RoslynAnalyzer(_logger);
            var testFiles = GenerateTestFiles(100, 200); // 100 files, 200 lines each
            
            await RunCompilationBenchmark(analyzer, testFiles, "Medium Codebase (100 files)");
            
            CleanupTestFiles(testFiles);
        }

        [Fact]
        public async Task BenchmarkCompilationCaching_LargeCodebase()
        {
            var analyzer = new RoslynAnalyzer(_logger);
            var testFiles = GenerateTestFiles(500, 500); // 500 files, 500 lines each
            
            await RunCompilationBenchmark(analyzer, testFiles, "Large Codebase (500 files)");
            
            CleanupTestFiles(testFiles);
        }

        [Fact]
        public async Task BenchmarkConcurrentAccess()
        {
            var analyzer = new RoslynAnalyzer(_logger);
            var testFiles = GenerateTestFiles(50, 100);

            var sw = Stopwatch.StartNew();
            
            // Test concurrent compilation access
            var tasks = testFiles.Select(async file =>
            {
                var model = await analyzer.GetSemanticModelAsync(file);
                var methods = await analyzer.ExtractMethodsFromFileAsync(file);
                return methods.Count;
            }).ToArray();

            var results = await Task.WhenAll(tasks);
            
            sw.Stop();
            
            var totalMethods = results.Sum();
            _output.WriteLine($"Concurrent Access Benchmark:");
            _output.WriteLine($"  Files: {testFiles.Length}");
            _output.WriteLine($"  Total Methods: {totalMethods}");
            _output.WriteLine($"  Duration: {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"  Methods/sec: {totalMethods * 1000.0 / sw.ElapsedMilliseconds:F2}");
            
            CleanupTestFiles(testFiles);
        }

        [Fact]
        public async Task BenchmarkMemoryUsage()
        {
            var analyzer = new RoslynAnalyzer(_logger);
            var testFiles = GenerateTestFiles(100, 200);

            var initialMemory = GC.GetTotalMemory(true);
            
            // Build call graphs to stress test memory usage
            await analyzer.BuildCallGraphAsync(testFiles);
            
            var afterAnalysisMemory = GC.GetTotalMemory(false);
            var memoryUsed = afterAnalysisMemory - initialMemory;
            
            _output.WriteLine($"Memory Usage Benchmark:");
            _output.WriteLine($"  Files: {testFiles.Length}");
            _output.WriteLine($"  Memory Used: {memoryUsed / 1024.0 / 1024.0:F2} MB");
            _output.WriteLine($"  Memory per File: {memoryUsed / testFiles.Length / 1024.0:F2} KB");
            
            // Test memory after forced garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var afterGcMemory = GC.GetTotalMemory(true);
            var retainedMemory = afterGcMemory - initialMemory;
            
            _output.WriteLine($"  Retained Memory: {retainedMemory / 1024.0 / 1024.0:F2} MB");
            
            CleanupTestFiles(testFiles);
        }

        private async Task RunCompilationBenchmark(IRoslynAnalyzer analyzer, string[] testFiles, string scenarioName)
        {
            var sw = Stopwatch.StartNew();
            
            // First pass - cold cache
            var coldCacheResults = new List<int>();
            foreach (var file in testFiles.Take(10)) // Sample first 10 files
            {
                var methods = await analyzer.ExtractMethodsFromFileAsync(file);
                coldCacheResults.Add(methods.Count);
            }
            
            var coldCacheTime = sw.ElapsedMilliseconds;
            sw.Restart();
            
            // Second pass - warm cache
            var warmCacheResults = new List<int>();
            foreach (var file in testFiles.Take(10)) // Same 10 files
            {
                var methods = await analyzer.ExtractMethodsFromFileAsync(file);
                warmCacheResults.Add(methods.Count);
            }
            
            var warmCacheTime = sw.ElapsedMilliseconds;
            sw.Stop();
            
            var cacheHitRatio = warmCacheTime > 0 ? (1.0 - (double)warmCacheTime / coldCacheTime) * 100 : 0;
            
            _output.WriteLine($"{scenarioName} Benchmark Results:");
            _output.WriteLine($"  Total Files: {testFiles.Length}");
            _output.WriteLine($"  Sample Size: 10");
            _output.WriteLine($"  Cold Cache Time: {coldCacheTime}ms");
            _output.WriteLine($"  Warm Cache Time: {warmCacheTime}ms");
            _output.WriteLine($"  Cache Performance Gain: {cacheHitRatio:F1}%");
            _output.WriteLine($"  Methods Found: {coldCacheResults.Sum()}");
            _output.WriteLine(string.Empty);
        }

        private string[] GenerateTestFiles(int fileCount, int linesPerFile)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "RoslynBenchmarks", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            
            var files = new List<string>();
            
            for (int i = 0; i < fileCount; i++)
            {
                var filePath = Path.Combine(tempDir, $"TestClass{i:D4}.cs");
                var content = GenerateTestFileContent($"TestClass{i:D4}", linesPerFile);
                
                File.WriteAllText(filePath, content);
                files.Add(filePath);
            }
            
            return files.ToArray();
        }

        private string GenerateTestFileContent(string className, int targetLines)
        {
            var content = new List<string>
            {
                "using System;",
                "using System.Collections.Generic;",
                "using System.Linq;",
                "using System.Threading.Tasks;",
                "",
                $"namespace TestIntelligence.Benchmarks",
                "{",
                $"    public class {className}",
                "    {"
            };

            int methodCount = Math.Max(1, targetLines / 10);
            int linesPerMethod = Math.Max(3, (targetLines - 10) / methodCount);

            for (int i = 0; i < methodCount; i++)
            {
                content.Add($"        public void Method{i}()");
                content.Add("        {");
                
                for (int j = 0; j < linesPerMethod - 2; j++)
                {
                    if (j % 3 == 0)
                    {
                        content.Add($"            var value{j} = {j};");
                    }
                    else if (j % 3 == 1)
                    {
                        content.Add($"            Method{(i + 1) % methodCount}();");
                    }
                    else
                    {
                        content.Add($"            Console.WriteLine(\"Line {j}\");");
                    }
                }
                
                content.Add("        }");
                content.Add("");
            }

            content.Add("    }");
            content.Add("}");
            
            return string.Join(Environment.NewLine, content);
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
    }

    public class TestLogger<T> : ILogger<T>
    {
        private readonly ITestOutputHelper _output;

        public TestLogger(ITestOutputHelper output)
        {
            _output = output;
        }

        public IDisposable BeginScope<TState>(TState state) => null!;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            try
            {
                _output.WriteLine($"[{logLevel}] {formatter(state, exception)}");
            }
            catch
            {
                // Ignore logging failures in tests
            }
        }
    }
}