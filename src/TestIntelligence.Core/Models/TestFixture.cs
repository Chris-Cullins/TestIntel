using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using TestIntelligence.Core.Assembly;

namespace TestIntelligence.Core.Models;
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
        [JsonIgnore]
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
        [JsonIgnore]
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
            return TestAttributeUtils.GetCategoryNames(FixtureAttributes);
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
            var fixtureAttributes = TestAttributeUtils.FilterTestRelatedAttributes(attributes).ToList();

            // Check for explicit fixture attributes
            foreach (var attribute in fixtureAttributes)
            {
                var attributeName = attribute.GetType().Name;
                if (TestAttributeUtils.IsFixtureAttribute(attributeName))
                {
                    IsTestFixture = true;
                    break;
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
                        TestAttributeUtils.IsTestAttribute(attr.GetType().Name) || 
                        TestAttributeUtils.IsTestCaseAttribute(attr.GetType().Name));
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