using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TestIntelligence.Core.Assembly;

namespace TestIntelligence.Core.Models
{
    /// <summary>
    /// Represents a test fixture (class) discovered in an assembly.
    /// </summary>
    public class TestFixture
    {
        public TestFixture(
            Type type,
            string assemblyPath,
            FrameworkVersion frameworkVersion)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            AssemblyPath = assemblyPath ?? throw new ArgumentNullException(nameof(assemblyPath));
            FrameworkVersion = frameworkVersion;
            
            ClassName = type.Name;
            FullClassName = type.FullName ?? type.Name;
            Namespace = type.Namespace ?? string.Empty;
            
            TestMethods = new List<TestMethod>();
            FixtureAttributes = new List<Attribute>();
            
            ExtractFixtureAttributes();
            DiscoverTestMethods();
        }

        /// <summary>
        /// The reflection type info for this test fixture.
        /// </summary>
        public Type Type { get; }

        /// <summary>
        /// The path to the assembly containing this fixture.
        /// </summary>
        public string AssemblyPath { get; }

        /// <summary>
        /// The .NET framework version of the assembly.
        /// </summary>
        public FrameworkVersion FrameworkVersion { get; }

        /// <summary>
        /// The simple name of the test fixture class.
        /// </summary>
        public string ClassName { get; }

        /// <summary>
        /// The full name (including namespace) of the test fixture class.
        /// </summary>
        public string FullClassName { get; }

        /// <summary>
        /// The namespace of the test fixture class.
        /// </summary>
        public string Namespace { get; }

        /// <summary>
        /// Test methods discovered in this fixture.
        /// </summary>
        public IReadOnlyList<TestMethod> TestMethods { get; private set; }

        /// <summary>
        /// Test fixture attributes found on this class.
        /// </summary>
        public IReadOnlyList<Attribute> FixtureAttributes { get; private set; }

        /// <summary>
        /// Whether this class is marked with [TestFixture] attribute.
        /// </summary>
        public bool IsTestFixture { get; private set; }

        /// <summary>
        /// Whether this fixture has setup methods.
        /// </summary>
        public bool HasSetUpMethods => TestMethods.Any(m => m.IsSetUp || m.IsOneTimeSetUp);

        /// <summary>
        /// Whether this fixture has teardown methods.
        /// </summary>
        public bool HasTearDownMethods => TestMethods.Any(m => m.IsTearDown || m.IsOneTimeTearDown);

        /// <summary>
        /// Whether this fixture has any executable tests.
        /// </summary>
        public bool HasTests => TestMethods.Any(m => m.IsExecutableTest());

        /// <summary>
        /// Gets all executable test methods in this fixture.
        /// </summary>
        public IEnumerable<TestMethod> GetExecutableTests()
        {
            return TestMethods.Where(m => m.IsExecutableTest());
        }

        /// <summary>
        /// Gets all setup methods in this fixture.
        /// </summary>
        public IEnumerable<TestMethod> GetSetUpMethods()
        {
            return TestMethods.Where(m => m.IsSetUp || m.IsOneTimeSetUp);
        }

        /// <summary>
        /// Gets all teardown methods in this fixture.
        /// </summary>
        public IEnumerable<TestMethod> GetTearDownMethods()
        {
            return TestMethods.Where(m => m.IsTearDown || m.IsOneTimeTearDown);
        }

        /// <summary>
        /// Gets categories associated with this fixture.
        /// </summary>
        public IEnumerable<string> GetCategories()
        {
            foreach (var attribute in FixtureAttributes)
            {
                if (attribute.GetType().Name == "CategoryAttribute")
                {
                    // Use reflection to get the Name property
                    var nameProperty = attribute.GetType().GetProperty("Name");
                    if (nameProperty?.GetValue(attribute) is string categoryName)
                    {
                        yield return categoryName;
                    }
                }
            }
        }

        /// <summary>
        /// Gets a unique identifier for this test fixture.
        /// </summary>
        public string GetUniqueId()
        {
            return FullClassName;
        }

        /// <summary>
        /// Extracts and categorizes fixture-related attributes.
        /// </summary>
        private void ExtractFixtureAttributes()
        {
            var attributes = Type.GetCustomAttributes(inherit: false);
            var fixtureAttributes = new List<Attribute>();

            foreach (var attribute in attributes)
            {
                var attributeName = attribute.GetType().Name;
                
                // Check for test framework fixture attributes
                switch (attributeName)
                {
                    // NUnit attributes
                    case "TestFixtureAttribute":
                        IsTestFixture = true;
                        fixtureAttributes.Add((Attribute)attribute);
                        break;
                    case "SetUpFixtureAttribute":
                        IsTestFixture = true;
                        fixtureAttributes.Add((Attribute)attribute);
                        break;
                    
                    // MSTest attributes
                    case "TestClassAttribute":
                        IsTestFixture = true;
                        fixtureAttributes.Add((Attribute)attribute);
                        break;
                    
                    // xUnit doesn't require explicit class attributes, but may have collection attributes
                    case "CollectionAttribute":
                        fixtureAttributes.Add((Attribute)attribute);
                        break;
                }
                
                // Include any test-related attributes
                if (attributeName.Contains("Test") || 
                    attributeName.Contains("Category") ||
                    attributeName.Contains("Ignore") ||
                    attributeName.Contains("Explicit") ||
                    attributeName.Contains("Trait") ||
                    attributeName.Contains("Collection") ||
                    attributeName.Contains("Owner") ||
                    attributeName.Contains("Priority"))
                {
                    fixtureAttributes.Add((Attribute)attribute);
                }
            }

            // For xUnit, also check if the class is a test fixture by having test methods
            if (!IsTestFixture)
            {
                var methods = Type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                IsTestFixture = methods.Any(method => 
                {
                    var methodAttributes = method.GetCustomAttributes(false);
                    return methodAttributes.Any(attr => 
                        attr.GetType().Name == "FactAttribute" || 
                        attr.GetType().Name == "TheoryAttribute");
                });
            }

            FixtureAttributes = fixtureAttributes.AsReadOnly();
        }

        /// <summary>
        /// Discovers test methods in this fixture.
        /// </summary>
        private void DiscoverTestMethods()
        {
            var methods = Type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            var testMethods = new List<TestMethod>();

            foreach (var method in methods)
            {
                var testMethod = new TestMethod(method, Type, AssemblyPath, FrameworkVersion);
                
                // Include methods with any test-related attributes
                if (testMethod.TestAttributes.Any())
                {
                    testMethods.Add(testMethod);
                }
            }

            TestMethods = testMethods.AsReadOnly();
        }

        public override string ToString()
        {
            return $"{ClassName} ({TestMethods.Count(m => m.IsExecutableTest())} tests)";
        }
    }
}