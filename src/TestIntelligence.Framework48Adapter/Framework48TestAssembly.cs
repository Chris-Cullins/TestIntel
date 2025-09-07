using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TestIntelligence.Core.Assembly;

namespace TestIntelligence.Framework48Adapter
{
    /// <summary>
    /// Test assembly implementation for .NET Framework 4.8 assemblies.
    /// </summary>
    public class Framework48TestAssembly : ITestAssembly
    {
        private readonly Assembly _assembly;
        private readonly FrameworkVersion _frameworkVersion;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the Framework48TestAssembly class.
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly.</param>
        /// <param name="assembly">The loaded assembly.</param>
        /// <param name="frameworkVersion">The framework version.</param>
        public Framework48TestAssembly(string assemblyPath, Assembly assembly, FrameworkVersion frameworkVersion)
        {
            AssemblyPath = assemblyPath ?? throw new ArgumentNullException(nameof(assemblyPath));
            _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
            _frameworkVersion = frameworkVersion;
            
            AssemblyName = assembly.GetName().Name ?? "Unknown";
            TargetFramework = GetTargetFrameworkString();
        }

        /// <inheritdoc />
        public string AssemblyPath { get; }

        /// <inheritdoc />
        public string AssemblyName { get; }

        /// <inheritdoc />
        public string TargetFramework { get; }

        /// <inheritdoc />
        public FrameworkVersion FrameworkVersion => _frameworkVersion;

        /// <inheritdoc />
        public Assembly UnderlyingAssembly => _assembly;

        /// <inheritdoc />
        public bool IsSuccess => true;

        /// <inheritdoc />
        public IReadOnlyList<string> Errors => Array.Empty<string>();

        /// <inheritdoc />
        public IReadOnlyList<Type> GetTypes()
        {
            try
            {
                return _assembly.GetTypes().ToList();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Return only the types that loaded successfully
                return ex.Types.Where(t => t != null).Cast<Type>().ToList();
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<Type> GetTypes(Func<Type, bool> predicate)
        {
            return GetTypes().Where(predicate).ToList();
        }

        /// <inheritdoc />
        public IReadOnlyList<Type> GetTestClasses()
        {
            return GetTypes(IsTestClass).ToList();
        }

        /// <inheritdoc />
        public IReadOnlyList<MethodInfo> GetTestMethods(Type testClass)
        {
            if (testClass == null)
                throw new ArgumentNullException(nameof(testClass));

            return testClass.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                           .Where(IsTestMethod)
                           .ToList();
        }

        /// <inheritdoc />
        public IReadOnlyList<MethodInfo> GetAllTestMethods()
        {
            return GetTestClasses()
                .SelectMany(GetTestMethods)
                .ToList();
        }

        /// <inheritdoc />
        public IReadOnlyList<T> GetCustomAttributes<T>() where T : Attribute
        {
            return _assembly.GetCustomAttributes<T>().ToList();
        }

        /// <inheritdoc />
        public bool HasTestFrameworkReference(string frameworkName)
        {
            return GetReferencedAssemblies()
                .Any(asm => asm.Name?.IndexOf(frameworkName, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <inheritdoc />
        public IReadOnlyList<AssemblyName> GetReferencedAssemblies()
        {
            return _assembly.GetReferencedAssemblies().ToList();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_disposed)
            {
                // .NET Framework assemblies are loaded into AppDomain and can't be unloaded
                // In a real implementation, we might use separate AppDomains for isolation
                _disposed = true;
            }
        }

        private string GetTargetFrameworkString()
        {
            try
            {
                var targetFrameworkAttribute = _assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>();
                return targetFrameworkAttribute?.FrameworkName ?? $".NETFramework,Version=v4.8";
            }
            catch
            {
                return $".NETFramework,Version=v4.8";
            }
        }

        private bool IsTestClass(Type type)
        {
            if (type.IsAbstract || type.IsInterface)
                return false;

            // NUnit test class detection
            if (type.GetCustomAttributes(true).Any(attr => 
                attr.GetType().Name.Contains("TestFixture") ||
                attr.GetType().Name.Contains("Test")))
            {
                return true;
            }

            // MSTest test class detection
            if (type.GetCustomAttributes(true).Any(attr => 
                attr.GetType().Name.Contains("TestClass")))
            {
                return true;
            }

            // xUnit test class detection (convention-based)
            if (type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                   .Any(IsTestMethod))
            {
                return true;
            }

            return false;
        }

        private bool IsTestMethod(MethodInfo method)
        {
            if (method == null || method.IsStatic || method.IsAbstract)
                return false;

            var attributes = method.GetCustomAttributes(true);
            
            // NUnit test method detection
            if (attributes.Any(attr => 
                attr.GetType().Name.Contains("Test") &&
                !attr.GetType().Name.Contains("TestFixture")))
            {
                return true;
            }

            // MSTest test method detection
            if (attributes.Any(attr => 
                attr.GetType().Name.Contains("TestMethod")))
            {
                return true;
            }

            // xUnit test method detection
            if (attributes.Any(attr => 
                attr.GetType().Name.Contains("Fact") ||
                attr.GetType().Name.Contains("Theory")))
            {
                return true;
            }

            return false;
        }
    }
}