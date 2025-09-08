using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Categorizer.Models;
using TestIntelligence.Core.Models;

namespace TestIntelligence.Categorizer
{
    /// <summary>
    /// Default implementation of test categorization using pattern-based rules.
    /// </summary>
    public class DefaultTestCategorizer : ITestCategorizer
    {
        private readonly List<CategorizationRule> _rules;

        public DefaultTestCategorizer()
        {
            _rules = CreateDefaultRules();
        }

        public DefaultTestCategorizer(IEnumerable<CategorizationRule> customRules)
        {
            _rules = customRules?.ToList() ?? throw new ArgumentNullException(nameof(customRules));
        }

        public Task<TestCategory> CategorizeAsync(TestCategorizationInfo testInfo, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var context = CreateContext(testInfo);
            var category = CategorizeInternal(context);
            
            return Task.FromResult(category);
        }

        public Task<IReadOnlyDictionary<string, TestCategory>> CategorizeAsync(
            IEnumerable<TestCategorizationInfo> tests, 
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = new Dictionary<string, TestCategory>();
            
            foreach (var test in tests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var context = CreateContext(test);
                var category = CategorizeInternal(context);
                result[GetTestKey(test)] = category;
            }

            return Task.FromResult<IReadOnlyDictionary<string, TestCategory>>(result);
        }

        private TestCategory CategorizeInternal(TestMethodContext context)
        {
            // Find the highest priority rule that matches
            var matchingRule = _rules
                .Where(rule => rule.Matcher(context))
                .OrderByDescending(rule => rule.Priority)
                .FirstOrDefault();

            return matchingRule?.Category ?? TestCategory.Unit;
        }

        private static TestMethodContext CreateContext(TestCategorizationInfo testInfo)
        {
            return new TestMethodContext(testInfo.MethodName, testInfo.ClassName, testInfo.NamespaceName, testInfo.AssemblyName);
        }

        private static string GetTestKey(TestCategorizationInfo testInfo)
        {
            return $"{testInfo.NamespaceName}.{testInfo.ClassName}.{testInfo.MethodName}";
        }

        private static List<CategorizationRule> CreateDefaultRules()
        {
            return new List<CategorizationRule>
            {
                // High priority: End-to-End tests
                CategorizationRule.CreateCompositeRule(
                    TestCategory.EndToEnd,
                    priority: 90,
                    description: "End-to-end tests",
                    methodPatterns: new[] { "e2e", "endtoend" },
                    classPatterns: new[] { "e2e", "endtoend" },
                    namespacePatterns: new[] { "e2e", "endtoend" }),

                // High priority: UI tests
                CategorizationRule.CreateCompositeRule(
                    TestCategory.UI,
                    priority: 85,
                    description: "UI automation tests",
                    methodPatterns: new[] { "ui", "selenium", "webdriver", "browser" },
                    classPatterns: new[] { "ui", "selenium", "webdriver", "browser" }),

                // High priority: Database tests
                CategorizationRule.CreateCompositeRule(
                    TestCategory.Database,
                    priority: 80,
                    description: "Database tests",
                    methodPatterns: new[] { "database", "db", "sql", "entity", "ef6", "efcore", "repository" },
                    classPatterns: new[] { "database", "db", "sql", "entity", "ef6", "efcore", "repository" }),

                // High priority: API tests
                CategorizationRule.CreateCompositeRule(
                    TestCategory.API,
                    priority: 75,
                    description: "API tests",
                    methodPatterns: new[] { "api", "http", "rest", "controller", "endpoint" },
                    classPatterns: new[] { "api", "http", "rest", "controller", "endpoint" },
                    namespacePatterns: new[] { "api", "controllers" }),

                // Medium priority: Performance tests
                CategorizationRule.CreateCompositeRule(
                    TestCategory.Performance,
                    priority: 70,
                    description: "Performance tests",
                    methodPatterns: new[] { "performance", "load", "stress", "benchmark" },
                    classPatterns: new[] { "performance", "load", "stress", "benchmark" }),

                // Medium priority: Security tests
                CategorizationRule.CreateCompositeRule(
                    TestCategory.Security,
                    priority: 65,
                    description: "Security tests",
                    methodPatterns: new[] { "security", "auth", "authorization", "authentication", "permission" },
                    classPatterns: new[] { "security", "auth", "authorization", "authentication", "permission" }),

                // Medium priority: Integration tests
                CategorizationRule.CreateCompositeRule(
                    TestCategory.Integration,
                    priority: 60,
                    description: "Integration tests",
                    methodPatterns: new[] { "integration" },
                    classPatterns: new[] { "integration" },
                    namespacePatterns: new[] { "integration" },
                    assemblyPatterns: new[] { "integration" }),

                // Lower priority: Unit test indicators (specific patterns)
                CategorizationRule.CreateClassNameRule(
                    TestCategory.Unit,
                    priority: 50,
                    description: "Direct unit test class patterns",
                    "service", "factory", "analyzer", "discovery", "utility", "helper"),

                // Special case: NUnit test discovery should be Unit tests
                CategorizationRule.CreateClassNameRule(
                    TestCategory.Unit,
                    priority: 55,
                    description: "NUnit test discovery classes",
                    "nunittestdiscovery", "testdiscovery"),

                // Lowest priority: Default unit test pattern for test classes
                new CategorizationRule(
                    TestCategory.Unit,
                    priority: 10,
                    description: "Test classes ending with 'Test' or 'Tests'",
                    context => context.ClassName.ToLowerInvariant().EndsWith("test") ||
                              context.ClassName.ToLowerInvariant().EndsWith("tests"))
            };
        }
    }
}