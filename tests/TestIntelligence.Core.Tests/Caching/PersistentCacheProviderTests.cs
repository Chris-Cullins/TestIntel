using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Caching;
using Xunit;

namespace TestIntelligence.Core.Tests.Caching
{
    public class PersistentCacheProviderTests : IDisposable
    {
        private readonly string _tempCacheDir;
        private readonly PersistentCacheProvider _cache;

        public PersistentCacheProviderTests()
        {
            _tempCacheDir = Path.Combine(Path.GetTempPath(), "TestIntelligence", "Tests", Guid.NewGuid().ToString());
            _cache = new PersistentCacheProvider(_tempCacheDir);
        }

        [Fact]
        public async Task GetAsync_WhenKeyDoesNotExist_ReturnsNull()
        {
            // Arrange
            var key = "non-existent-key";

            // Act
            var result = await _cache.GetAsync<TestData>(key);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task SetAndGetAsync_WhenValueExists_ReturnsValue()
        {
            // Arrange
            var key = "test-key";
            var testData = new TestData { Id = 1, Name = "Test" };

            // Act
            await _cache.SetAsync(key, testData);
            var result = await _cache.GetAsync<TestData>(key);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(testData.Id, result.Id);
            Assert.Equal(testData.Name, result.Name);
        }

        [Fact]
        public async Task GetAsync_WhenEntryExpired_ReturnsNull()
        {
            // Arrange
            var key = "expired-key";
            var testData = new TestData { Id = 2, Name = "Expired" };
            var shortExpiration = TimeSpan.FromMilliseconds(10);

            // Act
            await _cache.SetAsync(key, testData, shortExpiration);
            await Task.Delay(50); // Wait for expiration
            var result = await _cache.GetAsync<TestData>(key);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task ExistsAsync_WhenKeyExists_ReturnsTrue()
        {
            // Arrange
            var key = "existing-key";
            var testData = new TestData { Id = 3, Name = "Existing" };

            // Act
            await _cache.SetAsync(key, testData);
            var exists = await _cache.ExistsAsync(key);

            // Assert
            Assert.True(exists);
        }

        [Fact]
        public async Task ExistsAsync_WhenKeyDoesNotExist_ReturnsFalse()
        {
            // Arrange
            var key = "non-existing-key";

            // Act
            var exists = await _cache.ExistsAsync(key);

            // Assert
            Assert.False(exists);
        }

        [Fact]
        public async Task RemoveAsync_WhenKeyExists_ReturnsTrue()
        {
            // Arrange
            var key = "key-to-remove";
            var testData = new TestData { Id = 4, Name = "ToRemove" };

            // Act
            await _cache.SetAsync(key, testData);
            var removed = await _cache.RemoveAsync(key);
            var result = await _cache.GetAsync<TestData>(key);

            // Assert
            Assert.True(removed);
            Assert.Null(result);
        }

        [Fact]
        public async Task GetOrSetAsync_WhenKeyDoesNotExist_CreatesAndReturnsValue()
        {
            // Arrange
            var key = "new-key";
            var expectedData = new TestData { Id = 5, Name = "New" };
            var factoryCalled = false;

            // Act
            var result = await _cache.GetOrSetAsync(key, async () =>
            {
                factoryCalled = true;
                await Task.Delay(10); // Simulate async work
                return expectedData;
            });

            // Assert
            Assert.True(factoryCalled);
            Assert.NotNull(result);
            Assert.Equal(expectedData.Id, result.Id);
            Assert.Equal(expectedData.Name, result.Name);
        }

        [Fact]
        public async Task GetOrSetAsync_WhenKeyExists_ReturnsExistingValue()
        {
            // Arrange
            var key = "existing-factory-key";
            var existingData = new TestData { Id = 6, Name = "Existing" };
            var newData = new TestData { Id = 7, Name = "New" };
            var factoryCalled = false;

            // Act
            await _cache.SetAsync(key, existingData);
            var result = await _cache.GetOrSetAsync(key, async () =>
            {
                factoryCalled = true;
                return newData;
            });

            // Assert
            Assert.False(factoryCalled);
            Assert.NotNull(result);
            Assert.Equal(existingData.Id, result.Id);
            Assert.Equal(existingData.Name, result.Name);
        }

        [Fact]
        public async Task ClearAsync_RemovesAllEntries()
        {
            // Arrange
            var key1 = "key1";
            var key2 = "key2";
            var data1 = new TestData { Id = 8, Name = "Data1" };
            var data2 = new TestData { Id = 9, Name = "Data2" };

            // Act
            await _cache.SetAsync(key1, data1);
            await _cache.SetAsync(key2, data2);
            await _cache.ClearAsync();
            
            var result1 = await _cache.GetAsync<TestData>(key1);
            var result2 = await _cache.GetAsync<TestData>(key2);

            // Assert
            Assert.Null(result1);
            Assert.Null(result2);
        }

        [Fact]
        public async Task GetStatisticsAsync_ReturnsAccurateStats()
        {
            // Arrange
            var key1 = "stats-key1";
            var key2 = "stats-key2";
            var data1 = new TestData { Id = 10, Name = "StatsData1" };
            var data2 = new TestData { Id = 11, Name = "StatsData2" };

            // Act
            await _cache.SetAsync(key1, data1);
            await _cache.SetAsync(key2, data2);
            var stats = await _cache.GetStatisticsAsync();

            // Assert
            Assert.True(stats.TotalFiles >= 2);
            Assert.True(stats.ActiveFiles >= 2);
            Assert.True(stats.TotalSizeBytes > 0);
        }

        [Fact]
        public async Task CleanupExpiredAsync_RemovesOnlyExpiredEntries()
        {
            // Arrange
            var validKey = "valid-key";
            var expiredKey = "expired-cleanup-key";
            var validData = new TestData { Id = 12, Name = "Valid" };
            var expiredData = new TestData { Id = 13, Name = "Expired" };
            var shortExpiration = TimeSpan.FromMilliseconds(10);

            // Act
            await _cache.SetAsync(validKey, validData);
            await _cache.SetAsync(expiredKey, expiredData, shortExpiration);
            await Task.Delay(50); // Wait for expiration
            await _cache.CleanupExpiredAsync();
            
            var validResult = await _cache.GetAsync<TestData>(validKey);
            var expiredResult = await _cache.GetAsync<TestData>(expiredKey);

            // Assert
            Assert.NotNull(validResult);
            Assert.Null(expiredResult);
        }

        [Fact]
        public async Task MultipleThreads_CanAccessCacheConcurrently()
        {
            // Arrange
            const int threadCount = 10;
            const int operationsPerThread = 50;
            var tasks = new Task[threadCount];

            // Act
            for (int i = 0; i < threadCount; i++)
            {
                var threadIndex = i;
                tasks[i] = Task.Run(async () =>
                {
                    for (int j = 0; j < operationsPerThread; j++)
                    {
                        var key = $"thread-{threadIndex}-item-{j}";
                        var data = new TestData { Id = threadIndex * 1000 + j, Name = $"Thread{threadIndex}Item{j}" };
                        
                        await _cache.SetAsync(key, data);
                        var result = await _cache.GetAsync<TestData>(key);
                        
                        Assert.NotNull(result);
                        Assert.Equal(data.Id, result.Id);
                    }
                });
            }

            // Assert
            await Task.WhenAll(tasks);
        }

        [Fact]
        public void Constructor_CreatesDirectoryIfNotExists()
        {
            // Arrange
            var nonExistentDir = Path.Combine(_tempCacheDir, "sub", "dir");

            // Act
            using var cache = new PersistentCacheProvider(nonExistentDir);

            // Assert
            Assert.True(Directory.Exists(nonExistentDir));
        }

        public void Dispose()
        {
            _cache?.Dispose();
            
            if (Directory.Exists(_tempCacheDir))
            {
                try
                {
                    Directory.Delete(_tempCacheDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        private class TestData
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }
    }
}