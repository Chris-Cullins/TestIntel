using System;
using System.Linq;
using FluentAssertions;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Models;
using Xunit;

namespace TestIntelligence.Core.Tests.Models
{
    /// <summary>
    /// Simple, focused tests for TestFixture model that verify core properties and methods.
    /// </summary>
    public class TestFixtureTests
    {
        [Fact]
        public void Constructor_WithValidParameters_SetsProperties()
        {
            // Arrange
            var type = typeof(SampleFixtureClass);
            var assemblyPath = "/test/TestAssembly.dll";
            var frameworkVersion = FrameworkVersion.Net5Plus;

            // Act
            var testFixture = new TestFixture(type, assemblyPath, frameworkVersion);

            // Assert
            testFixture.Type.Should().Be(type);
            testFixture.AssemblyPath.Should().Be(assemblyPath);
            testFixture.FrameworkVersion.Should().Be(frameworkVersion);
            testFixture.ClassName.Should().Be(nameof(SampleFixtureClass));
            testFixture.FullClassName.Should().Be(type.FullName);
            testFixture.Namespace.Should().Be(type.Namespace);
        }

        [Fact]
        public void Constructor_WithNullType_ThrowsArgumentNullException()
        {
            // Arrange
            var assemblyPath = "/test/TestAssembly.dll";
            var frameworkVersion = FrameworkVersion.Net5Plus;

            // Act & Assert
            var action = () => new TestFixture(null!, assemblyPath, frameworkVersion);
            action.Should().Throw<ArgumentNullException>().WithParameterName("type");
        }

        [Fact]
        public void Constructor_WithNullAssemblyPath_ThrowsArgumentNullException()
        {
            // Arrange
            var type = typeof(SampleFixtureClass);
            var frameworkVersion = FrameworkVersion.Net5Plus;

            // Act & Assert
            var action = () => new TestFixture(type, null!, frameworkVersion);
            action.Should().Throw<ArgumentNullException>().WithParameterName("assemblyPath");
        }

        [Fact]
        public void GetUniqueId_ReturnsFullClassName()
        {
            // Arrange
            var testFixture = new TestFixture(typeof(SampleFixtureClass), "/test/TestAssembly.dll", FrameworkVersion.Net5Plus);

            // Act
            var uniqueId = testFixture.GetUniqueId();

            // Assert
            uniqueId.Should().Be(typeof(SampleFixtureClass).FullName);
        }

        [Fact]
        public void TestMethods_IsInitialized()
        {
            // Arrange & Act
            var testFixture = new TestFixture(typeof(SampleFixtureClass), "/test/TestAssembly.dll", FrameworkVersion.Net5Plus);

            // Assert
            testFixture.TestMethods.Should().NotBeNull();
        }

        [Fact]
        public void FixtureAttributes_IsInitialized()
        {
            // Arrange & Act
            var testFixture = new TestFixture(typeof(SampleFixtureClass), "/test/TestAssembly.dll", FrameworkVersion.Net5Plus);

            // Assert
            testFixture.FixtureAttributes.Should().NotBeNull();
        }

        [Fact]
        public void HasSetUpMethods_WithNoSetupMethods_ReturnsFalse()
        {
            // Arrange
            var testFixture = new TestFixture(typeof(SampleFixtureClass), "/test/TestAssembly.dll", FrameworkVersion.Net5Plus);

            // Act & Assert
            testFixture.HasSetUpMethods.Should().BeFalse();
        }

        [Fact]
        public void HasTearDownMethods_WithNoTearDownMethods_ReturnsFalse()
        {
            // Arrange
            var testFixture = new TestFixture(typeof(SampleFixtureClass), "/test/TestAssembly.dll", FrameworkVersion.Net5Plus);

            // Act & Assert
            testFixture.HasTearDownMethods.Should().BeFalse();
        }

        [Fact]
        public void HasTests_WithNoTestMethods_ReturnsFalse()
        {
            // Arrange
            var testFixture = new TestFixture(typeof(SampleFixtureClass), "/test/TestAssembly.dll", FrameworkVersion.Net5Plus);

            // Act & Assert
            testFixture.HasTests.Should().BeFalse();
        }

        [Fact]
        public void GetExecutableTests_ReturnsExpectedCollection()
        {
            // Arrange
            var testFixture = new TestFixture(typeof(SampleFixtureClass), "/test/TestAssembly.dll", FrameworkVersion.Net5Plus);

            // Act
            var executableTests = testFixture.GetExecutableTests().ToList();

            // Assert
            executableTests.Should().NotBeNull();
            executableTests.Should().BeEmpty(); // Since SampleFixtureClass has no test attributes
        }

        [Fact]
        public void GetSetUpMethods_ReturnsExpectedCollection()
        {
            // Arrange
            var testFixture = new TestFixture(typeof(SampleFixtureClass), "/test/TestAssembly.dll", FrameworkVersion.Net5Plus);

            // Act
            var setUpMethods = testFixture.GetSetUpMethods().ToList();

            // Assert
            setUpMethods.Should().NotBeNull();
            setUpMethods.Should().BeEmpty(); // Since SampleFixtureClass has no setup attributes
        }

        [Fact]
        public void GetTearDownMethods_ReturnsExpectedCollection()
        {
            // Arrange
            var testFixture = new TestFixture(typeof(SampleFixtureClass), "/test/TestAssembly.dll", FrameworkVersion.Net5Plus);

            // Act
            var tearDownMethods = testFixture.GetTearDownMethods().ToList();

            // Assert
            tearDownMethods.Should().NotBeNull();
            tearDownMethods.Should().BeEmpty(); // Since SampleFixtureClass has no teardown attributes
        }

        [Fact]
        public void GetCategories_ReturnsExpectedCollection()
        {
            // Arrange
            var testFixture = new TestFixture(typeof(SampleFixtureClass), "/test/TestAssembly.dll", FrameworkVersion.Net5Plus);

            // Act
            var categories = testFixture.GetCategories().ToList();

            // Assert
            categories.Should().NotBeNull();
            categories.Should().BeEmpty(); // Since SampleFixtureClass has no category attributes
        }

        [Fact]
        public void ToString_ReturnsExpectedFormat()
        {
            // Arrange
            var testFixture = new TestFixture(typeof(SampleFixtureClass), "/test/TestAssembly.dll", FrameworkVersion.Net5Plus);

            // Act
            var stringValue = testFixture.ToString();

            // Assert
            stringValue.Should().Contain(nameof(SampleFixtureClass));
            stringValue.Should().Contain("tests");
        }
    }

    // Helper class for testing
    public class SampleFixtureClass
    {
        public void TestMethod() { }
        public void RegularMethod() { }
        public string Property { get; set; } = string.Empty;
    }
}