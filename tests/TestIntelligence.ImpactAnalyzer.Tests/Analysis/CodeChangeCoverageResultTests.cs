using System;
using System.Collections.Generic;
using System.Linq;
using TestIntelligence.Core.Models;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Models;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Analysis
{
    public class CodeChangeCoverageResultTests
    {
        [Fact]
        public void Constructor_WithValidInputs_CalculatesStatisticsCorrectly()
        {
            // Arrange
            var codeChanges = CreateCodeChangeSet();
            var providedTests = CreateProvidedTests();
            var methodCoverage = CreateMethodCoverage();
            var solutionPath = "/test/solution.sln";

            // Act
            var result = new CodeChangeCoverageResult(
                codeChanges, 
                providedTests, 
                methodCoverage, 
                DateTime.UtcNow, 
                solutionPath);

            // Assert
            Assert.Equal(2, result.TotalChangedMethods);
            Assert.Equal(1, result.CoveredChangedMethods);
            Assert.Equal(1, result.UncoveredChangedMethods);
            Assert.Equal(50.0, result.CoveragePercentage);
            Assert.Single(result.UncoveredMethods);
            Assert.Contains("MethodTwo", result.UncoveredMethods);
        }

        [Fact]
        public void Constructor_WithFullCoverage_Returns100Percent()
        {
            // Arrange
            var codeChanges = CreateCodeChangeSet();
            var providedTests = CreateProvidedTests();
            var methodCoverage = new Dictionary<string, IReadOnlyList<TestCoverageInfo>>
            {
                { "MethodOne", CreateProvidedTests() },
                { "MethodTwo", CreateProvidedTests() }
            };
            var solutionPath = "/test/solution.sln";

            // Act
            var result = new CodeChangeCoverageResult(
                codeChanges, 
                providedTests, 
                methodCoverage, 
                DateTime.UtcNow, 
                solutionPath);

            // Assert
            Assert.Equal(100.0, result.CoveragePercentage);
            Assert.Equal(2, result.CoveredChangedMethods);
            Assert.Equal(0, result.UncoveredChangedMethods);
            Assert.Empty(result.UncoveredMethods);
        }

        [Fact]
        public void Constructor_WithNoCoverage_ReturnsZeroPercent()
        {
            // Arrange
            var codeChanges = CreateCodeChangeSet();
            var providedTests = CreateProvidedTests();
            var methodCoverage = new Dictionary<string, IReadOnlyList<TestCoverageInfo>>(); // No coverage
            var solutionPath = "/test/solution.sln";

            // Act
            var result = new CodeChangeCoverageResult(
                codeChanges, 
                providedTests, 
                methodCoverage, 
                DateTime.UtcNow, 
                solutionPath);

            // Assert
            Assert.Equal(0.0, result.CoveragePercentage);
            Assert.Equal(0, result.CoveredChangedMethods);
            Assert.Equal(2, result.UncoveredChangedMethods);
            Assert.Equal(2, result.UncoveredMethods.Count);
        }

        [Fact]
        public void Constructor_WithNoChanges_Returns100Percent()
        {
            // Arrange
            var codeChanges = new CodeChangeSet(new List<CodeChange>()); // No changes
            var providedTests = CreateProvidedTests();
            var methodCoverage = new Dictionary<string, IReadOnlyList<TestCoverageInfo>>();
            var solutionPath = "/test/solution.sln";

            // Act
            var result = new CodeChangeCoverageResult(
                codeChanges, 
                providedTests, 
                methodCoverage, 
                DateTime.UtcNow, 
                solutionPath);

            // Assert
            Assert.Equal(100.0, result.CoveragePercentage);
            Assert.Equal(0, result.TotalChangedMethods);
            Assert.Equal(0, result.CoveredChangedMethods);
            Assert.Equal(0, result.UncoveredChangedMethods);
        }

        [Fact]
        public void ConfidenceBreakdown_CalculatesCorrectly()
        {
            // Arrange
            var codeChanges = CreateCodeChangeSet();
            var highConfidenceTest = new TestCoverageInfo(
                "HighConfidenceTest", "HighTest", "TestClass", "Test.dll", 
                new[] { "Test.Method" }, 0.9, TestType.Unit);
            var mediumConfidenceTest = new TestCoverageInfo(
                "MediumConfidenceTest", "MediumTest", "TestClass", "Test.dll", 
                new[] { "Test.Method" }, 0.6, TestType.Unit);
            var lowConfidenceTest = new TestCoverageInfo(
                "LowConfidenceTest", "LowTest", "TestClass", "Test.dll", 
                new[] { "Test.Method" }, 0.3, TestType.Unit);

            var providedTests = new List<TestCoverageInfo> 
            { 
                highConfidenceTest, mediumConfidenceTest, lowConfidenceTest 
            }.AsReadOnly();

            var methodCoverage = new Dictionary<string, IReadOnlyList<TestCoverageInfo>>
            {
                { "MethodOne", new List<TestCoverageInfo> { highConfidenceTest, mediumConfidenceTest, lowConfidenceTest }.AsReadOnly() }
            };

            // Act
            var result = new CodeChangeCoverageResult(
                codeChanges, 
                providedTests, 
                methodCoverage, 
                DateTime.UtcNow, 
                "/test/solution.sln");

            // Assert
            Assert.Equal(1, result.ConfidenceBreakdown.HighConfidence);
            Assert.Equal(1, result.ConfidenceBreakdown.MediumConfidence);
            Assert.Equal(1, result.ConfidenceBreakdown.LowConfidence);
            Assert.Equal(0.6, result.ConfidenceBreakdown.AverageConfidence, 1); // Rounded to 1 decimal place
        }

        [Fact]
        public void CoverageByTestType_CalculatesCorrectly()
        {
            // Arrange
            var codeChanges = CreateCodeChangeSet();
            var unitTest = new TestCoverageInfo(
                "UnitTest", "UnitTest", "TestClass", "Test.dll", 
                new[] { "Test.Method" }, 0.8, TestType.Unit);
            var integrationTest = new TestCoverageInfo(
                "IntegrationTest", "IntegrationTest", "TestClass", "Test.dll", 
                new[] { "Test.Method" }, 0.7, TestType.Integration);

            var providedTests = new List<TestCoverageInfo> 
            { 
                unitTest, integrationTest 
            }.AsReadOnly();

            var methodCoverage = new Dictionary<string, IReadOnlyList<TestCoverageInfo>>
            {
                { "MethodOne", new List<TestCoverageInfo> { unitTest, integrationTest }.AsReadOnly() }
            };

            // Act
            var result = new CodeChangeCoverageResult(
                codeChanges, 
                providedTests, 
                methodCoverage, 
                DateTime.UtcNow, 
                "/test/solution.sln");

            // Assert
            Assert.Equal(1, result.CoverageByTestType[TestType.Unit]);
            Assert.Equal(1, result.CoverageByTestType[TestType.Integration]);
        }

        [Fact]
        public void Recommendations_GeneratesCorrectly()
        {
            // Arrange
            var codeChanges = CreateCodeChangeSet(); // Has 2 methods: MethodOne, MethodTwo
            var providedTests = CreateProvidedTests();
            var methodCoverage = CreateMethodCoverage(); // Only covers MethodOne
            var solutionPath = "/test/solution.sln";

            // Act
            var result = new CodeChangeCoverageResult(
                codeChanges, 
                providedTests, 
                methodCoverage, 
                DateTime.UtcNow, 
                solutionPath);

            // Assert
            Assert.NotEmpty(result.Recommendations);
            
            // Should have recommendation for missing test coverage
            var missingTestsRecommendation = result.Recommendations
                .FirstOrDefault(r => r.Type == CoverageRecommendationType.MissingTests);
            Assert.NotNull(missingTestsRecommendation);
            Assert.Equal(CoverageRecommendationPriority.High, missingTestsRecommendation.Priority);
            Assert.Contains("MethodTwo", missingTestsRecommendation.AffectedItems);
        }

        [Fact]
        public void Constructor_WithNullCodeChanges_ThrowsArgumentNullException()
        {
            // Arrange
            var providedTests = CreateProvidedTests();
            var methodCoverage = CreateMethodCoverage();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new CodeChangeCoverageResult(
                    null!, 
                    providedTests, 
                    methodCoverage, 
                    DateTime.UtcNow, 
                    "/test/solution.sln"));
        }

        [Fact]
        public void ToString_ReturnsFormattedString()
        {
            // Arrange
            var codeChanges = CreateCodeChangeSet();
            var providedTests = CreateProvidedTests();
            var methodCoverage = CreateMethodCoverage();

            var result = new CodeChangeCoverageResult(
                codeChanges, 
                providedTests, 
                methodCoverage, 
                DateTime.UtcNow, 
                "/test/solution.sln");

            // Act
            var toString = result.ToString();

            // Assert
            Assert.Contains("50.0%", toString);
            Assert.Contains("1/2", toString);
        }

        private static CodeChangeSet CreateCodeChangeSet()
        {
            var changes = new List<CodeChange>
            {
                new CodeChange(
                    "MyClass.cs", 
                    CodeChangeType.Modified,
                    new[] { "MethodOne", "MethodTwo" },
                    new[] { "MyClass" })
            };
            return new CodeChangeSet(changes);
        }

        private static IReadOnlyList<TestCoverageInfo> CreateProvidedTests()
        {
            var testCoverageInfo = new TestCoverageInfo(
                "MyTestClass.TestMethod",
                "TestMethod",
                "MyTestClass",
                "MyTestAssembly.dll",
                new[] { "MyTestClass.TestMethod", "MyClass.MethodOne" },
                0.8,
                TestType.Unit);

            return new List<TestCoverageInfo> { testCoverageInfo }.AsReadOnly();
        }

        private static Dictionary<string, IReadOnlyList<TestCoverageInfo>> CreateMethodCoverage()
        {
            var testCoverageInfo = new TestCoverageInfo(
                "MyTestClass.TestMethod",
                "TestMethod",
                "MyTestClass",
                "MyTestAssembly.dll",
                new[] { "MyTestClass.TestMethod", "MyClass.MethodOne" },
                0.8,
                TestType.Unit);

            return new Dictionary<string, IReadOnlyList<TestCoverageInfo>>
            {
                { "MethodOne", new List<TestCoverageInfo> { testCoverageInfo }.AsReadOnly() }
                // MethodTwo is not covered - this creates 50% coverage
            };
        }
    }

    public class CoverageByConfidenceTests
    {
        [Fact]
        public void Constructor_WithEmptyScores_InitializesCorrectly()
        {
            // Act
            var breakdown = new CoverageByConfidence();

            // Assert
            Assert.Equal(0, breakdown.HighConfidence);
            Assert.Equal(0, breakdown.MediumConfidence);
            Assert.Equal(0, breakdown.LowConfidence);
            Assert.Equal(0.0, breakdown.AverageConfidence);
        }

        [Fact]
        public void Constructor_WithMixedConfidenceScores_CalculatesCorrectly()
        {
            // Arrange
            var scores = new List<double> { 0.9, 0.6, 0.3, 0.8, 0.4 }.AsReadOnly();

            // Act
            var breakdown = new CoverageByConfidence(scores);

            // Assert
            Assert.Equal(2, breakdown.HighConfidence); // 0.9, 0.8
            Assert.Equal(1, breakdown.MediumConfidence); // 0.6
            Assert.Equal(2, breakdown.LowConfidence); // 0.3, 0.4
            Assert.Equal(0.6, breakdown.AverageConfidence, 1);
        }

        [Fact]
        public void Constructor_WithBoundaryValues_CalculatesCorrectly()
        {
            // Arrange
            var scores = new List<double> { 0.8, 0.5, 0.49 }.AsReadOnly();

            // Act
            var breakdown = new CoverageByConfidence(scores);

            // Assert
            Assert.Equal(1, breakdown.HighConfidence); // 0.8 (>= 0.8)
            Assert.Equal(1, breakdown.MediumConfidence); // 0.5 (>= 0.5 && < 0.8)
            Assert.Equal(1, breakdown.LowConfidence); // 0.49 (< 0.5)
        }
    }
}