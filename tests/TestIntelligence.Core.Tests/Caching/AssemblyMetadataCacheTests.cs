using System;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Caching;
using TestIntelligence.Core.Discovery;
using Xunit;

namespace TestIntelligence.Core.Tests.Caching
{
    /// <summary>
    /// Simple, focused tests for AssemblyMetadataCache that verify core functionality.
    /// </summary>
    public class AssemblyMetadataCacheTests : IDisposable
    {
        private readonly AssemblyMetadataCache _cache;

        public AssemblyMetadataCacheTests()
        {
            _cache = new AssemblyMetadataCache();
        }

        public void Dispose()
        {
            _cache?.Dispose();
        }

        [Fact]
        public async Task GetOrCacheTestDiscoveryAsync_WithNullAssemblyPath_ThrowsArgumentNullException()
        {
            // Arrange
            var factory = Substitute.For<Func<Task<TestDiscoveryResult>>>();

            // Act & Assert
            await _cache.Invoking(c => c.GetOrCacheTestDiscoveryAsync(null!, factory))
                .Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("assemblyPath");
        }

        [Fact]
        public async Task GetOrCacheTestDiscoveryAsync_WithNullFactory_ThrowsArgumentNullException()
        {
            // Arrange
            var assemblyPath = "/test/TestAssembly.dll";

            // Act & Assert
            await _cache.Invoking(c => c.GetOrCacheTestDiscoveryAsync(assemblyPath, null!))
                .Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("discoveryFactory");
        }

        [Fact]
        public async Task GetOrCacheTestDiscoveryAsync_WithValidInput_CallsFactory()
        {
            // Arrange
            var assemblyPath = "/test/TestAssembly.dll";
            var expectedResult = CreateTestDiscoveryResult(assemblyPath);
            var factory = Substitute.For<Func<Task<TestDiscoveryResult>>>();
            factory.Invoke().Returns(expectedResult);

            // Act
            var result = await _cache.GetOrCacheTestDiscoveryAsync(assemblyPath, factory);

            // Assert
            result.Should().NotBeNull();
            result.Should().Be(expectedResult);
            await factory.Received(1).Invoke();
        }

        [Fact]
        public async Task GetOrCacheAssemblyLoadAsync_WithNullAssemblyPath_ThrowsArgumentNullException()
        {
            // Arrange
            var factory = Substitute.For<Func<Task<AssemblyLoadResult>>>();

            // Act & Assert
            await _cache.Invoking(c => c.GetOrCacheAssemblyLoadAsync(null!, factory))
                .Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("assemblyPath");
        }

        [Fact]
        public async Task GetOrCacheAssemblyLoadAsync_WithNullFactory_ThrowsArgumentNullException()
        {
            // Arrange
            var assemblyPath = "/test/TestAssembly.dll";

            // Act & Assert
            await _cache.Invoking(c => c.GetOrCacheAssemblyLoadAsync(assemblyPath, null!))
                .Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("loadFactory");
        }

        [Fact]
        public async Task GetOrCacheAssemblyLoadAsync_WithValidInput_CallsFactory()
        {
            // Arrange
            var assemblyPath = "/test/TestAssembly.dll";
            var expectedResult = CreateMockAssemblyLoadResult();
            var factory = Substitute.For<Func<Task<AssemblyLoadResult>>>();
            factory.Invoke().Returns(expectedResult);

            // Act
            var result = await _cache.GetOrCacheAssemblyLoadAsync(assemblyPath, factory);

            // Assert
            result.Should().NotBeNull();
            result.Should().Be(expectedResult);
            await factory.Received(1).Invoke();
        }

        [Fact]
        public async Task CacheFrameworkVersionAsync_WithNullAssemblyPath_ThrowsArgumentNullException()
        {
            // Arrange
            var frameworkVersion = FrameworkVersion.Net5Plus;

            // Act & Assert
            await _cache.Invoking(c => c.CacheFrameworkVersionAsync(null!, frameworkVersion))
                .Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("assemblyPath");
        }

        [Fact]
        public async Task CacheFrameworkVersionAsync_WithValidInput_DoesNotThrow()
        {
            // Arrange
            var assemblyPath = "/test/TestAssembly.dll";
            var frameworkVersion = FrameworkVersion.Net5Plus;

            // Act & Assert
            await _cache.Invoking(c => c.CacheFrameworkVersionAsync(assemblyPath, frameworkVersion))
                .Should().NotThrowAsync();
        }

        [Fact]
        public async Task GetCachedFrameworkVersionAsync_WithNullAssemblyPath_ReturnsNull()
        {
            // Act
            var result = await _cache.GetCachedFrameworkVersionAsync(null!);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task GetCachedFrameworkVersionAsync_WithNonCachedAssembly_ReturnsNull()
        {
            // Arrange
            var assemblyPath = "/test/NonExistentAssembly.dll";

            // Act
            var result = await _cache.GetCachedFrameworkVersionAsync(assemblyPath);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task InvalidateAssemblyAsync_WithValidPath_DoesNotThrow()
        {
            // Arrange
            var assemblyPath = "/test/TestAssembly.dll";

            // Act & Assert
            await _cache.Invoking(c => c.InvalidateAssemblyAsync(assemblyPath))
                .Should().NotThrowAsync();
        }

        [Fact]
        public async Task ClearAllAsync_DoesNotThrow()
        {
            // Act & Assert
            await _cache.Invoking(c => c.ClearAllAsync())
                .Should().NotThrowAsync();
        }

        [Fact]
        public async Task GetModifiedAssembliesAsync_WithNullInput_ReturnsEmptyList()
        {
            // Act
            var result = await _cache.GetModifiedAssembliesAsync(null!);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            // Act & Assert - Should not throw
            _cache.Dispose();
            _cache.Dispose();
        }

        private static TestDiscoveryResult CreateTestDiscoveryResult(string assemblyPath)
        {
            return new TestDiscoveryResult(
                assemblyPath,
                FrameworkVersion.Net5Plus,
                Array.Empty<TestIntelligence.Core.Models.TestFixture>(),
                Array.Empty<string>());
        }

        private static AssemblyLoadResult CreateMockAssemblyLoadResult()
        {
            var testAssembly = Substitute.For<ITestAssembly>();
            return AssemblyLoadResult.Success(testAssembly);
        }
    }
}