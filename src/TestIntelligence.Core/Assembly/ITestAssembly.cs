using System;
using System.Collections.Generic;
using System.Reflection;

namespace TestIntelligence.Core.Assembly
{
    /// <summary>
    /// Represents a loaded test assembly with metadata and type information.
    /// </summary>
    public interface ITestAssembly : IDisposable
    {
        /// <summary>
        /// Gets the assembly path.
        /// </summary>
        string AssemblyPath { get; }

        /// <summary>
        /// Gets the assembly name.
        /// </summary>
        string AssemblyName { get; }

        /// <summary>
        /// Gets the target framework of the assembly.
        /// </summary>
        string TargetFramework { get; }

        /// <summary>
        /// Gets the framework version detected for this assembly.
        /// </summary>
        FrameworkVersion FrameworkVersion { get; }

        /// <summary>
        /// Gets the underlying assembly reference (use with caution for isolation).
        /// </summary>
        System.Reflection.Assembly UnderlyingAssembly { get; }

        /// <summary>
        /// Gets all types in the assembly.
        /// </summary>
        IReadOnlyList<Type> GetTypes();

        /// <summary>
        /// Gets types that match the specified predicate.
        /// </summary>
        IReadOnlyList<Type> GetTypes(Func<Type, bool> predicate);

        /// <summary>
        /// Gets test classes (classes with test framework attributes).
        /// </summary>
        IReadOnlyList<Type> GetTestClasses();

        /// <summary>
        /// Gets test methods from the specified type.
        /// </summary>
        IReadOnlyList<MethodInfo> GetTestMethods(Type testClass);

        /// <summary>
        /// Gets all test methods in the assembly.
        /// </summary>
        IReadOnlyList<MethodInfo> GetAllTestMethods();

        /// <summary>
        /// Gets custom attributes of the specified type.
        /// </summary>
        IReadOnlyList<T> GetCustomAttributes<T>() where T : Attribute;

        /// <summary>
        /// Determines if the assembly has references to specific test frameworks.
        /// </summary>
        bool HasTestFrameworkReference(string frameworkName);

        /// <summary>
        /// Gets the referenced assemblies.
        /// </summary>
        IReadOnlyList<AssemblyName> GetReferencedAssemblies();

        /// <summary>
        /// Gets a value indicating whether the assembly was loaded successfully.
        /// </summary>
        bool IsSuccess { get; }

        /// <summary>
        /// Gets the errors that occurred during assembly loading, if any.
        /// </summary>
        IReadOnlyList<string> Errors { get; }
    }
}