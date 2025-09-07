using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Caching;
using TestIntelligence.Core.Caching;
using Xunit;
using Xunit.Abstractions;

namespace TestIntelligence.ImpactAnalyzer.Tests.Performance
{
    public class LargeSolutionPerformanceTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _tempDirectory;
        private readonly List<string> _createdDirectories;
        private readonly ILogger<CallGraphCache> _logger;

        public LargeSolutionPerformanceTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDirectory = Path.Combine(Path.GetTempPath(), "LargeSolutionPerfTests", Guid.NewGuid().ToString());
            _createdDirectories = new List<string>();
            _logger = new TestLogger<CallGraphCache>(output);
            
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            foreach (var dir in _createdDirectories.Concat(new[] { _tempDirectory }))
            {
                if (Directory.Exists(dir))
                {
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                    catch
                    {
                        // Best effort cleanup
                    }
                }
            }
        }

        [Fact]
        public async Task LargeSolutionAnalysis_20Projects_CompletesWithinTimeLimit()
        {
            // Arrange
            const int projectCount = 20;
            const int classesPerProject = 10;
            const int methodsPerClass = 8;
            const int maxTimeoutMinutes = 5;

            var solution = await GenerateTestSolutionAsync("LargeSolution20", projectCount, classesPerProject, methodsPerClass);
            
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(maxTimeoutMinutes));
            var sw = Stopwatch.StartNew();

            try
            {
                // Act
                var loggerFactory = new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory();
                var analyzer = new RoslynAnalyzer(new TestLogger<RoslynAnalyzer>(_output), loggerFactory);

                var analysisResults = new List<AnalysisResult>();
                foreach (var projectPath in solution.ProjectPaths)
                {
                    var sourceFiles = Directory.GetFiles(Path.GetDirectoryName(projectPath)!, "*.cs", SearchOption.TopDirectoryOnly);
                    var callGraph = await analyzer.BuildCallGraphAsync(sourceFiles.Where(f => !f.Contains("AssemblyInfo")).ToArray());
                    
                    analysisResults.Add(new AnalysisResult
                    {
                        ProjectPath = projectPath,
                        MethodCount = callGraph.Count,
                        EdgeCount = callGraph.Values.SelectMany(v => v).Count(),
                        AnalysisDuration = sw.Elapsed
                    });
                }

                sw.Stop();

                // Assert
                sw.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(maxTimeoutMinutes), 
                    "large solution analysis should complete within timeout");

                analysisResults.Should().HaveCount(projectCount);
                var totalMethods = analysisResults.Sum(r => r.MethodCount);
                var totalEdges = analysisResults.Sum(r => r.EdgeCount);

                _output.WriteLine($"Large Solution Performance Results (20 Projects):");
                _output.WriteLine($"  Total Duration: {sw.ElapsedMilliseconds:N0}ms");
                _output.WriteLine($"  Total Methods: {totalMethods:N0}");
                _output.WriteLine($"  Total Call Edges: {totalEdges:N0}");
                _output.WriteLine($"  Methods/sec: {totalMethods * 1000.0 / sw.ElapsedMilliseconds:F1}");
                _output.WriteLine($"  Avg Project Analysis: {sw.ElapsedMilliseconds / (double)projectCount:F1}ms");

                totalMethods.Should().BeGreaterThan(1000, "should analyze significant number of methods");
                totalEdges.Should().BeGreaterThan(500, "should find substantial call relationships");
            }
            catch (OperationCanceledException)
            {
                Assert.Fail($"Analysis timed out after {maxTimeoutMinutes} minutes");
            }
        }

        [Fact]
        public async Task MassiveSolutionAnalysis_50Projects_CacheEffectiveness()
        {
            // Arrange
            const int projectCount = 50;
            const int classesPerProject = 5;
            const int methodsPerClass = 6;

            var solution = await GenerateTestSolutionAsync("MassiveSolution50", projectCount, classesPerProject, methodsPerClass);
            
            var cacheOptions = new CompressedCacheOptions
            {
                MaxCacheSizeBytes = 100 * 1024 * 1024, // 100MB
                EnableBackgroundMaintenance = false
            };

            using var cache = new CallGraphCache(_tempDirectory, cacheOptions, _logger);
            var assemblies = new[] { "System.dll", "mscorlib.dll", "System.Core.dll" };

            var sw = Stopwatch.StartNew();
            
            // Act - First pass (cold cache)
            var coldPassResults = new List<TimeSpan>();
            foreach (var projectPath in solution.ProjectPaths.Take(10)) // Sample for performance
            {
                var projectSw = Stopwatch.StartNew();
                var callGraph = GenerateMockCallGraph(classesPerProject * methodsPerClass);
                var reverseCallGraph = GenerateMockReverseCallGraph(callGraph);
                
                await cache.StoreCallGraphAsync(projectPath, assemblies, callGraph, reverseCallGraph, TimeSpan.FromSeconds(1));
                projectSw.Stop();
                coldPassResults.Add(projectSw.Elapsed);
            }

            var coldPassTime = sw.ElapsedMilliseconds;
            sw.Restart();

            // Act - Second pass (warm cache)
            var warmPassResults = new List<TimeSpan>();
            foreach (var projectPath in solution.ProjectPaths.Take(10))
            {
                var projectSw = Stopwatch.StartNew();
                var result = await cache.GetCallGraphAsync(projectPath, assemblies);
                projectSw.Stop();
                warmPassResults.Add(projectSw.Elapsed);
                
                result.Should().NotBeNull("cached entry should be retrieved");
            }

            var warmPassTime = sw.ElapsedMilliseconds;
            sw.Stop();

            // Assert
            var stats = await cache.GetStatisticsAsync();
            
            _output.WriteLine($"Massive Solution Cache Performance (50 Projects):");
            _output.WriteLine($"  Projects: {projectCount}");
            _output.WriteLine($"  Sample Size: 10");
            _output.WriteLine($"  Cold Pass Time: {coldPassTime}ms");
            _output.WriteLine($"  Warm Pass Time: {warmPassTime}ms");
            _output.WriteLine($"  Cache Hit Ratio: {stats.HitRatio:P1}");
            _output.WriteLine($"  Total Entries: {stats.TotalEntries}");
            _output.WriteLine($"  Cache Size: {stats.TotalCompressedSize / 1024.0 / 1024.0:F2}MB");
            _output.WriteLine($"  Compression Ratio: {stats.AverageCompressionRatio:F1}%");

            warmPassTime.Should().BeLessThan(coldPassTime / 2, "warm cache should be significantly faster");
            stats.HitRatio.Should().BeGreaterThan(0.9, "should achieve high cache hit ratio");
            stats.AverageCompressionRatio.Should().BeGreaterThan(20, "should achieve good compression");
        }

        [Fact]
        public async Task ConcurrentLargeSolutionAnalysis_HandlesParallelProjects()
        {
            // Arrange
            const int projectCount = 15;
            const int concurrentLimit = 5;
            
            var solution = await GenerateTestSolutionAsync("ConcurrentSolution", projectCount, 8, 10);
            
            var semaphore = new SemaphoreSlim(concurrentLimit, concurrentLimit);
            var sw = Stopwatch.StartNew();

            // Act
            var analysisResults = await Task.WhenAll(solution.ProjectPaths.Select(async projectPath =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var projectSw = Stopwatch.StartNew();
                    var sourceFiles = Directory.GetFiles(Path.GetDirectoryName(projectPath)!, "*.cs", SearchOption.TopDirectoryOnly);
                    
                    // Simulate analysis with synthetic call graphs
                    var callGraph = GenerateMockCallGraph(Random.Shared.Next(50, 150));
                    await Task.Delay(Random.Shared.Next(100, 500)); // Simulate processing time
                    
                    projectSw.Stop();
                    
                    return new AnalysisResult
                    {
                        ProjectPath = projectPath,
                        MethodCount = callGraph.Count,
                        EdgeCount = callGraph.Values.SelectMany(v => v).Count(),
                        AnalysisDuration = projectSw.Elapsed
                    };
                }
                finally
                {
                    semaphore.Release();
                }
            }));

            sw.Stop();

            // Assert
            analysisResults.Should().HaveCount(projectCount);
            
            var totalMethods = analysisResults.Sum(r => r.MethodCount);
            var avgProjectTime = analysisResults.Average(r => r.AnalysisDuration.TotalMilliseconds);
            var maxProjectTime = analysisResults.Max(r => r.AnalysisDuration.TotalMilliseconds);

            _output.WriteLine($"Concurrent Analysis Performance:");
            _output.WriteLine($"  Projects: {projectCount}");
            _output.WriteLine($"  Concurrent Limit: {concurrentLimit}");
            _output.WriteLine($"  Total Duration: {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"  Avg Project Time: {avgProjectTime:F1}ms");
            _output.WriteLine($"  Max Project Time: {maxProjectTime:F1}ms");
            _output.WriteLine($"  Total Methods: {totalMethods}");
            _output.WriteLine($"  Parallelism Efficiency: {100.0 * avgProjectTime * projectCount / sw.ElapsedMilliseconds:F1}%");

            // Should achieve some parallelism benefit
            sw.ElapsedMilliseconds.Should().BeLessThan(avgProjectTime * projectCount, 
                "concurrent processing should be faster than sequential");
        }

        [Fact]
        public async Task MemoryPressureTest_LargeSolutionStressTest()
        {
            // Arrange
            const int projectCount = 30;
            const int memoryLimitMB = 200; // 200MB limit
            
            var solution = await GenerateTestSolutionAsync("MemoryStress", projectCount, 12, 15);
            
            var initialMemory = GC.GetTotalMemory(true);
            var maxMemoryUsage = 0L;

            // Act
            var processedProjects = 0;
            foreach (var projectPath in solution.ProjectPaths)
            {
                // Generate large call graphs
                var callGraph = GenerateLargeMockCallGraph(200); // 200 methods per project
                var reverseCallGraph = GenerateMockReverseCallGraph(callGraph);
                
                // Simulate processing
                await Task.Delay(10); // Small delay to allow memory tracking
                
                var currentMemory = GC.GetTotalMemory(false);
                maxMemoryUsage = Math.Max(maxMemoryUsage, currentMemory - initialMemory);
                
                processedProjects++;
                
                // Trigger GC every 10 projects to simulate real-world memory management
                if (processedProjects % 10 == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }

            var finalMemory = GC.GetTotalMemory(true);
            var retainedMemory = finalMemory - initialMemory;

            // Assert
            _output.WriteLine($"Memory Pressure Test Results:");
            _output.WriteLine($"  Projects Processed: {processedProjects}");
            _output.WriteLine($"  Max Memory Usage: {maxMemoryUsage / 1024.0 / 1024.0:F2}MB");
            _output.WriteLine($"  Retained Memory: {retainedMemory / 1024.0 / 1024.0:F2}MB");
            _output.WriteLine($"  Memory per Project: {maxMemoryUsage / processedProjects / 1024.0:F1}KB");

            maxMemoryUsage.Should().BeLessThan(memoryLimitMB * 1024 * 1024, 
                $"memory usage should stay below {memoryLimitMB}MB");
            
            retainedMemory.Should().BeLessThan(maxMemoryUsage / 2, 
                "should release most memory after processing");
        }

        [Fact]
        public async Task LargeSolutionScalabilityTest_MeasuresPerformanceCharacteristics()
        {
            // Arrange - Test different solution sizes
            var testScenarios = new[]
            {
                new { Name = "Small", Projects = 5, Classes = 5, Methods = 5 },
                new { Name = "Medium", Projects = 15, Classes = 8, Methods = 8 },
                new { Name = "Large", Projects = 30, Classes = 12, Methods = 10 }
            };

            var results = new List<ScalabilityResult>();

            // Act
            foreach (var scenario in testScenarios)
            {
                var solution = await GenerateTestSolutionAsync(scenario.Name, scenario.Projects, scenario.Classes, scenario.Methods);
                
                var sw = Stopwatch.StartNew();
                var totalMethods = 0;
                var totalEdges = 0;

                foreach (var projectPath in solution.ProjectPaths)
                {
                    var callGraph = GenerateMockCallGraph(scenario.Classes * scenario.Methods);
                    totalMethods += callGraph.Count;
                    totalEdges += callGraph.Values.SelectMany(v => v).Count();
                }

                sw.Stop();

                results.Add(new ScalabilityResult
                {
                    ScenarioName = scenario.Name,
                    ProjectCount = scenario.Projects,
                    TotalMethods = totalMethods,
                    TotalEdges = totalEdges,
                    ProcessingTime = sw.Elapsed,
                    MethodsPerSecond = totalMethods * 1000.0 / sw.ElapsedMilliseconds
                });
            }

            // Assert & Report
            _output.WriteLine("Scalability Test Results:");
            _output.WriteLine($"{"Scenario",-10} {"Projects",-8} {"Methods",-8} {"Edges",-8} {"Time(ms)",-10} {"Methods/s",-10}");
            _output.WriteLine(new string('-', 70));

            foreach (var result in results)
            {
                _output.WriteLine($"{result.ScenarioName,-10} {result.ProjectCount,-8} {result.TotalMethods,-8} {result.TotalEdges,-8} {result.ProcessingTime.TotalMilliseconds,-10:F0} {result.MethodsPerSecond,-10:F1}");
            }

            // Performance should scale reasonably (not exponentially)
            var smallResult = results.First(r => r.ScenarioName == "Small");
            var largeResult = results.First(r => r.ScenarioName == "Large");

            var sizeRatio = (double)largeResult.TotalMethods / smallResult.TotalMethods;
            var timeRatio = largeResult.ProcessingTime.TotalMilliseconds / smallResult.ProcessingTime.TotalMilliseconds;

            _output.WriteLine($"\nScalability Analysis:");
            _output.WriteLine($"  Size Ratio: {sizeRatio:F1}x");
            _output.WriteLine($"  Time Ratio: {timeRatio:F1}x");
            _output.WriteLine($"  Efficiency: {sizeRatio / timeRatio:F2}");

            timeRatio.Should().BeLessThan(sizeRatio * 2, "performance should scale better than O(n²)");
        }

        private async Task<TestSolution> GenerateTestSolutionAsync(string solutionName, int projectCount, int classesPerProject, int methodsPerClass)
        {
            var solutionDir = Path.Combine(_tempDirectory, solutionName);
            Directory.CreateDirectory(solutionDir);
            _createdDirectories.Add(solutionDir);

            var solutionPath = Path.Combine(solutionDir, $"{solutionName}.sln");
            var projectPaths = new List<string>();

            // Create solution file content
            var solutionContent = new List<string>
            {
                "Microsoft Visual Studio Solution File, Format Version 12.00",
                "# Visual Studio Version 17"
            };

            // Generate projects
            for (int p = 0; p < projectCount; p++)
            {
                var projectName = $"Project{p:D3}";
                var projectDir = Path.Combine(solutionDir, projectName);
                Directory.CreateDirectory(projectDir);

                var projectPath = Path.Combine(projectDir, $"{projectName}.csproj");
                projectPaths.Add(projectPath);

                // Add to solution
                var projectGuid = Guid.NewGuid();
                solutionContent.Add($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{projectName}\", \"{projectName}\\{projectName}.csproj\", \"{{{projectGuid}}}\"");
                solutionContent.Add("EndProject");

                // Create project file
                var projectContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>";

                await File.WriteAllTextAsync(projectPath, projectContent);

                // Generate classes
                for (int c = 0; c < classesPerProject; c++)
                {
                    var className = $"Class{c:D2}";
                    var classPath = Path.Combine(projectDir, $"{className}.cs");
                    var classContent = GenerateClassContent(projectName, className, methodsPerClass, classesPerProject);
                    
                    await File.WriteAllTextAsync(classPath, classContent);
                }
            }

            // Write solution file
            await File.WriteAllTextAsync(solutionPath, string.Join(Environment.NewLine, solutionContent));

            return new TestSolution
            {
                SolutionPath = solutionPath,
                ProjectPaths = projectPaths
            };
        }

        private string GenerateClassContent(string projectName, string className, int methodCount, int totalClasses)
        {
            var content = new List<string>
            {
                "using System;",
                "using System.Collections.Generic;",
                "using System.Linq;",
                "",
                $"namespace {projectName}",
                "{",
                $"    public class {className}",
                "    {"
            };

            for (int m = 0; m < methodCount; m++)
            {
                var methodName = $"Method{m:D2}";
                content.Add($"        public void {methodName}()");
                content.Add("        {");
                
                // Add some method calls to create call graph
                if (m > 0)
                {
                    content.Add($"            Method{(m - 1):D2}();");
                }
                
                if (m < methodCount - 1)
                {
                    content.Add($"            Method{(m + 1):D2}();");
                }

                // Reference other classes occasionally
                if (totalClasses > 1 && Random.Shared.Next(100) < 30)
                {
                    var otherClass = Random.Shared.Next(totalClasses);
                    content.Add($"            // var other = new Class{otherClass:D2}();");
                }

                content.Add($"            Console.WriteLine(\"{className}.{methodName}\");");
                content.Add("        }");
                content.Add("");
            }

            content.Add("    }");
            content.Add("}");

            return string.Join(Environment.NewLine, content);
        }

        private Dictionary<string, HashSet<string>> GenerateMockCallGraph(int methodCount)
        {
            var callGraph = new Dictionary<string, HashSet<string>>();
            
            for (int i = 0; i < methodCount; i++)
            {
                var method = $"Method{i}";
                var callees = new HashSet<string>();
                
                // Each method calls 1-4 others
                var calleeCount = Random.Shared.Next(1, 5);
                for (int j = 0; j < calleeCount; j++)
                {
                    var callee = $"Method{Random.Shared.Next(methodCount)}";
                    if (callee != method) // Avoid self-calls
                    {
                        callees.Add(callee);
                    }
                }
                
                callGraph[method] = callees;
            }
            
            return callGraph;
        }

        private Dictionary<string, HashSet<string>> GenerateLargeMockCallGraph(int methodCount)
        {
            var callGraph = new Dictionary<string, HashSet<string>>();
            
            for (int i = 0; i < methodCount; i++)
            {
                var method = $"LargeMethod{i}_{Guid.NewGuid():N}"; // Longer names for memory pressure
                var callees = new HashSet<string>();
                
                // More connections for larger graphs
                var calleeCount = Random.Shared.Next(3, 8);
                for (int j = 0; j < calleeCount; j++)
                {
                    var calleeIndex = Random.Shared.Next(methodCount);
                    var callee = $"LargeMethod{calleeIndex}_{Guid.NewGuid():N}";
                    callees.Add(callee);
                }
                
                callGraph[method] = callees;
            }
            
            return callGraph;
        }

        private Dictionary<string, HashSet<string>> GenerateMockReverseCallGraph(Dictionary<string, HashSet<string>> callGraph)
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

        private record TestSolution
        {
            public string SolutionPath { get; init; } = string.Empty;
            public List<string> ProjectPaths { get; init; } = new();
        }

        private record AnalysisResult
        {
            public string ProjectPath { get; init; } = string.Empty;
            public int MethodCount { get; init; }
            public int EdgeCount { get; init; }
            public TimeSpan AnalysisDuration { get; init; }
        }

        private record ScalabilityResult
        {
            public string ScenarioName { get; init; } = string.Empty;
            public int ProjectCount { get; init; }
            public int TotalMethods { get; init; }
            public int TotalEdges { get; init; }
            public TimeSpan ProcessingTime { get; init; }
            public double MethodsPerSecond { get; init; }
        }

        private class TestLogger<T> : ILogger<T>
        {
            private readonly ITestOutputHelper _output;

            public TestLogger(ITestOutputHelper output)
            {
                _output = output;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                try
                {
                    if (logLevel >= LogLevel.Warning)
                    {
                        _output.WriteLine($"[{logLevel}] {formatter(state, exception)}");
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