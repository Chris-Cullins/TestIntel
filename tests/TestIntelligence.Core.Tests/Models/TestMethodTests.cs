using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Models;
using Xunit;

namespace TestIntelligence.Core.Tests.Models
{
    /// <summary>
    /// Simple, focused tests for TestMethod model that verify core properties and methods.
    /// </summary>
    public class TestMethodTests
    {
        [Fact]
        public void Constructor_WithValidParameters_SetsProperties()
        {
            // Arrange
            var methodInfo = typeof(SampleClass).GetMethod(nameof(SampleClass.TestMethod))!;
            var declaringType = typeof(SampleClass);
            var assemblyPath = "/test/TestAssembly.dll";
            var frameworkVersion = FrameworkVersion.Net5Plus;

            // Act
            var testMethod = new TestMethod(methodInfo, declaringType, assemblyPath, frameworkVersion);

            // Assert
            testMethod.MethodInfo.Should().BeSameAs(methodInfo);
            testMethod.DeclaringType.Should().Be(declaringType);
            testMethod.AssemblyPath.Should().Be(assemblyPath);
            testMethod.FrameworkVersion.Should().Be(frameworkVersion);
            testMethod.MethodName.Should().Be(nameof(SampleClass.TestMethod));
            testMethod.ClassName.Should().Be(nameof(SampleClass));
            testMethod.FullClassName.Should().Be(typeof(SampleClass).FullName);
        }

        [Fact]
        public void Constructor_WithNullMethodInfo_ThrowsArgumentNullException()
        {
            // Arrange
            var declaringType = typeof(SampleClass);
            var assemblyPath = "/test/TestAssembly.dll";
            var frameworkVersion = FrameworkVersion.Net5Plus;

            // Act & Assert
            var action = () => new TestMethod(null!, declaringType, assemblyPath, frameworkVersion);
            action.Should().Throw<ArgumentNullException>().WithParameterName("methodInfo");
        }

        [Fact]
        public void Constructor_WithNullDeclaringType_ThrowsArgumentNullException()
        {
            // Arrange
            var methodInfo = typeof(SampleClass).GetMethod(nameof(SampleClass.TestMethod))!;
            var assemblyPath = "/test/TestAssembly.dll";
            var frameworkVersion = FrameworkVersion.Net5Plus;

            // Act & Assert
            var action = () => new TestMethod(methodInfo, null!, assemblyPath, frameworkVersion);
            action.Should().Throw<ArgumentNullException>().WithParameterName("declaringType");
        }

        [Fact]
        public void Constructor_WithNullAssemblyPath_ThrowsArgumentNullException()
        {
            // Arrange
            var methodInfo = typeof(SampleClass).GetMethod(nameof(SampleClass.TestMethod))!;
            var declaringType = typeof(SampleClass);
            var frameworkVersion = FrameworkVersion.Net5Plus;

            // Act & Assert
            var action = () => new TestMethod(methodInfo, declaringType, null!, frameworkVersion);
            action.Should().Throw<ArgumentNullException>().WithParameterName("assemblyPath");
        }

        [Fact]
        public void GetUniqueId_ReturnsExpectedFormat()
        {
            // Arrange
            var methodInfo = typeof(SampleClass).GetMethod(nameof(SampleClass.TestMethod))!;
            var testMethod = new TestMethod(methodInfo, typeof(SampleClass), "/test/TestAssembly.dll", FrameworkVersion.Net5Plus);

            // Act
            var uniqueId = testMethod.GetUniqueId();

            // Assert
            uniqueId.Should().Be($"{typeof(SampleClass).FullName}.TestMethod");
        }

        [Fact]
        public void GetDisplayName_ReturnsExpectedFormat()
        {
            // Arrange
            var methodInfo = typeof(SampleClass).GetMethod(nameof(SampleClass.TestMethod))!;
            var testMethod = new TestMethod(methodInfo, typeof(SampleClass), "/test/TestAssembly.dll", FrameworkVersion.Net5Plus);

            // Act
            var displayName = testMethod.GetDisplayName();

            // Assert
            displayName.Should().Be("SampleClass.TestMethod");
        }

        [Fact]
        public void IsExecutableTest_WithRegularMethod_ReturnsFalse()
        {
            // Arrange
            var methodInfo = typeof(SampleClass).GetMethod(nameof(SampleClass.TestMethod))!;
            var testMethod = new TestMethod(methodInfo, typeof(SampleClass), "/test/TestAssembly.dll", FrameworkVersion.Net5Plus);

            // Act
            var isExecutable = testMethod.IsExecutableTest();

            // Assert
            isExecutable.Should().BeFalse();
        }

        [Fact]
        public void TestAttributes_IsInitialized()
        {
            // Arrange
            var methodInfo = typeof(SampleClass).GetMethod(nameof(SampleClass.TestMethod))!;
            var testMethod = new TestMethod(methodInfo, typeof(SampleClass), "/test/TestAssembly.dll", FrameworkVersion.Net5Plus);

            // Act & Assert
            testMethod.TestAttributes.Should().NotBeNull();
        }

        [Fact]
        public void ToString_ReturnsDisplayName()
        {
            // Arrange
            var methodInfo = typeof(SampleClass).GetMethod(nameof(SampleClass.TestMethod))!;
            var testMethod = new TestMethod(methodInfo, typeof(SampleClass), "/test/TestAssembly.dll", FrameworkVersion.Net5Plus);

            // Act
            var stringValue = testMethod.ToString();

            // Assert
            stringValue.Should().Be(testMethod.GetDisplayName());
        }

        [Fact]
        public void GetTestCaseParameters_WithRegularMethod_ReturnsEmpty()
        {
            // Arrange
            var methodInfo = typeof(SampleClass).GetMethod(nameof(SampleClass.TestMethod))!;
            var testMethod = new TestMethod(methodInfo, typeof(SampleClass), "/test/TestAssembly.dll", FrameworkVersion.Net5Plus);

            // Act
            var parameters = testMethod.GetTestCaseParameters().ToList();

            // Assert
            parameters.Should().BeEmpty();
        }
    }

    // Helper class for testing
    public class SampleClass
    {
        public void TestMethod() { }
        public void RegularMethod() { }
    }
}