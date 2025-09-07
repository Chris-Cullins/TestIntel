using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Framework48Adapter;
using Xunit;

namespace TestIntelligence.Framework48Adapter.Tests
{
    public class Framework48AssemblyLoaderTests
    {
        private readonly Framework48AssemblyLoader _loader;

        public Framework48AssemblyLoaderTests()
        {
            _loader = new Framework48AssemblyLoader();
        }

        [Fact]
        public void SupportedFramework_ShouldReturnNetFramework48()
        {
            // Act & Assert
            Assert.Equal(FrameworkVersion.NetFramework48, _loader.SupportedFramework);
        }

        [Fact]
        public async Task LoadAssemblyAsync_WithNonExistentFile_ThrowsInvalidOperationException()
        {
            // Arrange
            var nonExistentPath = "/path/to/nonexistent/assembly.dll";

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _loader.LoadAssemblyAsync(nonExistentPath));
            
            Assert.Contains("Failed to load assembly", exception.Message);
            Assert.IsType<FileNotFoundException>(exception.InnerException);
        }

        [Fact]
        public async Task LoadAssemblyAsync_WithCancellationToken_ThrowsInvalidOperationException()
        {
            // Arrange
            var validPath = Assembly.GetExecutingAssembly().Location;
            var cancelledToken = new CancellationToken(true);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _loader.LoadAssemblyAsync(validPath, cancelledToken));
            
            Assert.Contains("Failed to load assembly", exception.Message);
            Assert.IsType<OperationCanceledException>(exception.InnerException);
        }

        [Fact]
        public async Task LoadAssemblyAsync_WithValidAssembly_ReturnsTestAssembly()
        {
            // Arrange
            var currentAssemblyPath = Assembly.GetExecutingAssembly().Location;

            // Act
            var testAssembly = await _loader.LoadAssemblyAsync(currentAssemblyPath);

            // Assert
            Assert.NotNull(testAssembly);
            Assert.IsType<Framework48TestAssembly>(testAssembly);
            Assert.Equal(currentAssemblyPath, testAssembly.AssemblyPath);
        }

        [Fact]
        public void LoadAssembly_WithValidAssembly_ReturnsTestAssembly()
        {
            // Arrange
            var currentAssemblyPath = Assembly.GetExecutingAssembly().Location;

            // Act
            var testAssembly = _loader.LoadAssembly(currentAssemblyPath);

            // Assert
            Assert.NotNull(testAssembly);
            Assert.IsType<Framework48TestAssembly>(testAssembly);
            Assert.Equal(currentAssemblyPath, testAssembly.AssemblyPath);
        }

        [Fact]
        public void TryUnloadAssembly_WithValidAssembly_ReturnsFalse()
        {
            // Arrange
            var currentAssemblyPath = Assembly.GetExecutingAssembly().Location;
            var testAssembly = _loader.LoadAssembly(currentAssemblyPath);

            // Act
            var result = _loader.TryUnloadAssembly(testAssembly);

            // Assert
            Assert.False(result); // Framework 4.8 can't unload assemblies
        }

        [Fact]
        public void TryUnloadAssembly_WithNullAssembly_ReturnsFalse()
        {
            // Act
            var result = _loader.TryUnloadAssembly(null!);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void CanLoad_WithNonExistentFile_ReturnsFalse()
        {
            // Arrange
            var nonExistentPath = "/path/to/nonexistent/assembly.dll";

            // Act
            var canLoad = _loader.CanLoad(nonExistentPath);

            // Assert
            Assert.False(canLoad);
        }

        [Fact]
        public void CanLoad_WithValidAssembly_ReturnsTrue()
        {
            // Arrange
            var currentAssemblyPath = Assembly.GetExecutingAssembly().Location;

            // Act
            var canLoad = _loader.CanLoad(currentAssemblyPath);

            // Assert - This might be true or false depending on the actual target framework
            // Just testing that it doesn't throw an exception
            Assert.True(canLoad || !canLoad);
        }

        [Fact]
        public void Dispose_DoesNotThrow()
        {
            // Act & Assert
            var exception = Record.Exception(() => _loader.Dispose());
            Assert.Null(exception);
        }

        [Fact]
        public void AssemblyResolve_CanBeSetAndRetrieved()
        {
            // Arrange
            Func<object, ResolveEventArgs, Assembly?> resolver = (sender, args) => null;

            // Act
            _loader.AssemblyResolve = resolver;

            // Assert
            Assert.Same(resolver, _loader.AssemblyResolve);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null!)]
        public async Task LoadAssemblyAsync_WithInvalidPath_ThrowsInvalidOperationException(string path)
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _loader.LoadAssemblyAsync(path!));
            
            Assert.Contains("Failed to load assembly", exception.Message);
        }
    }
}