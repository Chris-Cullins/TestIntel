using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Discovery;
using Xunit;

namespace TestIntelligence.Core.Tests.Discovery
{
    /// <summary>
    /// Simple, focused tests for NUnitTestDiscovery that verify core functionality.
    /// </summary>
    public class NUnitTestDiscoveryTests
    {
        private readonly NUnitTestDiscovery _discovery;

        public NUnitTestDiscoveryTests()
        {
            _discovery = new NUnitTestDiscovery();
        }

        [Fact]
        public void IsTestFixture_WithNullType_ReturnsFalse()
        {
            // Act
            var result = _discovery.IsTestFixture(null!);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsTestFixture_WithNonTestClass_ReturnsFalse()
        {
            // Arrange
            var type = typeof(string);

            // Act
            var result = _discovery.IsTestFixture(type);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsTestMethod_WithNullMethod_ReturnsFalse()
        {
            // Act
            var result = _discovery.IsTestMethod(null!);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsTestMethod_WithRegularMethod_ReturnsFalse()
        {
            // Arrange
            var method = typeof(string).GetMethod("ToString", Type.EmptyTypes)!;

            // Act
            var result = _discovery.IsTestMethod(method);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task DiscoverTestsAsync_WithNullAssembly_ThrowsArgumentNullException()
        {
            // Act & Assert
            await _discovery.Invoking(d => d.DiscoverTestsAsync((ITestAssembly)null!))
                .Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("testAssembly");
        }

        [Fact]
        public async Task DiscoverTestsAsync_WithValidAssembly_ReturnsResult()
        {
            // Arrange
            var testAssembly = CreateMockTestAssembly();

            // Act
            var result = await _discovery.DiscoverTestsAsync(testAssembly);

            // Assert
            result.Should().NotBeNull();
            result.AssemblyPath.Should().Be("/test/TestAssembly.dll");
            result.FrameworkVersion.Should().Be(FrameworkVersion.Net5Plus);
            result.TestFixtures.Should().NotBeNull();
            result.Errors.Should().NotBeNull();
        }

        [Fact]
        public async Task DiscoverTestsAsync_MultipleAssemblies_WithNullInput_ThrowsArgumentNullException()
        {
            // Act & Assert
            await _discovery.Invoking(d => d.DiscoverTestsAsync((System.Collections.Generic.IEnumerable<ITestAssembly>)null!))
                .Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("testAssemblies");
        }

        [Fact]
        public async Task DiscoverTestsAsync_MultipleAssemblies_ReturnsResults()
        {
            // Arrange
            var assemblies = new[] { CreateMockTestAssembly() };

            // Act
            var results = await _discovery.DiscoverTestsAsync((System.Collections.Generic.IEnumerable<ITestAssembly>)assemblies);

            // Assert
            results.Should().NotBeNull();
            results.Should().HaveCount(1);
            results.Keys.Should().Contain("/test/TestAssembly.dll");
        }

        private static ITestAssembly CreateMockTestAssembly()
        {
            var mockAssembly = Substitute.For<System.Reflection.Assembly>();
            mockAssembly.GetTypes().Returns(new[] { typeof(SimpleTestClass) });

            var testAssembly = Substitute.For<ITestAssembly>();
            testAssembly.AssemblyPath.Returns("/test/TestAssembly.dll");
            testAssembly.FrameworkVersion.Returns(FrameworkVersion.Net5Plus);
            testAssembly.UnderlyingAssembly.Returns(mockAssembly);

            return testAssembly;
        }
    }

    // Simple test class for mocking
    public class SimpleTestClass
    {
        public void RegularMethod() { }
    }
}