using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using TestIntelligence.Core.Assembly;
using Xunit;

namespace TestIntelligence.Core.Tests.Assembly
{
    public class TestAssemblyWrapperTests
    {
        private readonly System.Reflection.Assembly _testAssembly;
        private readonly string _assemblyPath;
        private readonly FrameworkVersion _frameworkVersion;

        public TestAssemblyWrapperTests()
        {
            _testAssembly = typeof(TestAssemblyWrapperTests).Assembly;
            _assemblyPath = _testAssembly.Location;
            _frameworkVersion = FrameworkVersion.Net5Plus;
        }

        [Fact]
        public void Constructor_WithValidParameters_ShouldInitializeProperties()
        {
            // Act
            using var wrapper = new TestAssemblyWrapper(_testAssembly, _assemblyPath, _frameworkVersion);

            // Assert
            wrapper.AssemblyPath.Should().Be(_assemblyPath);
            wrapper.AssemblyName.Should().Be(_testAssembly.GetName().Name);
            wrapper.FrameworkVersion.Should().Be(_frameworkVersion);
            wrapper.UnderlyingAssembly.Should().BeSameAs(_testAssembly);
        }

        [Fact]
        public void Constructor_WithNullAssembly_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var action = () => new TestAssemblyWrapper(null!, _assemblyPath, _frameworkVersion);
            action.Should().Throw<ArgumentNullException>().WithParameterName("assembly");
        }

        [Fact]
        public void Constructor_WithNullAssemblyPath_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var action = () => new TestAssemblyWrapper(_testAssembly, null!, _frameworkVersion);
            action.Should().Throw<ArgumentNullException>().WithParameterName("assemblyPath");
        }

        [Fact]
        public void GetTypes_ShouldReturnAllTypes()
        {
            // Arrange
            using var wrapper = new TestAssemblyWrapper(_testAssembly, _assemblyPath, _frameworkVersion);

            // Act
            var types = wrapper.GetTypes();

            // Assert
            types.Should().NotBeNull();
            types.Should().NotBeEmpty();
            types.Should().Contain(typeof(TestAssemblyWrapperTests));
        }

        [Fact]
        public void GetTypes_WithPredicate_ShouldReturnFilteredTypes()
        {
            // Arrange
            using var wrapper = new TestAssemblyWrapper(_testAssembly, _assemblyPath, _frameworkVersion);

            // Act
            var types = wrapper.GetTypes(t => t.Name.Contains("Tests"));

            // Assert
            types.Should().NotBeNull();
            types.Should().Contain(typeof(TestAssemblyWrapperTests));
        }

        [Fact]
        public void GetTypes_WithNullPredicate_ShouldThrowArgumentNullException()
        {
            // Arrange
            using var wrapper = new TestAssemblyWrapper(_testAssembly, _assemblyPath, _frameworkVersion);

            // Act & Assert
            var action = () => wrapper.GetTypes(null!);
            action.Should().Throw<ArgumentNullException>().WithParameterName("predicate");
        }

        [Fact]
        public void GetTestClasses_ShouldReturnTestClasses()
        {
            // Arrange
            using var wrapper = new TestAssemblyWrapper(_testAssembly, _assemblyPath, _frameworkVersion);

            // Act
            var testClasses = wrapper.GetTestClasses();

            // Assert
            testClasses.Should().NotBeNull();
            testClasses.Should().Contain(typeof(TestAssemblyWrapperTests));
        }

        [Fact]
        public void GetTestMethods_WithValidTestClass_ShouldReturnTestMethods()
        {
            // Arrange
            using var wrapper = new TestAssemblyWrapper(_testAssembly, _assemblyPath, _frameworkVersion);

            // Act
            var testMethods = wrapper.GetTestMethods(typeof(TestAssemblyWrapperTests));

            // Assert
            testMethods.Should().NotBeNull();
            testMethods.Should().NotBeEmpty();
            testMethods.Any(m => m.Name.Contains("Test")).Should().BeTrue();
        }

        [Fact]
        public void GetTestMethods_WithNullTestClass_ShouldThrowArgumentNullException()
        {
            // Arrange
            using var wrapper = new TestAssemblyWrapper(_testAssembly, _assemblyPath, _frameworkVersion);

            // Act & Assert
            var action = () => wrapper.GetTestMethods(null!);
            action.Should().Throw<ArgumentNullException>().WithParameterName("testClass");
        }

        [Fact]
        public void GetAllTestMethods_ShouldReturnAllTestMethods()
        {
            // Arrange
            using var wrapper = new TestAssemblyWrapper(_testAssembly, _assemblyPath, _frameworkVersion);

            // Act
            var testMethods = wrapper.GetAllTestMethods();

            // Assert
            testMethods.Should().NotBeNull();
            testMethods.Should().NotBeEmpty();
        }

        [Fact]
        public void GetCustomAttributes_ShouldReturnAttributes()
        {
            // Arrange
            using var wrapper = new TestAssemblyWrapper(_testAssembly, _assemblyPath, _frameworkVersion);

            // Act
            var attributes = wrapper.GetCustomAttributes<System.Runtime.Versioning.TargetFrameworkAttribute>();

            // Assert
            attributes.Should().NotBeNull();
        }

        [Fact]
        public void HasTestFrameworkReference_WithExistingFramework_ShouldReturnTrue()
        {
            // Arrange
            using var wrapper = new TestAssemblyWrapper(_testAssembly, _assemblyPath, _frameworkVersion);

            // Act
            var result = wrapper.HasTestFrameworkReference("xunit");

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void HasTestFrameworkReference_WithNonExistingFramework_ShouldReturnFalse()
        {
            // Arrange
            using var wrapper = new TestAssemblyWrapper(_testAssembly, _assemblyPath, _frameworkVersion);

            // Act
            var result = wrapper.HasTestFrameworkReference("NonExistentFramework");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void HasTestFrameworkReference_WithNullOrWhitespaceFrameworkName_ShouldReturnFalse()
        {
            // Arrange
            using var wrapper = new TestAssemblyWrapper(_testAssembly, _assemblyPath, _frameworkVersion);

            // Act & Assert
            wrapper.HasTestFrameworkReference(null!).Should().BeFalse();
            wrapper.HasTestFrameworkReference("").Should().BeFalse();
            wrapper.HasTestFrameworkReference("   ").Should().BeFalse();
        }

        [Fact]
        public void GetReferencedAssemblies_ShouldReturnReferencedAssemblies()
        {
            // Arrange
            using var wrapper = new TestAssemblyWrapper(_testAssembly, _assemblyPath, _frameworkVersion);

            // Act
            var referencedAssemblies = wrapper.GetReferencedAssemblies();

            // Assert
            referencedAssemblies.Should().NotBeNull();
            referencedAssemblies.Should().NotBeEmpty();
        }

        [Fact]
        public void TargetFramework_ShouldReturnTargetFrameworkString()
        {
            // Arrange
            using var wrapper = new TestAssemblyWrapper(_testAssembly, _assemblyPath, _frameworkVersion);

            // Act
            var targetFramework = wrapper.TargetFramework;

            // Assert
            targetFramework.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void Dispose_ShouldMarkAsDisposed()
        {
            // Arrange
            var wrapper = new TestAssemblyWrapper(_testAssembly, _assemblyPath, _frameworkVersion);

            // Act
            wrapper.Dispose();

            // Assert
            var action = () => wrapper.GetTypes();
            action.Should().Throw<ObjectDisposedException>();
        }

        [Fact]
        public void Dispose_CalledMultipleTimes_ShouldNotThrow()
        {
            // Arrange
            var wrapper = new TestAssemblyWrapper(_testAssembly, _assemblyPath, _frameworkVersion);

            // Act & Assert
            var action = () =>
            {
                wrapper.Dispose();
                wrapper.Dispose();
            };
            action.Should().NotThrow();
        }

        [Fact]
        public void AfterDispose_AllMethodsShouldThrowObjectDisposedException()
        {
            // Arrange
            var wrapper = new TestAssemblyWrapper(_testAssembly, _assemblyPath, _frameworkVersion);
            wrapper.Dispose();

            // Act & Assert
            ((Action)(() => wrapper.GetTypes())).Should().Throw<ObjectDisposedException>();
            ((Action)(() => wrapper.GetTypes(t => true))).Should().Throw<ObjectDisposedException>();
            ((Action)(() => wrapper.GetTestClasses())).Should().Throw<ObjectDisposedException>();
            ((Action)(() => wrapper.GetTestMethods(typeof(string)))).Should().Throw<ObjectDisposedException>();
            ((Action)(() => wrapper.GetAllTestMethods())).Should().Throw<ObjectDisposedException>();
            ((Action)(() => wrapper.GetCustomAttributes<Attribute>())).Should().Throw<ObjectDisposedException>();
            ((Action)(() => wrapper.HasTestFrameworkReference("test"))).Should().Throw<ObjectDisposedException>();
            ((Action)(() => wrapper.GetReferencedAssemblies())).Should().Throw<ObjectDisposedException>();
        }

        [Fact]
        public void Properties_AfterDispose_ShouldStillBeAccessible()
        {
            // Arrange
            var wrapper = new TestAssemblyWrapper(_testAssembly, _assemblyPath, _frameworkVersion);
            wrapper.Dispose();

            // Act & Assert
            // Properties that don't require the assembly to be loaded should still work
            wrapper.AssemblyPath.Should().Be(_assemblyPath);
            wrapper.FrameworkVersion.Should().Be(_frameworkVersion);
            wrapper.UnderlyingAssembly.Should().BeSameAs(_testAssembly);
        }
    }

    // Helper test class for testing IsTestClass functionality
    [System.ComponentModel.DataAnnotations.Display(Name = "NotATestClass")]
    public class NotATestClass
    {
        public void RegularMethod() { }
    }
}