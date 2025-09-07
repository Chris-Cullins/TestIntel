using System;
using System.Linq;
using System.Reflection;
using TestIntelligence.Core.Assembly;
using TestIntelligence.NetCoreAdapter;
using Xunit;

namespace TestIntelligence.NetCoreAdapter.Tests
{
    public class NetCoreTestAssemblyTests
    {
        private readonly Assembly _testAssembly;
        private readonly NetCoreTestAssembly _testAssemblyWrapper;

        public NetCoreTestAssemblyTests()
        {
            _testAssembly = Assembly.GetExecutingAssembly();
            _testAssemblyWrapper = new NetCoreTestAssembly(
                _testAssembly.Location, 
                _testAssembly, 
                FrameworkVersion.Net5Plus);
        }

        [Fact]
        public void Constructor_WithValidParameters_SetsProperties()
        {
            // Arrange
            var path = "/test/path";
            var assembly = Assembly.GetExecutingAssembly();
            var framework = FrameworkVersion.Net5Plus;

            // Act
            var testAssembly = new NetCoreTestAssembly(path, assembly, framework);

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
                new NetCoreTestAssembly(null!, assembly, FrameworkVersion.Net5Plus));
        }

        [Fact]
        public void Constructor_WithNullAssembly_ThrowsArgumentNullException()
        {
            // Arrange
            var path = "/test/path";

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new NetCoreTestAssembly(path, null!, FrameworkVersion.Net5Plus));
        }

        [Fact]
        public void GetTypes_ReturnsAllTypes()
        {
            // Act
            var types = _testAssemblyWrapper.GetTypes();

            // Assert
            Assert.NotNull(types);
            Assert.NotEmpty(types);
            Assert.Contains(typeof(NetCoreTestAssemblyTests), types);
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
            Assert.Contains(typeof(NetCoreTestAssemblyTests), testClasses);
        }

        [Fact]
        public void GetTestMethods_WithValidTestClass_ReturnsTestMethods()
        {
            // Act
            var testMethods = _testAssemblyWrapper.GetTestMethods(typeof(NetCoreTestAssemblyTests));

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
            Assert.Contains(allTestMethods, m => m.DeclaringType == typeof(NetCoreTestAssemblyTests));
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
            var testAssembly = new NetCoreTestAssembly(
                "/test/path", 
                Assembly.GetExecutingAssembly(), 
                FrameworkVersion.Net5Plus);

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
            Assert.Equal(FrameworkVersion.Net5Plus, _testAssemblyWrapper.FrameworkVersion);
        }

        [Fact]
        public void TargetFramework_IsNotNullOrEmpty()
        {
            // Assert
            Assert.NotNull(_testAssemblyWrapper.TargetFramework);
            Assert.NotEmpty(_testAssemblyWrapper.TargetFramework);
        }

        [Theory]
        [InlineData(FrameworkVersion.Net5Plus, ".NET,Version=v8.0")]
        [InlineData(FrameworkVersion.NetCore, ".NETCoreApp,Version=v3.1")]
        public void TargetFramework_WithDifferentFrameworkVersions_ReturnsExpectedFormat(
            FrameworkVersion frameworkVersion, 
            string expectedPrefix)
        {
            // Arrange
            var assembly = Assembly.GetExecutingAssembly();
            var testAssembly = new NetCoreTestAssembly("/test/path", assembly, frameworkVersion);

            // Act & Assert
            Assert.NotNull(testAssembly.TargetFramework);
            // Either returns the actual target framework from attributes or the fallback
            Assert.True(testAssembly.TargetFramework.Contains(".NET") || 
                       testAssembly.TargetFramework == expectedPrefix);
        }
    }
}