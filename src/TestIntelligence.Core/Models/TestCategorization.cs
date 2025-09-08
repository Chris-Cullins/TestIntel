using System;

namespace TestIntelligence.Core.Models
{
    /// <summary>
    /// Basic test information needed for categorization.
    /// This is a lightweight version used specifically for categorization purposes.
    /// </summary>
    public class TestCategorizationInfo
    {
        public TestMethod TestMethod { get; }
        public string MethodName { get; }
        public string ClassName { get; }
        public string NamespaceName { get; }
        public string AssemblyName { get; }

        public TestCategorizationInfo(TestMethod testMethod)
        {
            TestMethod = testMethod ?? throw new ArgumentNullException(nameof(testMethod));
            MethodName = testMethod.MethodInfo.Name;
            ClassName = testMethod.MethodInfo.DeclaringType?.Name ?? string.Empty;
            NamespaceName = testMethod.MethodInfo.DeclaringType?.Namespace ?? string.Empty;
            AssemblyName = testMethod.AssemblyPath;
        }

        public TestCategorizationInfo(string methodName, string className, string namespaceName, string assemblyName)
        {
            MethodName = methodName ?? string.Empty;
            ClassName = className ?? string.Empty;
            NamespaceName = namespaceName ?? string.Empty;
            AssemblyName = assemblyName ?? string.Empty;
            TestMethod = null!; // For test purposes
        }
    }
}