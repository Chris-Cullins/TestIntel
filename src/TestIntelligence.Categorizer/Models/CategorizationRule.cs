using System;
using System.Text.RegularExpressions;
using TestIntelligence.Core.Models;

namespace TestIntelligence.Categorizer.Models
{
    /// <summary>
    /// Represents a rule for categorizing tests based on patterns.
    /// </summary>
    public class CategorizationRule
    {
        /// <summary>
        /// The category to assign when this rule matches.
        /// </summary>
        public TestCategory Category { get; }

        /// <summary>
        /// Priority of this rule (higher numbers take precedence).
        /// </summary>
        public int Priority { get; }

        /// <summary>
        /// Description of what this rule matches.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// The pattern matching function.
        /// </summary>
        public Func<TestMethodContext, bool> Matcher { get; }

        public CategorizationRule(
            TestCategory category,
            int priority,
            string description,
            Func<TestMethodContext, bool> matcher)
        {
            Category = category;
            Priority = priority;
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
        }

        /// <summary>
        /// Creates a rule that matches based on method name patterns.
        /// </summary>
        public static CategorizationRule CreateMethodNameRule(
            TestCategory category,
            int priority,
            string description,
            params string[] patterns)
        {
            return new CategorizationRule(
                category,
                priority,
                description,
                context => ContainsAny(context.MethodName, patterns));
        }

        /// <summary>
        /// Creates a rule that matches based on class name patterns.
        /// </summary>
        public static CategorizationRule CreateClassNameRule(
            TestCategory category,
            int priority,
            string description,
            params string[] patterns)
        {
            return new CategorizationRule(
                category,
                priority,
                description,
                context => ContainsAny(context.ClassName, patterns));
        }

        /// <summary>
        /// Creates a rule that matches based on namespace patterns.
        /// </summary>
        public static CategorizationRule CreateNamespaceRule(
            TestCategory category,
            int priority,
            string description,
            params string[] patterns)
        {
            return new CategorizationRule(
                category,
                priority,
                description,
                context => ContainsAny(context.NamespaceName, patterns));
        }

        /// <summary>
        /// Creates a rule that matches based on assembly name patterns.
        /// </summary>
        public static CategorizationRule CreateAssemblyRule(
            TestCategory category,
            int priority,
            string description,
            params string[] patterns)
        {
            return new CategorizationRule(
                category,
                priority,
                description,
                context => ContainsAny(context.AssemblyName, patterns));
        }

        /// <summary>
        /// Creates a composite rule that matches multiple pattern types.
        /// </summary>
        public static CategorizationRule CreateCompositeRule(
            TestCategory category,
            int priority,
            string description,
            string[]? methodPatterns = null,
            string[]? classPatterns = null,
            string[]? namespacePatterns = null,
            string[]? assemblyPatterns = null)
        {
            return new CategorizationRule(
                category,
                priority,
                description,
                context => 
                    (methodPatterns != null && ContainsAny(context.MethodName, methodPatterns)) ||
                    (classPatterns != null && ContainsAny(context.ClassName, classPatterns)) ||
                    (namespacePatterns != null && ContainsAny(context.NamespaceName, namespacePatterns)) ||
                    (assemblyPatterns != null && ContainsAny(context.AssemblyName, assemblyPatterns)));
        }

        private static bool ContainsAny(string text, string[] patterns)
        {
            if (string.IsNullOrEmpty(text) || patterns == null || patterns.Length == 0)
                return false;

            var lowerText = text.ToLowerInvariant();
            foreach (var pattern in patterns)
            {
                if (!string.IsNullOrEmpty(pattern) && lowerText.Contains(pattern.ToLowerInvariant()))
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Context information about a test method for categorization.
    /// </summary>
    public class TestMethodContext
    {
        public string MethodName { get; }
        public string ClassName { get; }
        public string NamespaceName { get; }
        public string AssemblyName { get; }

        public TestMethodContext(string methodName, string className, string namespaceName, string assemblyName)
        {
            MethodName = methodName ?? string.Empty;
            ClassName = className ?? string.Empty;
            NamespaceName = namespaceName ?? string.Empty;
            AssemblyName = assemblyName ?? string.Empty;
        }
    }
}