using System;
using System.Linq;
using System.Reflection;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Framework48Adapter;
using Xunit;

namespace TestIntelligence.Framework48Adapter.Tests
{
    public class Framework48TestAssemblyTests
    {
        private readonly Assembly _testAssembly;
        private readonly Framework48TestAssembly _testAssemblyWrapper;

        public Framework48TestAssemblyTests()
        {
            _testAssembly = Assembly.GetExecutingAssembly();
            _testAssemblyWrapper = new Framework48TestAssembly(
                _testAssembly.Location, 
                _testAssembly, 
                FrameworkVersion.NetFramework48);
        }

        [Fact]
        public void Constructor_WithValidParameters_SetsProperties()
        {
            // Arrange
            var path = "/test/path";
            var assembly = Assembly.GetExecutingAssembly();
            var framework = FrameworkVersion.NetFramework48;

            // Act
            var testAssembly = new Framework48TestAssembly(path, assembly, framework);

            // Assert
            Assert.Equal(path, testAssembly.AssemblyPath);
            Assert.Equal(assembly.GetName().Name, testAssembly.AssemblyName);
            Assert.Equal(framework, testAssembly.FrameworkVersion);
            Assert.Same(assembly, testAssembly.UnderlyingAssembly);
            Assert.NotNull(testAssembly.TargetFramework);
        }

        [Fact]
        public void Constructor_WithNullAssemblyPath_ThrowsArgumentNullException()
        {
            // Arrange
            var assembly = Assembly.GetExecutingAssembly();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new Framework48TestAssembly(null!, assembly, FrameworkVersion.NetFramework48));
        }

        [Fact]
        public void Constructor_WithNullAssembly_ThrowsArgumentNullException()
        {
            // Arrange
            var path = "/test/path";

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new Framework48TestAssembly(path, null!, FrameworkVersion.NetFramework48));
        }

        [Fact]
        public void GetTypes_ReturnsAllTypes()
        {
            // Act
            var types = _testAssemblyWrapper.GetTypes();

            // Assert
            Assert.NotNull(types);
            Assert.NotEmpty(types);
            Assert.Contains(typeof(Framework48TestAssemblyTests), types);
        }

        [Fact]
        public void GetTypes_WithPredicate_FiltersTypes()
        {
            // Act
            var testTypes = _testAssemblyWrapper.GetTypes(t => t.Name.Contains("Test"));

            // Assert
            Assert.NotNull(testTypes);
            Assert.All(testTypes, type => Assert.Contains("Test", type.Name));
        }

        [Fact]
        public void GetTestClasses_ReturnsTestClasses()
        {
            // Act
            var testClasses = _testAssemblyWrapper.GetTestClasses();

            // Assert
            Assert.NotNull(testClasses);
            // This test class should be detected as a test class
            Assert.Contains(typeof(Framework48TestAssemblyTests), testClasses);
        }

        [Fact]
        public void GetTestMethods_WithValidTestClass_ReturnsTestMethods()
        {
            // Act
            var testMethods = _testAssemblyWrapper.GetTestMethods(typeof(Framework48TestAssemblyTests));

            // Assert
            Assert.NotNull(testMethods);
            Assert.NotEmpty(testMethods);
            // This test method should be detected
            Assert.Contains(testMethods, m => m.Name == nameof(GetTestMethods_WithValidTestClass_ReturnsTestMethods));
        }

        [Fact]
        public void GetTestMethods_WithNullTestClass_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                _testAssemblyWrapper.GetTestMethods(null!));
        }

        [Fact]
        public void GetAllTestMethods_ReturnsAllTestMethods()
        {
            // Act
            var allTestMethods = _testAssemblyWrapper.GetAllTestMethods();

            // Assert
            Assert.NotNull(allTestMethods);
            Assert.NotEmpty(allTestMethods);
            // Should contain test methods from this test class
            Assert.Contains(allTestMethods, m => m.DeclaringType == typeof(Framework48TestAssemblyTests));
        }

        [Fact]
        public void GetCustomAttributes_ReturnsAssemblyAttributes()
        {
            // Act
            var attributes = _testAssemblyWrapper.GetCustomAttributes<AssemblyTitleAttribute>();

            // Assert
            Assert.NotNull(attributes);
            // May or may not have AssemblyTitle attributes - just test it doesn't throw
        }

        [Fact]
        public void HasTestFrameworkReference_WithExistingFramework_ReturnsTrue()
        {
            // Act
            var hasXunit = _testAssemblyWrapper.HasTestFrameworkReference("xunit");

            // Assert
            Assert.True(hasXunit); // This test project references xunit
        }

        [Fact]
        public void HasTestFrameworkReference_WithNonExistingFramework_ReturnsFalse()
        {
            // Act
            var hasNonExistingFramework = _testAssemblyWrapper.HasTestFrameworkReference("NonExistingFramework");

            // Assert
            Assert.False(hasNonExistingFramework);
        }

        [Fact]
        public void GetReferencedAssemblies_ReturnsAssemblyList()
        {
            // Act
            var referencedAssemblies = _testAssemblyWrapper.GetReferencedAssemblies();

            // Assert
            Assert.NotNull(referencedAssemblies);
            Assert.NotEmpty(referencedAssemblies);
            // Should contain System assemblies
            Assert.Contains(referencedAssemblies, asm => asm.Name?.Contains("System") == true);
        }

        [Fact]
        public void Dispose_DoesNotThrow()
        {
            // Arrange
            var testAssembly = new Framework48TestAssembly(
                "/test/path", 
                Assembly.GetExecutingAssembly(), 
                FrameworkVersion.NetFramework48);

            // Act & Assert
            var exception = Record.Exception(() => testAssembly.Dispose());
            Assert.Null(exception);
        }

        [Fact]
        public void AssemblyPath_ReturnsCorrectPath()
        {
            // Assert
            Assert.Equal(_testAssembly.Location, _testAssemblyWrapper.AssemblyPath);
        }

        [Fact]
        public void AssemblyName_ReturnsCorrectName()
        {
            // Assert
            Assert.Equal(_testAssembly.GetName().Name, _testAssemblyWrapper.AssemblyName);
        }

        [Fact]
        public void FrameworkVersion_ReturnsCorrectVersion()
        {
            // Assert
            Assert.Equal(FrameworkVersion.NetFramework48, _testAssemblyWrapper.FrameworkVersion);
        }

        [Fact]
        public void TargetFramework_IsNotNullOrEmpty()
        {
            // Assert
            Assert.NotNull(_testAssemblyWrapper.TargetFramework);
            Assert.NotEmpty(_testAssemblyWrapper.TargetFramework);
        }
    }
}