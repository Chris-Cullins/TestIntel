using System;
using System.Threading.Tasks;
using FluentAssertions;
using TestIntelligence.Core.Caching;
using Xunit;

namespace TestIntelligence.Core.Tests.Caching
{
    /// <summary>
    /// Simple, focused tests for MemoryCacheProvider that verify core functionality.
    /// </summary>
    public class MemoryCacheProviderTests : IDisposable
    {
        private readonly MemoryCacheProvider _cacheProvider;

        public MemoryCacheProviderTests()
        {
            _cacheProvider = new MemoryCacheProvider();
        }

        public void Dispose()
        {
            _cacheProvider?.Dispose();
        }

        [Fact]
        public async Task GetAsync_WithNonExistentKey_ReturnsNull()
        {
            // Arrange
            var key = "non-existent-key";

            // Act
            var result = await _cacheProvider.GetAsync<string>(key);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task GetAsync_WithNullKey_ReturnsNull()
        {
            // Act
            var result = await _cacheProvider.GetAsync<string>(null!);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task SetAsync_WithValidKeyAndValue_StoresValue()
        {
            // Arrange
            var key = "test-key";
            var value = "test-value";

            // Act
            await _cacheProvider.SetAsync(key, value);
            var result = await _cacheProvider.GetAsync<string>(key);

            // Assert
            result.Should().Be(value);
        }

        [Fact]
        public async Task SetAsync_WithNullKey_ThrowsArgumentNullException()
        {
            // Arrange
            var value = "test-value";

            // Act & Assert
            await _cacheProvider.Invoking(c => c.SetAsync<string>(null!, value))
                .Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("key");
        }

        [Fact]
        public async Task SetAsync_WithNullValue_ThrowsArgumentNullException()
        {
            // Arrange
            var key = "test-key";

            // Act & Assert
            await _cacheProvider.Invoking(c => c.SetAsync<string>(key, null!))
                .Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("value");
        }

        [Fact]
        public async Task RemoveAsync_WithExistingKey_RemovesValue_ReturnsTrue()
        {
            // Arrange
            var key = "test-key";
            var value = "test-value";
            await _cacheProvider.SetAsync(key, value);

            // Act
            var removed = await _cacheProvider.RemoveAsync(key);

            // Assert
            removed.Should().BeTrue();
            var result = await _cacheProvider.GetAsync<string>(key);
            result.Should().BeNull();
        }

        [Fact]
        public async Task RemoveAsync_WithNonExistentKey_ReturnsFalse()
        {
            // Arrange
            var key = "non-existent-key";

            // Act
            var removed = await _cacheProvider.RemoveAsync(key);

            // Assert
            removed.Should().BeFalse();
        }

        [Fact]
        public async Task ExistsAsync_WithExistingKey_ReturnsTrue()
        {
            // Arrange
            var key = "test-key";
            var value = "test-value";
            await _cacheProvider.SetAsync(key, value);

            // Act
            var exists = await _cacheProvider.ExistsAsync(key);

            // Assert
            exists.Should().BeTrue();
        }

        [Fact]
        public async Task ExistsAsync_WithNonExistentKey_ReturnsFalse()
        {
            // Arrange
            var key = "non-existent-key";

            // Act
            var exists = await _cacheProvider.ExistsAsync(key);

            // Assert
            exists.Should().BeFalse();
        }

        [Fact]
        public async Task ClearAsync_RemovesAllEntries()
        {
            // Arrange
            await _cacheProvider.SetAsync("key1", "value1");
            await _cacheProvider.SetAsync("key2", "value2");

            // Act
            await _cacheProvider.ClearAsync();

            // Assert
            (await _cacheProvider.GetAsync<string>("key1")).Should().BeNull();
            (await _cacheProvider.GetAsync<string>("key2")).Should().BeNull();
        }

        [Fact]
        public async Task GetOrSetAsync_WithNonExistentKey_CallsFactory()
        {
            // Arrange
            var key = "factory-key";
            var expectedValue = "factory-value";

            // Act
            var result = await _cacheProvider.GetOrSetAsync(key, () =>
            {
                return Task.FromResult(expectedValue);
            });

            // Assert
            result.Should().Be(expectedValue);
            var cachedResult = await _cacheProvider.GetAsync<string>(key);
            cachedResult.Should().Be(expectedValue);
        }

        [Fact]
        public async Task GetOrSetAsync_WithExistingKey_DoesNotCallFactory()
        {
            // Arrange
            var key = "existing-key";
            var cachedValue = "cached-value";
            var factoryValue = "factory-value";

            await _cacheProvider.SetAsync(key, cachedValue);

            // Act
            var result = await _cacheProvider.GetOrSetAsync(key, () => Task.FromResult(factoryValue));

            // Assert
            result.Should().Be(cachedValue);
        }

        [Fact]
        public async Task GetOrSetAsync_WithNullKey_ThrowsArgumentNullException()
        {
            // Act & Assert
            await _cacheProvider.Invoking(c => c.GetOrSetAsync<string>(null!, () => Task.FromResult("value")))
                .Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("key");
        }

        [Fact]
        public async Task GetOrSetAsync_WithNullFactory_ThrowsArgumentNullException()
        {
            // Arrange
            var key = "test-key";

            // Act & Assert
            await _cacheProvider.Invoking(c => c.GetOrSetAsync<string>(key, null!))
                .Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("factory");
        }

        [Fact]
        public void GetStatistics_ReturnsValidStatistics()
        {
            // Arrange
            _cacheProvider.SetAsync("key1", "value1").Wait();
            _cacheProvider.SetAsync("key2", "value2").Wait();

            // Act
            var stats = _cacheProvider.GetStatistics();

            // Assert
            stats.Should().NotBeNull();
            stats.TotalEntries.Should().BeGreaterOrEqualTo(2);
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            // Act & Assert - Should not throw
            _cacheProvider.Dispose();
            _cacheProvider.Dispose();
        }
    }
}