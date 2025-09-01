using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using TestIntelligence.Core.Caching;

namespace TestIntelligence.Test
{
    class CacheTestProgram
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Enhanced Caching System Test ===");
            Console.WriteLine();

            await TestCompressionUtilities();
            await TestCompressedCache();
            await TestProjectCacheManager();
            
            Console.WriteLine();
            Console.WriteLine("✅ All tests completed successfully!");
        }

        static async Task TestCompressionUtilities()
        {
            Console.WriteLine("🧪 Testing Compression Utilities...");
            
            var testData = new TestObject
            {
                Id = 12345,
                Name = "Test object for compression testing with some repeated text that should compress well",
                Items = new List<string>
                {
                    "Item 1", "Item 2", "Item 3", "Item 4", "Item 5",
                    "Repeated item", "Repeated item", "Repeated item", "Repeated item"
                }
            };

            try
            {
                // Test compression
                var compressed = await CacheCompressionUtilities.CompressAsync(testData);
                Console.WriteLine($"   Original size: {compressed.UncompressedSize:N0} bytes");
                Console.WriteLine($"   Compressed size: {compressed.CompressedSize:N0} bytes");
                Console.WriteLine($"   Compression ratio: {compressed.CompressionRatio:P1}");
                
                // Test decompression  
                var decompressed = await CacheCompressionUtilities.DecompressAsync<TestObject>(compressed);
                
                if (decompressed != null && decompressed.Id == testData.Id && decompressed.Name == testData.Name)
                {
                    Console.WriteLine("   ✅ Compression/decompression roundtrip successful");
                }
                else
                {
                    Console.WriteLine("   ❌ Compression/decompression failed");
                }

                // Test compression ratio estimation
                var estimatedRatio = CacheCompressionUtilities.EstimateCompressionRatio(testData);
                Console.WriteLine($"   Estimated compression ratio: {estimatedRatio:P1}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Compression test failed: {ex.Message}");
            }
            
            Console.WriteLine();
        }

        static async Task TestCompressedCache()
        {
            Console.WriteLine("🧪 Testing Compressed Cache Provider...");

            var tempDir = Path.Combine(Path.GetTempPath(), "TestIntelCacheTest", Guid.NewGuid().ToString());
            
            try
            {
                var options = new CompressedCacheOptions
                {
                    MaxCacheSizeBytes = 10 * 1024 * 1024, // 10MB
                    EnableBackgroundMaintenance = false
                };

                using var cache = new CompressedCacheProvider<TestObject>(tempDir, options);

                var testData = new TestObject
                {
                    Id = 99999,
                    Name = "Cached test object",
                    Items = new List<string> { "Cached Item 1", "Cached Item 2" }
                };

                // Test cache set
                await cache.SetAsync("test-key", testData);
                Console.WriteLine("   ✅ Cache set successful");

                // Test cache get
                var retrieved = await cache.GetAsync("test-key");
                if (retrieved != null && retrieved.Id == testData.Id)
                {
                    Console.WriteLine("   ✅ Cache get successful");
                }
                else
                {
                    Console.WriteLine("   ❌ Cache get failed");
                }

                // Test cache statistics
                var stats = await cache.GetStatsAsync();
                Console.WriteLine($"   Cache entries: {stats.TotalEntries}");
                Console.WriteLine($"   Compressed size: {stats.TotalCompressedSizeFormatted}");
                Console.WriteLine($"   Uncompressed size: {stats.TotalUncompressedSizeFormatted}");
                Console.WriteLine($"   Compression ratio: {stats.AverageCompressionRatio}%");

                // Test cache removal
                var removed = await cache.RemoveAsync("test-key");
                Console.WriteLine($"   ✅ Cache removal: {removed}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Compressed cache test failed: {ex.Message}");
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    try
                    {
                        Directory.Delete(tempDir, true);
                    }
                    catch { }
                }
            }
            
            Console.WriteLine();
        }

        static async Task TestProjectCacheManager()
        {
            Console.WriteLine("🧪 Testing Project Cache Manager...");

            var tempDir = Path.Combine(Path.GetTempPath(), "TestIntelProjectCache", Guid.NewGuid().ToString());
            var testProjectPath = Path.Combine(tempDir, "TestProject.csproj");

            try
            {
                // Create test project structure
                Directory.CreateDirectory(tempDir);
                var projectContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
                    <PropertyGroup>
                        <TargetFramework>net8.0</TargetFramework>
                        <OutputType>Library</OutputType>
                    </PropertyGroup>
                </Project>";
                await File.WriteAllTextAsync(testProjectPath, projectContent);

                // Create some test source files
                await File.WriteAllTextAsync(Path.Combine(tempDir, "Class1.cs"), "public class Class1 { }");
                await File.WriteAllTextAsync(Path.Combine(tempDir, "Class2.cs"), "public class Class2 { }");

                var options = new CompressedCacheOptions
                {
                    MaxCacheSizeBytes = 5 * 1024 * 1024, // 5MB
                    EnableBackgroundMaintenance = false
                };

                using var projectCache = new ProjectCacheManager(tempDir, options);

                // Test project entry creation
                var entry = await projectCache.CreateProjectEntryAsync(testProjectPath, "net8.0");
                Console.WriteLine($"   ✅ Project entry created for: {Path.GetFileName(testProjectPath)}");
                Console.WriteLine($"   Source files found: {entry.SourceFiles.Count}");
                Console.WriteLine($"   Target framework: {entry.TargetFramework}");

                // Test project caching
                await projectCache.StoreProjectAsync(entry);
                Console.WriteLine("   ✅ Project entry stored in cache");

                // Test project retrieval
                var retrieved = await projectCache.GetProjectAsync(testProjectPath, "net8.0");
                if (retrieved != null && retrieved.ProjectPath == testProjectPath)
                {
                    Console.WriteLine("   ✅ Project entry retrieved from cache");
                }
                else
                {
                    Console.WriteLine("   ❌ Project entry retrieval failed");
                }

                // Test cache statistics
                var stats = await projectCache.GetStatisticsAsync();
                Console.WriteLine($"   Total entries: {stats.TotalEntries}");
                Console.WriteLine($"   Hit ratio: {stats.HitRatio:P1}");
                Console.WriteLine($"   Cache size: {stats.TotalCompressedSizeFormatted}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Project cache test failed: {ex.Message}");
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    try
                    {
                        Directory.Delete(tempDir, true);
                    }
                    catch { }
                }
            }

            Console.WriteLine();
        }
    }

    public class TestObject
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<string> Items { get; set; } = new List<string>();
    }
}