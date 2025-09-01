using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;
using TestIntelligence.Core.Assembly;

namespace TestIntelligence.Core.Models
{
    /// <summary>
    /// Represents a test method discovered in an assembly.
    /// </summary>
    public class TestMethod
    {
        public TestMethod(
            MethodInfo methodInfo,
            Type declaringType,
            string assemblyPath,
            FrameworkVersion frameworkVersion)
        {
            MethodInfo = methodInfo ?? throw new ArgumentNullException(nameof(methodInfo));
            DeclaringType = declaringType ?? throw new ArgumentNullException(nameof(declaringType));
            AssemblyPath = assemblyPath ?? throw new ArgumentNullException(nameof(assemblyPath));
            FrameworkVersion = frameworkVersion;
            
            MethodName = methodInfo.Name;
            ClassName = declaringType.Name;
            FullClassName = declaringType.FullName ?? declaringType.Name;
            TestAttributes = new List<Attribute>();
            
            ExtractTestAttributes();
        }

        /// <summary>
        /// The reflection method info for this test method.
        /// </summary>
        [JsonIgnore]
        public MethodInfo MethodInfo { get; }

        /// <summary>
        /// The declaring type that contains this test method.
        /// </summary>
        [JsonIgnore]
        public Type DeclaringType { get; }

        /// <summary>
        /// The path to the assembly containing this test.
        /// </summary>
        public string AssemblyPath { get; }

        /// <summary>
        /// The .NET framework version of the assembly.
        /// </summary>
        public FrameworkVersion FrameworkVersion { get; }

        /// <summary>
        /// The name of the test method.
        /// </summary>
        public string MethodName { get; }

        /// <summary>
        /// The simple name of the class containing this test.
        /// </summary>
        public string ClassName { get; }

        /// <summary>
        /// The full name (including namespace) of the class containing this test.
        /// </summary>
        public string FullClassName { get; }

        /// <summary>
        /// Test-related attributes found on this method.
        /// </summary>
        [JsonIgnore]
        public IReadOnlyList<Attribute> TestAttributes { get; private set; }

        /// <summary>
        /// Whether this method is marked with [Test] attribute.
        /// </summary>
        public bool IsTest { get; private set; }

        /// <summary>
        /// Whether this method is marked with [TestCase] attribute(s).
        /// </summary>
        public bool IsTestCase { get; private set; }

        /// <summary>
        /// Whether this method is marked with [SetUp] attribute.
        /// </summary>
        public bool IsSetUp { get; private set; }

        /// <summary>
        /// Whether this method is marked with [TearDown] attribute.
        /// </summary>
        public bool IsTearDown { get; private set; }

        /// <summary>
        /// Whether this method is marked with [OneTimeSetUp] attribute.
        /// </summary>
        public bool IsOneTimeSetUp { get; private set; }

        /// <summary>
        /// Whether this method is marked with [OneTimeTearDown] attribute.
        /// </summary>
        public bool IsOneTimeTearDown { get; private set; }

        /// <summary>
        /// Gets a unique identifier for this test method.
        /// </summary>
        public string GetUniqueId()
        {
            return $"{FullClassName}.{MethodName}";
        }

        /// <summary>
        /// Gets the display name for this test method.
        /// </summary>
        public string GetDisplayName()
        {
            return $"{ClassName}.{MethodName}";
        }

        /// <summary>
        /// Extracts and categorizes test-related attributes.
        /// </summary>
        private void ExtractTestAttributes()
        {
            var attributes = MethodInfo.GetCustomAttributes(inherit: false);
            var testAttributes = new List<Attribute>();

            foreach (var attribute in attributes)
            {
                var attributeName = attribute.GetType().Name;
                
                // Check for NUnit test attributes
                switch (attributeName)
                {
                    case "TestAttribute":
                        IsTest = true;
                        testAttributes.Add((Attribute)attribute);
                        break;
                    
                    case "TestCaseAttribute":
                        IsTestCase = true;
                        testAttributes.Add((Attribute)attribute);
                        break;
                        
                    case "SetUpAttribute":
                        IsSetUp = true;
                        testAttributes.Add((Attribute)attribute);
                        break;
                        
                    case "TearDownAttribute":
                        IsTearDown = true;
                        testAttributes.Add((Attribute)attribute);
                        break;
                        
                    case "OneTimeSetUpAttribute":
                        IsOneTimeSetUp = true;
                        testAttributes.Add((Attribute)attribute);
                        break;
                        
                    case "OneTimeTearDownAttribute":
                        IsOneTimeTearDown = true;
                        testAttributes.Add((Attribute)attribute);
                        break;

                    // xUnit attributes
                    case "FactAttribute":
                        IsTest = true;
                        testAttributes.Add((Attribute)attribute);
                        break;
                        
                    case "TheoryAttribute":
                        IsTestCase = true;
                        testAttributes.Add((Attribute)attribute);
                        break;

                    // MSTest attributes  
                    case "TestMethodAttribute":
                        IsTest = true;
                        testAttributes.Add((Attribute)attribute);
                        break;
                        
                    case "DataTestMethodAttribute":
                        IsTestCase = true;
                        testAttributes.Add((Attribute)attribute);
                        break;
                        
                    case "TestInitializeAttribute":
                        IsSetUp = true;
                        testAttributes.Add((Attribute)attribute);
                        break;
                        
                    case "TestCleanupAttribute":
                        IsTearDown = true;
                        testAttributes.Add((Attribute)attribute);
                        break;
                        
                    case "ClassInitializeAttribute":
                        IsOneTimeSetUp = true;
                        testAttributes.Add((Attribute)attribute);
                        break;
                        
                    case "ClassCleanupAttribute":
                        IsOneTimeTearDown = true;
                        testAttributes.Add((Attribute)attribute);
                        break;
                }
                
                // Include any test-related attributes
                if (attributeName.Contains("Test") || 
                    attributeName.Contains("SetUp") || 
                    attributeName.Contains("TearDown") ||
                    attributeName.Contains("Ignore") ||
                    attributeName.Contains("Category") ||
                    attributeName.Contains("Fact") ||
                    attributeName.Contains("Theory") ||
                    attributeName.Contains("Initialize") ||
                    attributeName.Contains("Cleanup") ||
                    attributeName.Contains("Trait") ||
                    attributeName.Contains("Skip"))
                {
                    testAttributes.Add((Attribute)attribute);
                }
            }

            TestAttributes = testAttributes.AsReadOnly();
        }

        /// <summary>
        /// Determines if this is an executable test method.
        /// </summary>
        public bool IsExecutableTest()
        {
            return IsTest || IsTestCase;
        }

        /// <summary>
        /// Gets all test case parameters if this is a parameterized test.
        /// </summary>
        public IEnumerable<object[]> GetTestCaseParameters()
        {
            if (!IsTestCase) 
                yield break;

            foreach (var attribute in TestAttributes)
            {
                if (attribute.GetType().Name == "TestCaseAttribute")
                {
                    // Use reflection to get the Arguments property
                    var argsProperty = attribute.GetType().GetProperty("Arguments");
                    if (argsProperty?.GetValue(attribute) is object[] args)
                    {
                        yield return args;
                    }
                }
            }
        }

        public override string ToString()
        {
            return GetDisplayName();
        }
    }
}