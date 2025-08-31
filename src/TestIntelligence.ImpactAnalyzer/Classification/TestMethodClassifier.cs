using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TestIntelligence.Core.Models;
using TestIntelligence.ImpactAnalyzer.Analysis;

namespace TestIntelligence.ImpactAnalyzer.Classification
{
    /// <summary>
    /// Classifies methods as test methods based on various heuristics and attributes.
    /// </summary>
    public class TestMethodClassifier
    {
        private static readonly HashSet<string> TestAttributeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TestAttribute",
            "TestCaseAttribute",
            "NUnit.Framework.TestAttribute",
            "NUnit.Framework.TestCaseAttribute",
            "FactAttribute",
            "TheoryAttribute",
            "Xunit.FactAttribute", 
            "Xunit.TheoryAttribute",
            "TestMethodAttribute",
            "DataTestMethodAttribute",
            "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute",
            "Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute"
        };

        private static readonly HashSet<string> TestFixtureAttributeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TestFixtureAttribute",
            "NUnit.Framework.TestFixtureAttribute",
            "TestClassAttribute",
            "Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute"
        };

        private static readonly HashSet<string> TestProjectPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "test",
            "tests", 
            "unittest",
            "unittests",
            "integrationtest",
            "integrationtests",
            "spec",
            "specs"
        };

        private static readonly Regex TestMethodNamePattern = new Regex(
            @"(test|spec|should|when|given|scenario|example|verify|check|ensure|benchmark|calculate).*|(.*)(test|tests|spec|specs)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Determines if a method is a test method using the accurate analysis from RoslynAnalyzer.
        /// Falls back to heuristics only when the IsTestMethod property is not available.
        /// </summary>
        public bool IsTestMethod(MethodInfo methodInfo)
        {
            if (methodInfo == null)
                throw new ArgumentNullException(nameof(methodInfo));

            // Primary check: Trust the Roslyn analyzer's accurate attribute parsing
            // This property is set by analyzing actual [Test], [Fact], [Theory], [TestMethod] attributes
            if (methodInfo.IsTestMethod)
                return true;

            // Conservative fallback for cases where attribute parsing might have failed
            // Only apply to methods in explicitly identified test projects
            if (IsInTestProject(methodInfo.FilePath))
            {
                // Use comprehensive regex pattern for test method naming conventions
                return TestMethodNamePattern.IsMatch(methodInfo.Name);
            }

            // No fallback for production code - if it doesn't have attributes, it's not a test
            return false;
        }

        /// <summary>
        /// Determines the type of test method.
        /// </summary>
        public TestType ClassifyTestType(MethodInfo methodInfo)
        {
            if (methodInfo == null)
                throw new ArgumentNullException(nameof(methodInfo));

            if (!IsTestMethod(methodInfo))
                return TestType.Unknown;

            var filePath = methodInfo.FilePath.ToLowerInvariant();
            var methodName = methodInfo.Name.ToLowerInvariant();
            var typeName = methodInfo.ContainingType.ToLowerInvariant();

            // Check for end-to-end test indicators first (more specific)
            if (filePath.Contains("e2e") || 
                filePath.Contains("endtoend") ||
                methodName.Contains("e2e") ||
                methodName.Contains("endtoend") ||
                methodName.Contains("scenario") ||
                methodName.Contains("journey"))
            {
                return TestType.End2End;
            }

            // Check for integration test indicators
            if (filePath.Contains("integration") || 
                methodName.Contains("integration") || 
                typeName.Contains("integration"))
            {
                return TestType.Integration;
            }

            // Check for performance test indicators
            if (filePath.Contains("performance") ||
                filePath.Contains("benchmark") ||
                methodName.Contains("performance") ||
                methodName.Contains("benchmark") ||
                typeName.Contains("performance") ||
                typeName.Contains("benchmark"))
            {
                return TestType.Performance;
            }

            // Check for security test indicators  
            if (filePath.Contains("security") ||
                methodName.Contains("security") ||
                methodName.Contains("auth") ||
                methodName.Contains("permission") ||
                typeName.Contains("security"))
            {
                return TestType.Security;
            }

            // Default to unit test
            return TestType.Unit;
        }

        /// <summary>
        /// Calculates confidence score for test method classification.
        /// </summary>
        public double CalculateTestConfidence(MethodInfo methodInfo)
        {
            if (methodInfo == null)
                throw new ArgumentNullException(nameof(methodInfo));

            double confidence = 0.0;

            // Strong indicators (high confidence)
            if (HasTestAttributes(methodInfo))
                confidence += 0.8;

            if (IsInTestProject(methodInfo.FilePath))
                confidence += 0.3;

            if (IsTestFile(methodInfo.FilePath))
                confidence += 0.2;

            // Moderate indicators
            if (HasTestMethodName(methodInfo.Name))
                confidence += 0.4;

            if (IsInTestClass(methodInfo.ContainingType))
                confidence += 0.2;

            return Math.Min(1.0, confidence);
        }

        private bool HasTestAttributes(MethodInfo methodInfo)
        {
            // Since we don't have direct access to method attributes in our current implementation,
            // we'll use heuristics based on the method's context and naming patterns
            
            // Strong indicators that this is likely a test method with attributes
            var fileName = Path.GetFileNameWithoutExtension(methodInfo.FilePath).ToLowerInvariant();
            var methodName = methodInfo.Name.ToLowerInvariant();
            var typeName = methodInfo.ContainingType.ToLowerInvariant();
            
            // If it's in a test file and has test-like naming, likely has test attributes
            if (IsInTestProject(methodInfo.FilePath) || IsTestFile(methodInfo.FilePath))
            {
                // In test projects, methods with conventional test names likely have attributes
                if (HasTestMethodName(methodInfo.Name))
                    return true;
                    
                // Methods in test classes are likely test methods if they're public
                if (IsInTestClass(methodInfo.ContainingType))
                    return true;
            }
            
            // Conservative approach for non-test projects
            return false;
        }

        private bool HasTestMethodName(string methodName)
        {
            return TestMethodNamePattern.IsMatch(methodName);
        }

        private bool IsInTestProject(string filePath)
        {
            var pathParts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return pathParts.Any(part => TestProjectPatterns.Any(pattern => 
                part.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private bool IsTestFile(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
            return TestProjectPatterns.Any(pattern => fileName.Contains(pattern));
        }

        private bool IsInTestClass(string className)
        {
            var lowerClassName = className.ToLowerInvariant();
            return TestProjectPatterns.Any(pattern => lowerClassName.Contains(pattern));
        }

        /// <summary>
        /// Gets all methods from a method collection that are classified as test methods.
        /// </summary>
        public IReadOnlyList<MethodInfo> GetTestMethods(IEnumerable<MethodInfo> methods)
        {
            if (methods == null)
                throw new ArgumentNullException(nameof(methods));

            return methods.Where(IsTestMethod).ToList().AsReadOnly();
        }

        /// <summary>
        /// Groups test methods by their test type classification.
        /// </summary>
        public IReadOnlyDictionary<TestType, IReadOnlyList<MethodInfo>> GroupTestMethodsByType(IEnumerable<MethodInfo> testMethods)
        {
            if (testMethods == null)
                throw new ArgumentNullException(nameof(testMethods));

            return testMethods
                .Where(IsTestMethod)
                .GroupBy(ClassifyTestType)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<MethodInfo>)g.ToList().AsReadOnly()
                );
        }
    }
}