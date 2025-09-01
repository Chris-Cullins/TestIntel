using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Caching;
using Xunit;

namespace TestIntelligence.Core.Tests.Caching
{
    public class CompressedCacheProviderTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly ILogger<CompressedCacheProvider<TestCacheData>> _mockLogger;

        public CompressedCacheProviderTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "CompressedCacheTests", Guid.NewGuid().ToString());
            _mockLogger = new TestLogger<CompressedCacheProvider<TestCacheData>>();
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        [Fact]
        public async Task GetAsync_WithNonExistentKey_ReturnsNull()
        {
            // Arrange
            using var cache = CreateCache();

            // Act
            var result = await cache.GetAsync("non-existent-key");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task SetAsync_WithValidData_StoresCompressedData()
        {
            // Arrange
            using var cache = CreateCache();
            var testData = CreateTestData("Test Data");

            // Act
            await cache.SetAsync("test-key", testData);
            var retrieved = await cache.GetAsync("test-key");

            // Assert
            Assert.NotNull(retrieved);
            Assert.Equal(testData.Id, retrieved.Id);
            Assert.Equal(testData.Name, retrieved.Name);
            Assert.Equal(testData.Items.Count, retrieved.Items.Count);
        }

        [Fact]
        public async Task SetAsync_WithNullKey_ThrowsArgumentNullException()
        {
            // Arrange
            using var cache = CreateCache();
            var testData = CreateTestData("Test");

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => cache.SetAsync(null!, testData));
        }

        [Fact]
        public async Task SetAsync_WithNullValue_ThrowsArgumentNullException()
        {
            // Arrange
            using var cache = CreateCache();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => cache.SetAsync("test-key", null!));
        }

        [Fact]
        public async Task GetAsync_WithExpiredEntry_ReturnsNull()
        {
            // Arrange
            using var cache = CreateCache();
            var testData = CreateTestData("Expiring Data");

            // Act
            await cache.SetAsync("expiring-key", testData, TimeSpan.FromMilliseconds(100));
            await Task.Delay(200); // Wait for expiration
            var result = await cache.GetAsync("expiring-key");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task RemoveAsync_WithExistingKey_RemovesEntry()
        {
            // Arrange
            using var cache = CreateCache();
            var testData = CreateTestData("To Be Removed");
            await cache.SetAsync("remove-key", testData);

            // Act
            var removeResult = await cache.RemoveAsync("remove-key");
            var getResult = await cache.GetAsync("remove-key");

            // Assert
            Assert.True(removeResult);
            Assert.Null(getResult);
        }

        [Fact]
        public async Task RemoveAsync_WithNonExistentKey_ReturnsFalse()
        {
            // Arrange
            using var cache = CreateCache();

            // Act
            var result = await cache.RemoveAsync("non-existent");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task GetCompressedSizeAsync_WithExistingEntry_ReturnsSize()
        {
            // Arrange
            using var cache = CreateCache();
            var testData = CreateTestData("Size Test Data");
            await cache.SetAsync("size-key", testData);

            // Act
            var size = await cache.GetCompressedSizeAsync("size-key");

            // Assert
            Assert.NotNull(size);
            Assert.True(size > 0);
        }

        [Fact]
        public async Task GetCompressedSizeAsync_WithNonExistentKey_ReturnsNull()
        {
            // Arrange
            using var cache = CreateCache();

            // Act
            var size = await cache.GetCompressedSizeAsync("non-existent");

            // Assert
            Assert.Null(size);
        }

        [Fact]
        public async Task GetStatsAsync_WithMultipleEntries_ReturnsAccurateStats()
        {
            // Arrange
            using var cache = CreateCache();
            
            await cache.SetAsync("key1", CreateLargeTestData("Data 1"));
            await cache.SetAsync("key2", CreateLargeTestData("Data 2"));
            await cache.GetAsync("key1"); // Hit
            await cache.GetAsync("non-existent"); // Miss

            // Act
            var stats = await cache.GetStatsAsync();

            // Assert
            Assert.Equal(2, stats.TotalEntries);
            Assert.True(stats.TotalCompressedSize > 0);
            Assert.True(stats.TotalUncompressedSize > 0);
            Assert.Equal(1, stats.HitCount);
            Assert.Equal(1, stats.MissCount);
            Assert.True(stats.HitRatio > 0);
            Assert.True(stats.AverageCompressionRatio > 0);
        }

        [Fact]
        public async Task GetOrSetAsync_WithExistingKey_ReturnsExistingValue()
        {
            // Arrange
            using var cache = CreateCache();
            var originalData = CreateTestData("Original Data");
            await cache.SetAsync("existing-key", originalData);

            var factoryCalled = false;

            // Act
            var result = await cache.GetOrSetAsync("existing-key", async () =>
            {
                factoryCalled = true;
                return await Task.FromResult(CreateTestData("Factory Data"));
            });

            // Assert
            Assert.False(factoryCalled);
            Assert.Equal(originalData.Id, result.Id);
        }

        [Fact]
        public async Task GetOrSetAsync_WithNewKey_CallsFactory()
        {
            // Arrange
            using var cache = CreateCache();
            var factoryData = CreateTestData("Factory Data");

            // Act
            var result = await cache.GetOrSetAsync("new-key", async () => await Task.FromResult(factoryData));
            var retrieved = await cache.GetAsync("new-key");

            // Assert
            Assert.Equal(factoryData.Id, result.Id);
            Assert.NotNull(retrieved);
            Assert.Equal(factoryData.Id, retrieved.Id);
        }

        [Fact]
        public async Task ClearAsync_RemovesAllEntries()
        {
            // Arrange
            using var cache = CreateCache();
            await cache.SetAsync("key1", CreateLargeTestData("Data 1"));
            await cache.SetAsync("key2", CreateLargeTestData("Data 2"));

            // Act
            await cache.ClearAsync();
            var stats = await cache.GetStatsAsync();

            // Assert
            Assert.Equal(0, stats.TotalEntries);
            Assert.Equal(0, stats.TotalCompressedSize);
            Assert.Null(await cache.GetAsync("key1"));
            Assert.Null(await cache.GetAsync("key2"));
        }

        [Fact]
        public async Task PerformMaintenanceAsync_RemovesExpiredEntries()
        {
            // Arrange
            using var cache = CreateCache();
            await cache.SetAsync("persistent", CreateTestData("Persistent"));
            await cache.SetAsync("expiring", CreateTestData("Expiring"), TimeSpan.FromMilliseconds(100));
            
            await Task.Delay(200); // Wait for expiration

            // Act
            await cache.PerformMaintenanceAsync();

            // Assert
            Assert.NotNull(await cache.GetAsync("persistent"));
            Assert.Null(await cache.GetAsync("expiring"));
        }

        [Fact]
        public async Task StorageLimitEnforcement_EvictsOldestEntries()
        {
            // Arrange
            var options = new CompressedCacheOptions
            {
                MaxCacheSizeBytes = 1024, // Very small limit to force eviction
                EnableBackgroundMaintenance = false
            };
            using var cache = CreateCache(options);

            // Add entries that will exceed the limit
            for (int i = 0; i < 10; i++)
            {
                var largeData = CreateLargeTestData($"Large Data {i}");
                await cache.SetAsync($"large-key-{i}", largeData);
                await Task.Delay(10); // Ensure different timestamps
            }

            // Act
            await cache.PerformMaintenanceAsync();
            var stats = await cache.GetStatsAsync();

            // Assert
            Assert.True(stats.TotalCompressedSize <= options.MaxCacheSizeBytes);
            Assert.True(stats.EvictionCount > 0);
        }

        [Fact]
        public async Task CompressionEffectiveness_AchievesGoodRatio()
        {
            // Arrange
            using var cache = CreateCache();
            var repetitiveData = CreateRepetitiveTestData();

            // Act
            await cache.SetAsync("repetitive", repetitiveData);
            var stats = await cache.GetStatsAsync();

            // Assert
            Assert.True(stats.AverageCompressionRatio > 50); // Should achieve > 50% compression
            Assert.True(stats.TotalCompressedSize < stats.TotalUncompressedSize);
        }

        [Fact]
        public async Task ConcurrentAccess_HandlesMultipleOperations()
        {
            // Arrange
            using var cache = CreateCache();
            const int operationCount = 20;
            var tasks = new List<Task>();

            // Act - Perform concurrent operations
            for (int i = 0; i < operationCount; i++)
            {
                var index = i;
                tasks.Add(Task.Run(async () =>
                {
                    var data = CreateTestData($"Concurrent Data {index}");
                    await cache.SetAsync($"concurrent-{index}", data);
                }));
            }

            await Task.WhenAll(tasks);

            // Verify all entries were stored
            for (int i = 0; i < operationCount; i++)
            {
                var retrieved = await cache.GetAsync($"concurrent-{i}");
                Assert.NotNull(retrieved);
                Assert.Contains($"Concurrent Data {i}", retrieved.Name);
            }

            // Assert
            var stats = await cache.GetStatsAsync();
            Assert.Equal(operationCount, stats.TotalEntries);
        }

        [Fact]
        public async Task CacheResilience_HandlesCorruptedFiles()
        {
            // Arrange
            using var cache = CreateCache();
            var testData = CreateTestData("Test Data");
            await cache.SetAsync("test", testData);

            // Corrupt the cache file by writing invalid data
            var cacheFiles = Directory.GetFiles(_tempDirectory, "*.cache");
            if (cacheFiles.Length > 0)
            {
                await File.WriteAllTextAsync(cacheFiles[0], "corrupted data");
            }

            // Act - Should handle gracefully
            var result = await cache.GetAsync("test");

            // Assert
            Assert.Null(result); // Should return null for corrupted data
        }

        private CompressedCacheProvider<TestCacheData> CreateCache(CompressedCacheOptions? options = null)
        {
            return new CompressedCacheProvider<TestCacheData>(_tempDirectory, options, _mockLogger);
        }

        private static TestCacheData CreateTestData(string name)
        {
            return new TestCacheData
            {
                Id = Random.Shared.Next(1, 1000),
                Name = name,
                Items = new List<string> { "Item1", "Item2", "Item3" }
            };
        }

        private static TestCacheData CreateLargeTestData(string name)
        {
            return new TestCacheData
            {
                Id = Random.Shared.Next(1, 1000),
                Name = name,
                Items = Enumerable.Repeat("Large item with lots of text to increase size", 50).ToList()
            };
        }

        private static TestCacheData CreateRepetitiveTestData()
        {
            return new TestCacheData
            {
                Id = 999,
                Name = "Repetitive data for compression testing",
                Items = Enumerable.Repeat("This repeated text should compress very well", 100).ToList()
            };
        }

        public class TestCacheData
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public List<string> Items { get; set; } = new();
        }

        private class TestLogger<T> : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => false;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
        }
    }
}