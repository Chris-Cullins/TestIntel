using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TestIntelligence.Core.Assembly
{
    /// <summary>
    /// Default implementation of ITestAssembly that wraps a loaded assembly with metadata extraction capabilities.
    /// </summary>
    public class TestAssemblyWrapper : ITestAssembly
    {
        private readonly System.Reflection.Assembly _assembly;
        private readonly string _assemblyPath;
        private readonly FrameworkVersion _frameworkVersion;
        private readonly Lazy<Type[]> _types;
        private readonly Lazy<Type[]> _testClasses;
        private readonly Lazy<MethodInfo[]> _testMethods;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the TestAssemblyWrapper.
        /// </summary>
        /// <param name="assembly">The loaded assembly to wrap.</param>
        /// <param name="assemblyPath">The path to the assembly file.</param>
        /// <param name="frameworkVersion">The detected framework version.</param>
        public TestAssemblyWrapper(System.Reflection.Assembly assembly, string assemblyPath, FrameworkVersion frameworkVersion)
        {
            _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
            _assemblyPath = assemblyPath ?? throw new ArgumentNullException(nameof(assemblyPath));
            _frameworkVersion = frameworkVersion;

            // Initialize lazy-loaded collections for performance
            _types = new Lazy<Type[]>(LoadTypes);
            _testClasses = new Lazy<Type[]>(LoadTestClasses);
            _testMethods = new Lazy<MethodInfo[]>(LoadAllTestMethods);
        }

        /// <inheritdoc />
        public string AssemblyPath => _assemblyPath;

        /// <inheritdoc />
        public string AssemblyName => _assembly.GetName().Name ?? "";

        /// <inheritdoc />
        public string TargetFramework => GetTargetFramework();

        /// <inheritdoc />
        public FrameworkVersion FrameworkVersion => _frameworkVersion;

        /// <inheritdoc />
        public System.Reflection.Assembly UnderlyingAssembly => _assembly;

        /// <inheritdoc />
        public IReadOnlyList<Type> GetTypes()
        {
            ThrowIfDisposed();
            return _types.Value;
        }

        /// <inheritdoc />
        public IReadOnlyList<Type> GetTypes(Func<Type, bool> predicate)
        {
            ThrowIfDisposed();
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            return _types.Value.Where(predicate).ToArray();
        }

        /// <inheritdoc />
        public IReadOnlyList<Type> GetTestClasses()
        {
            ThrowIfDisposed();
            return _testClasses.Value;
        }

        /// <inheritdoc />
        public IReadOnlyList<MethodInfo> GetTestMethods(Type testClass)
        {
            ThrowIfDisposed();
            if (testClass == null)
                throw new ArgumentNullException(nameof(testClass));

            return testClass.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(IsTestMethod)
                .ToArray();
        }

        /// <inheritdoc />
        public IReadOnlyList<MethodInfo> GetAllTestMethods()
        {
            ThrowIfDisposed();
            return _testMethods.Value;
        }

        /// <inheritdoc />
        public IReadOnlyList<T> GetCustomAttributes<T>() where T : Attribute
        {
            ThrowIfDisposed();
            try
            {
                return _assembly.GetCustomAttributes<T>().ToArray();
            }
            catch
            {
                return Array.Empty<T>();
            }
        }

        /// <inheritdoc />
        public bool HasTestFrameworkReference(string frameworkName)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(frameworkName))
                return false;

            try
            {
                var referencedAssemblies = _assembly.GetReferencedAssemblies();
                return referencedAssemblies.Any(a => 
                    a.Name != null && a.Name.IndexOf(frameworkName, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<AssemblyName> GetReferencedAssemblies()
        {
            ThrowIfDisposed();
            try
            {
                return _assembly.GetReferencedAssemblies();
            }
            catch
            {
                return Array.Empty<AssemblyName>();
            }
        }

        /// <summary>
        /// Loads all types from the assembly safely.
        /// </summary>
        private Type[] LoadTypes()
        {
            try
            {
                return _assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Return only the types that loaded successfully
                return ex.Types.Where(t => t != null).ToArray()!;
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        /// <summary>
        /// Loads test classes from the assembly.
        /// </summary>
        private Type[] LoadTestClasses()
        {
            return _types.Value
                .Where(IsTestClass)
                .ToArray();
        }

        /// <summary>
        /// Loads all test methods from all test classes.
        /// </summary>
        private MethodInfo[] LoadAllTestMethods()
        {
            var testMethods = new List<MethodInfo>();

            foreach (var testClass in _testClasses.Value)
            {
                try
                {
                    var methods = testClass.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                        .Where(IsTestMethod);
                    
                    testMethods.AddRange(methods);
                }
                catch
                {
                    // Skip this test class if we can't get its methods
                }
            }

            return testMethods.ToArray();
        }

        /// <summary>
        /// Determines if a type is a test class based on common test framework patterns.
        /// </summary>
        private static bool IsTestClass(Type type)
        {
            if (type == null || !type.IsClass || type.IsAbstract)
                return false;

            try
            {
                // Check for test framework class attributes
                var attributes = type.GetCustomAttributes(false);
                foreach (var attr in attributes)
                {
                    var attrTypeName = attr.GetType().Name;
                    if (IsTestClassAttribute(attrTypeName))
                        return true;
                }

                // Check if the class has any test methods
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                return methods.Any(IsTestMethod);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Determines if a method is a test method based on common test framework patterns.
        /// </summary>
        private static bool IsTestMethod(MethodInfo method)
        {
            if (method == null)
                return false;

            try
            {
                var attributes = method.GetCustomAttributes(false);
                return attributes.Any(attr => IsTestMethodAttribute(attr.GetType().Name));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if an attribute name indicates a test class.
        /// </summary>
        private static bool IsTestClassAttribute(string attributeName)
        {
            var testClassAttributes = new[]
            {
                "TestClassAttribute", "TestClass",
                "TestFixtureAttribute", "TestFixture",
                "TestSuiteAttribute", "TestSuite"
            };

            return testClassAttributes.Any(attr => 
                attributeName.Equals(attr, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Checks if an attribute name indicates a test method.
        /// </summary>
        private static bool IsTestMethodAttribute(string attributeName)
        {
            var testMethodAttributes = new[]
            {
                "TestMethodAttribute", "TestMethod",
                "TestAttribute", "Test",
                "FactAttribute", "Fact",
                "TheoryAttribute", "Theory",
                "DataTestMethodAttribute", "DataTestMethod",
                "TestCaseAttribute", "TestCase"
            };

            return testMethodAttributes.Any(attr => 
                attributeName.Equals(attr, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets the target framework information from the assembly.
        /// </summary>
        private string GetTargetFramework()
        {
            try
            {
                var targetFrameworkAttr = _assembly.GetCustomAttributes(typeof(System.Runtime.Versioning.TargetFrameworkAttribute), false)
                    .Cast<System.Runtime.Versioning.TargetFrameworkAttribute>()
                    .FirstOrDefault();

                return targetFrameworkAttr?.FrameworkName ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// Throws an ObjectDisposedException if this instance has been disposed.
        /// </summary>
        protected void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TestAssemblyWrapper));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                DisposeCore();
            }
            finally
            {
                _disposed = true;
            }
        }

        /// <summary>
        /// Override this method to provide custom disposal logic.
        /// </summary>
        protected virtual void DisposeCore()
        {
            // Base implementation - no resources to dispose
            // Derived classes can override to clean up specific resources
        }
    }
}