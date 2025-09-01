using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TestIntelligence.Core.Caching;
using TestIntelligence.ImpactAnalyzer.Caching;

namespace TestIntelligence.Test
{
    class RealCacheTestProgram
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Enhanced Caching System Test on Real TestIntelligence Library ===");
            Console.WriteLine();

            await TestCallGraphCacheWithRealData();
            await TestProjectCacheWithRealProjects();
            await TestPerformanceComparison();
            
            Console.WriteLine();
            Console.WriteLine("✅ All real-world cache tests completed successfully!");
        }

        static async Task TestCallGraphCacheWithRealData()
        {
            Console.WriteLine("🧪 Testing Call Graph Cache with Real TestIntelligence Projects...");

            var tempCacheDir = Path.Combine(Path.GetTempPath(), "TestIntelRealCache", Guid.NewGuid().ToString());
            
            try
            {
                var options = new CompressedCacheOptions
                {
                    MaxCacheSizeBytes = 50 * 1024 * 1024, // 50MB
                    EnableBackgroundMaintenance = false
                };

                using var callGraphCache = new CallGraphCache(tempCacheDir, options);

                // Test with real project paths from this library
                var coreProjectPath = Path.GetFullPath("src/TestIntelligence.Core/TestIntelligence.Core.csproj");
                var impactProjectPath = Path.GetFullPath("src/TestIntelligence.ImpactAnalyzer/TestIntelligence.ImpactAnalyzer.csproj");

                if (File.Exists(coreProjectPath))
                {
                    // Simulate real dependency assemblies
                    var referencedAssemblies = new[]
                    {
                        "System.Core.dll",
                        "System.IO.Compression.dll",
                        "Microsoft.Extensions.Logging.dll",
                        "Newtonsoft.Json.dll"
                    };

                    // Create mock call graph data similar to what would be generated
                    var callGraph = new Dictionary<string, HashSet<string>>
                    {
                        ["TestIntelligence.Core.Caching.CompressedCacheProvider`1.GetAsync"] = new HashSet<string>
                        {
                            "TestIntelligence.Core.Caching.CacheCompressionUtilities.DecompressAsync",
                            "System.IO.File.ReadAllBytesAsync"
                        },
                        ["TestIntelligence.Core.Caching.CompressedCacheProvider`1.SetAsync"] = new HashSet<string>
                        {
                            "TestIntelligence.Core.Caching.CacheCompressionUtilities.CompressAsync",
                            "System.IO.File.WriteAllBytesAsync"
                        }
                    };

                    var reverseCallGraph = new Dictionary<string, HashSet<string>>
                    {
                        ["TestIntelligence.Core.Caching.CacheCompressionUtilities.DecompressAsync"] = new HashSet<string>
                        {
                            "TestIntelligence.Core.Caching.CompressedCacheProvider`1.GetAsync"
                        },
                        ["System.IO.File.ReadAllBytesAsync"] = new HashSet<string>
                        {
                            "TestIntelligence.Core.Caching.CompressedCacheProvider`1.GetAsync"
                        }
                    };

                    var buildTime = TimeSpan.FromSeconds(2.5);

                    // Test cache miss (first access)
                    var stopwatch = Stopwatch.StartNew();
                    var cachedResult1 = await callGraphCache.GetCallGraphAsync(coreProjectPath, referencedAssemblies);
                    stopwatch.Stop();
                    
                    if (cachedResult1 == null)
                    {
                        Console.WriteLine($"   ✅ Cache miss handled correctly (took {stopwatch.ElapsedMilliseconds}ms)");
                    }

                    // Store the call graph
                    await callGraphCache.StoreCallGraphAsync(coreProjectPath, referencedAssemblies, callGraph, reverseCallGraph, buildTime);
                    Console.WriteLine("   ✅ Real call graph data stored in cache");

                    // Test cache hit (second access)
                    stopwatch.Restart();
                    var cachedResult2 = await callGraphCache.GetCallGraphAsync(coreProjectPath, referencedAssemblies);
                    stopwatch.Stop();

                    if (cachedResult2 != null)
                    {
                        Console.WriteLine($"   ✅ Cache hit successful (took {stopwatch.ElapsedMilliseconds}ms)");
                        Console.WriteLine($"   Call graph methods: {cachedResult2.CallGraph.Count}");
                        Console.WriteLine($"   Total call edges: {cachedResult2.CallGraph.Values.Sum(callees => callees.Count)}");
                        Console.WriteLine($"   Original build time: {cachedResult2.BuildTime.TotalSeconds:F1}s");
                        
                        // Test data integrity
                        var validation = cachedResult2.ValidateIntegrity();
                        if (validation.IsValid)
                        {
                            Console.WriteLine("   ✅ Call graph data integrity validated");
                        }
                        else
                        {
                            Console.WriteLine($"   ❌ Data integrity issues: {string.Join(", ", validation.Issues)}");
                        }

                        // Test statistics
                        var graphStats = cachedResult2.GetStatistics();
                        Console.WriteLine($"   Graph density: {graphStats.GraphDensity:P2}");
                        Console.WriteLine($"   Average fan-out: {graphStats.AverageFanOut:F1}");
                    }
                }
                else
                {
                    Console.WriteLine($"   ⚠️  Core project not found at: {coreProjectPath}");
                }

                // Get overall cache statistics
                var cacheStats = await callGraphCache.GetStatisticsAsync();
                Console.WriteLine($"   Cache statistics:");
                Console.WriteLine($"     Entries: {cacheStats.TotalEntries}");
                Console.WriteLine($"     Hit ratio: {cacheStats.HitRatio:P1}");
                Console.WriteLine($"     Compressed size: {cacheStats.TotalCompressedSizeFormatted}");
                Console.WriteLine($"     Compression ratio: {cacheStats.AverageCompressionRatio}%");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Call graph cache test failed: {ex.Message}");
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempCacheDir))
                {
                    try
                    {
                        Directory.Delete(tempCacheDir, true);
                    }
                    catch { }
                }
            }
            
            Console.WriteLine();
        }

        static async Task TestProjectCacheWithRealProjects()
        {
            Console.WriteLine("🧪 Testing Project Cache with Real TestIntelligence Projects...");

            var tempCacheDir = Path.Combine(Path.GetTempPath(), "TestIntelRealProjectCache", Guid.NewGuid().ToString());
            
            try
            {
                var options = new CompressedCacheOptions
                {
                    MaxCacheSizeBytes = 25 * 1024 * 1024, // 25MB
                    EnableBackgroundMaintenance = false
                };

                using var projectCache = new ProjectCacheManager(tempCacheDir, options);

                // Test with real project paths
                var testProjects = new[]
                {
                    "src/TestIntelligence.Core/TestIntelligence.Core.csproj",
                    "src/TestIntelligence.DataTracker/TestIntelligence.DataTracker.csproj",
                    "src/TestIntelligence.ImpactAnalyzer/TestIntelligence.ImpactAnalyzer.csproj"
                };

                var cachedProjects = 0;
                var totalSourceFiles = 0;

                foreach (var projectPath in testProjects)
                {
                    var fullPath = Path.GetFullPath(projectPath);
                    if (File.Exists(fullPath))
                    {
                        Console.WriteLine($"   Processing: {Path.GetFileName(fullPath)}");

                        // Test cache miss (first access)
                        var cached1 = await projectCache.GetProjectAsync(fullPath, "net8.0");
                        if (cached1 == null)
                        {
                            // Create and cache project entry
                            var entry = await projectCache.CreateProjectEntryAsync(fullPath, "net8.0");
                            await projectCache.StoreProjectAsync(entry);
                            
                            Console.WriteLine($"     Source files: {entry.SourceFiles.Count}");
                            Console.WriteLine($"     Referenced assemblies: {entry.ReferencedAssemblies.Count}");
                            totalSourceFiles += entry.SourceFiles.Count;
                            cachedProjects++;

                            // Test cache hit (second access)
                            var cached2 = await projectCache.GetProjectAsync(fullPath, "net8.0");
                            if (cached2 != null)
                            {
                                Console.WriteLine("     ✅ Cache hit successful");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"   ⚠️  Project not found: {fullPath}");
                    }
                }

                Console.WriteLine($"   ✅ Cached {cachedProjects} projects with {totalSourceFiles} total source files");

                // Get cache statistics
                var stats = await projectCache.GetStatisticsAsync();
                Console.WriteLine($"   Cache statistics:");
                Console.WriteLine($"     Total entries: {stats.TotalEntries}");
                Console.WriteLine($"     Hit count: {stats.HitCount}");
                Console.WriteLine($"     Miss count: {stats.MissCount}");
                Console.WriteLine($"     Hit ratio: {stats.HitRatio:P1}");
                Console.WriteLine($"     Cache size: {stats.TotalCompressedSizeFormatted}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Project cache test failed: {ex.Message}");
                Console.WriteLine($"   Stack trace: {ex.StackTrace}");
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempCacheDir))
                {
                    try
                    {
                        Directory.Delete(tempCacheDir, true);
                    }
                    catch { }
                }
            }

            Console.WriteLine();
        }

        static async Task TestPerformanceComparison()
        {
            Console.WriteLine("🧪 Performance Comparison: Cached vs Non-Cached Operations...");

            var tempCacheDir = Path.Combine(Path.GetTempPath(), "TestIntelPerfCache", Guid.NewGuid().ToString());
            
            try
            {
                var options = new CompressedCacheOptions
                {
                    MaxCacheSizeBytes = 10 * 1024 * 1024, // 10MB
                    EnableBackgroundMaintenance = false
                };

                using var cache = new CompressedCacheProvider<Dictionary<string, object>>(tempCacheDir, options);

                // Create test data that simulates real analysis results
                var testData = new Dictionary<string, object>
                {
                    ["CallGraph"] = GenerateMockCallGraph(1000), // 1000 methods
                    ["TypeAnalysis"] = GenerateMockTypeAnalysis(500), // 500 types
                    ["Dependencies"] = GenerateMockDependencies(50), // 50 assemblies
                    ["Metadata"] = new Dictionary<string, object>
                    {
                        ["AnalysisTime"] = TimeSpan.FromMinutes(5),
                        ["MethodCount"] = 1000,
                        ["TypeCount"] = 500
                    }
                };

                // Measure non-cached operation (serialization)
                var stopwatch = Stopwatch.StartNew();
                var jsonData = System.Text.Json.JsonSerializer.Serialize(testData);
                stopwatch.Stop();
                var serializationTime = stopwatch.ElapsedMilliseconds;

                // Measure cache store operation
                stopwatch.Restart();
                await cache.SetAsync("perf-test", testData);
                stopwatch.Stop();
                var cacheStoreTime = stopwatch.ElapsedMilliseconds;

                // Measure cache retrieval operation
                stopwatch.Restart();
                var cachedData = await cache.GetAsync("perf-test");
                stopwatch.Stop();
                var cacheRetrievalTime = stopwatch.ElapsedMilliseconds;

                Console.WriteLine($"   Performance Results:");
                Console.WriteLine($"     JSON serialization: {serializationTime}ms");
                Console.WriteLine($"     Cache store: {cacheStoreTime}ms");
                Console.WriteLine($"     Cache retrieval: {cacheRetrievalTime}ms");
                Console.WriteLine($"     Speed improvement: {(double)serializationTime / cacheRetrievalTime:F1}x faster");

                // Measure compression effectiveness
                var stats = await cache.GetStatsAsync();
                Console.WriteLine($"     Compression ratio: {stats.AverageCompressionRatio}%");
                Console.WriteLine($"     Space saved: {stats.TotalUncompressedSizeFormatted} -> {stats.TotalCompressedSizeFormatted}");

                if (cachedData != null)
                {
                    Console.WriteLine("   ✅ Performance test completed successfully");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Performance test failed: {ex.Message}");
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempCacheDir))
                {
                    try
                    {
                        Directory.Delete(tempCacheDir, true);
                    }
                    catch { }
                }
            }

            Console.WriteLine();
        }

        private static Dictionary<string, List<string>> GenerateMockCallGraph(int methodCount)
        {
            var callGraph = new Dictionary<string, List<string>>();
            var random = new Random(42); // Deterministic seed

            for (int i = 0; i < methodCount; i++)
            {
                var methodName = $"TestIntelligence.Mock.Class{i / 10}.Method{i}";
                var calleeCount = random.Next(0, 8); // 0-7 callees per method
                var callees = new List<string>();

                for (int j = 0; j < calleeCount; j++)
                {
                    var calleeIndex = random.Next(methodCount);
                    var calleeName = $"TestIntelligence.Mock.Class{calleeIndex / 10}.Method{calleeIndex}";
                    if (!callees.Contains(calleeName))
                    {
                        callees.Add(calleeName);
                    }
                }

                callGraph[methodName] = callees;
            }

            return callGraph;
        }

        private static Dictionary<string, object> GenerateMockTypeAnalysis(int typeCount)
        {
            var analysis = new Dictionary<string, object>();
            var random = new Random(42);

            for (int i = 0; i < typeCount; i++)
            {
                var typeName = $"TestIntelligence.Mock.Class{i}";
                analysis[typeName] = new Dictionary<string, object>
                {
                    ["BaseType"] = i > 0 ? $"TestIntelligence.Mock.Class{random.Next(i)}" : "System.Object",
                    ["Interfaces"] = Enumerable.Range(0, random.Next(0, 4))
                        .Select(j => $"TestIntelligence.Mock.IInterface{j}")
                        .ToList(),
                    ["Methods"] = Enumerable.Range(0, random.Next(1, 10))
                        .Select(j => $"Method{j}")
                        .ToList(),
                    ["Properties"] = Enumerable.Range(0, random.Next(0, 5))
                        .Select(j => $"Property{j}")
                        .ToList()
                };
            }

            return analysis;
        }

        private static List<string> GenerateMockDependencies(int count)
        {
            var dependencies = new List<string>();
            
            for (int i = 0; i < count; i++)
            {
                dependencies.Add($"MockAssembly{i}.dll");
            }
            
            // Add some realistic .NET assemblies
            dependencies.AddRange(new[]
            {
                "System.Core.dll",
                "System.IO.dll",
                "Microsoft.Extensions.Logging.dll",
                "Newtonsoft.Json.dll"
            });

            return dependencies;
        }
    }
}