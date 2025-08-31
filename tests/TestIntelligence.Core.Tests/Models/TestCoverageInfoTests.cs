using System;
using System.Collections.Generic;
using TestIntelligence.Core.Models;
using TestIntelligence.Core.Services;
using Xunit;

namespace TestIntelligence.Core.Tests.Models
{
    public class TestCoverageInfoTests
    {
        [Fact]
        public void Constructor_WithValidParameters_CreatesInstance()
        {
            // Arrange
            var testMethodId = "MyTests.TestMethod()";
            var testMethodName = "TestMethod";
            var testClassName = "MyTests";
            var testAssembly = "MyTests.dll";
            var callPath = new[] { "MyTests.TestMethod()", "BusinessLogic.DoWork()" };
            var confidence = 0.85;
            var testType = TestType.Unit;

            // Act
            var coverageInfo = new TestCoverageInfo(
                testMethodId, testMethodName, testClassName,
                testAssembly, callPath, confidence, testType);

            // Assert
            Assert.Equal(testMethodId, coverageInfo.TestMethodId);
            Assert.Equal(testMethodName, coverageInfo.TestMethodName);
            Assert.Equal(testClassName, coverageInfo.TestClassName);
            Assert.Equal(testAssembly, coverageInfo.TestAssembly);
            Assert.Equal(callPath, coverageInfo.CallPath);
            Assert.Equal(confidence, coverageInfo.Confidence);
            Assert.Equal(testType, coverageInfo.TestType);
        }

        [Theory]
        [InlineData(-0.5, 0.0)]
        [InlineData(1.5, 1.0)]
        [InlineData(0.5, 0.5)]
        public void Constructor_WithInvalidConfidence_ClampsToValidRange(double inputConfidence, double expectedConfidence)
        {
            // Arrange & Act
            var coverageInfo = new TestCoverageInfo(
                "Test.Method()", "Method", "Test", "Test.dll",
                new[] { "Test.Method()", "Target()" }, inputConfidence, TestType.Unit);

            // Assert
            Assert.Equal(expectedConfidence, coverageInfo.Confidence);
        }

        [Fact]
        public void Constructor_WithNullTestMethodId_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new TestCoverageInfo(
                null!, "Method", "Test", "Test.dll",
                new[] { "Test.Method()" }, 0.8, TestType.Unit));
        }

        [Fact]
        public void Constructor_WithNullCallPath_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new TestCoverageInfo(
                "Test.Method()", "Method", "Test", "Test.dll",
                null!, 0.8, TestType.Unit));
        }

        [Theory]
        [InlineData(new[] { "Test()", "Target()" }, 1)]
        [InlineData(new[] { "Test()", "Helper()", "Target()" }, 2)]
        [InlineData(new[] { "Test()", "A()", "B()", "C()", "Target()" }, 4)]
        public void CallDepth_WithVariousCallPaths_ReturnsCorrectDepth(string[] callPath, int expectedDepth)
        {
            // Arrange
            var coverageInfo = new TestCoverageInfo(
                "Test.Method()", "Method", "Test", "Test.dll",
                callPath, 0.8, TestType.Unit);

            // Act & Assert
            Assert.Equal(expectedDepth, coverageInfo.CallDepth);
        }

        [Theory]
        [InlineData(new[] { "Test()", "Target()" }, true)]
        [InlineData(new[] { "Test()", "Helper()", "Target()" }, false)]
        public void IsDirectCoverage_WithVariousCallPaths_ReturnsCorrectValue(string[] callPath, bool expectedIsDirect)
        {
            // Arrange
            var coverageInfo = new TestCoverageInfo(
                "Test.Method()", "Method", "Test", "Test.dll",
                callPath, 0.8, TestType.Unit);

            // Act & Assert
            Assert.Equal(expectedIsDirect, coverageInfo.IsDirectCoverage);
        }

        [Fact]
        public void GetCallPathDisplay_WithMultipleSteps_ReturnsFormattedPath()
        {
            // Arrange
            var callPath = new[] { "TestMethod()", "HelperMethod()", "TargetMethod()" };
            var coverageInfo = new TestCoverageInfo(
                "Test.TestMethod()", "TestMethod", "Test", "Test.dll",
                callPath, 0.8, TestType.Unit);

            // Act
            var display = coverageInfo.GetCallPathDisplay();

            // Assert
            Assert.Equal("TestMethod() -> HelperMethod() -> TargetMethod()", display);
        }

        [Fact]
        public void ToString_ReturnsExpectedFormat()
        {
            // Arrange
            var coverageInfo = new TestCoverageInfo(
                "Test.TestMethod()", "TestMethod", "Test", "Test.dll",
                new[] { "TestMethod()", "TargetMethod()" }, 0.85, TestType.Unit);

            // Act
            var result = coverageInfo.ToString();

            // Assert
            Assert.Equal("TestMethod (Unit, Confidence: 0.85, Depth: 1)", result);
        }

        [Fact]
        public void Equals_WithSameMethodIdAndCallPath_ReturnsTrue()
        {
            // Arrange
            var callPath = new[] { "Test()", "Target()" };
            var coverage1 = new TestCoverageInfo(
                "Test.Method()", "Method", "Test", "Test.dll",
                callPath, 0.8, TestType.Unit);
            var coverage2 = new TestCoverageInfo(
                "Test.Method()", "Method", "Test", "Test.dll",
                callPath, 0.9, TestType.Integration); // Different confidence and type

            // Act & Assert
            Assert.Equal(coverage1, coverage2);
        }

        [Fact]
        public void Equals_WithDifferentMethodId_ReturnsFalse()
        {
            // Arrange
            var callPath = new[] { "Test()", "Target()" };
            var coverage1 = new TestCoverageInfo(
                "Test.Method1()", "Method1", "Test", "Test.dll",
                callPath, 0.8, TestType.Unit);
            var coverage2 = new TestCoverageInfo(
                "Test.Method2()", "Method2", "Test", "Test.dll",
                callPath, 0.8, TestType.Unit);

            // Act & Assert
            Assert.NotEqual(coverage1, coverage2);
        }

        [Fact]
        public void GetHashCode_WithSameMethodIdAndCallPath_ReturnsSameValue()
        {
            // Arrange
            var callPath = new[] { "Test()", "Target()" };
            var coverage1 = new TestCoverageInfo(
                "Test.Method()", "Method", "Test", "Test.dll",
                callPath, 0.8, TestType.Unit);
            var coverage2 = new TestCoverageInfo(
                "Test.Method()", "Method", "Test", "Test.dll",
                callPath, 0.9, TestType.Integration);

            // Act & Assert
            Assert.Equal(coverage1.GetHashCode(), coverage2.GetHashCode());
        }
    }

    public class TestCoverageMapTests
    {
        [Fact]
        public void Constructor_WithValidParameters_CreatesInstance()
        {
            // Arrange
            var methodToTests = new Dictionary<string, List<TestCoverageInfo>>();
            var buildTimestamp = DateTime.UtcNow;
            var solutionPath = "/path/to/solution.sln";

            // Act
            var map = new TestCoverageMap(methodToTests, buildTimestamp, solutionPath);

            // Assert
            Assert.Equal(methodToTests, map.MethodToTests);
            Assert.Equal(buildTimestamp, map.BuildTimestamp);
            Assert.Equal(solutionPath, map.SolutionPath);
        }

        [Fact]
        public void Constructor_WithNullMethodToTests_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new TestCoverageMap(
                null!, DateTime.UtcNow, "/path/to/solution.sln"));
        }

        [Fact]
        public void Constructor_WithNullSolutionPath_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new TestCoverageMap(
                new Dictionary<string, List<TestCoverageInfo>>(), DateTime.UtcNow, null!));
        }

        [Fact]
        public void GetTestsForMethod_WithExistingMethod_ReturnsTests()
        {
            // Arrange
            var testCoverage = new TestCoverageInfo(
                "Test.Method()", "Method", "Test", "Test.dll",
                new[] { "Test.Method()", "Target()" }, 0.8, TestType.Unit);
            
            var methodToTests = new Dictionary<string, List<TestCoverageInfo>>
            {
                { "Target()", new List<TestCoverageInfo> { testCoverage } }
            };

            var map = new TestCoverageMap(methodToTests, DateTime.UtcNow, "/solution.sln");

            // Act
            var result = map.GetTestsForMethod("Target()");

            // Assert
            Assert.Single(result);
            Assert.Equal(testCoverage, result[0]);
        }

        [Fact]
        public void GetTestsForMethod_WithNonExistentMethod_ReturnsEmptyList()
        {
            // Arrange
            var map = new TestCoverageMap(
                new Dictionary<string, List<TestCoverageInfo>>(),
                DateTime.UtcNow, "/solution.sln");

            // Act
            var result = map.GetTestsForMethod("NonExistent()");

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void GetCoveredMethods_WithMultipleMethods_ReturnsAllMethodIds()
        {
            // Arrange
            var methodToTests = new Dictionary<string, List<TestCoverageInfo>>
            {
                { "Method1()", new List<TestCoverageInfo>() },
                { "Method2()", new List<TestCoverageInfo>() }
            };

            var map = new TestCoverageMap(methodToTests, DateTime.UtcNow, "/solution.sln");

            // Act
            var result = map.GetCoveredMethods();

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Contains("Method1()", result);
            Assert.Contains("Method2()", result);
        }

        [Fact]
        public void CoveredMethodCount_ReturnsCorrectCount()
        {
            // Arrange
            var methodToTests = new Dictionary<string, List<TestCoverageInfo>>
            {
                { "Method1()", new List<TestCoverageInfo>() },
                { "Method2()", new List<TestCoverageInfo>() },
                { "Method3()", new List<TestCoverageInfo>() }
            };

            var map = new TestCoverageMap(methodToTests, DateTime.UtcNow, "/solution.sln");

            // Act & Assert
            Assert.Equal(3, map.CoveredMethodCount);
        }

        [Fact]
        public void TotalCoverageRelationships_ReturnsCorrectCount()
        {
            // Arrange
            var coverage1 = new TestCoverageInfo(
                "Test1.Method()", "Method", "Test1", "Test1.dll",
                new[] { "Test1.Method()", "Target1()" }, 0.8, TestType.Unit);
            var coverage2 = new TestCoverageInfo(
                "Test2.Method()", "Method", "Test2", "Test2.dll",
                new[] { "Test2.Method()", "Target2()" }, 0.8, TestType.Unit);
            
            var methodToTests = new Dictionary<string, List<TestCoverageInfo>>
            {
                { "Target1()", new List<TestCoverageInfo> { coverage1, coverage2 } },
                { "Target2()", new List<TestCoverageInfo> { coverage1 } }
            };

            var map = new TestCoverageMap(methodToTests, DateTime.UtcNow, "/solution.sln");

            // Act & Assert
            Assert.Equal(3, map.TotalCoverageRelationships); // 2 for Target1 + 1 for Target2
        }
    }

    public class TestCoverageStatisticsTests
    {
        [Fact]
        public void Constructor_WithValidParameters_CreatesInstance()
        {
            // Arrange
            var coverageByTestType = new Dictionary<TestType, int>
            {
                { TestType.Unit, 10 },
                { TestType.Integration, 5 }
            };

            // Act
            var stats = new TestCoverageStatistics(
                totalMethods: 100,
                coveredMethods: 75,
                totalTests: 50,
                totalCoverageRelationships: 120,
                coverageByTestType: coverageByTestType);

            // Assert
            Assert.Equal(100, stats.TotalMethods);
            Assert.Equal(75, stats.CoveredMethods);
            Assert.Equal(50, stats.TotalTests);
            Assert.Equal(120, stats.TotalCoverageRelationships);
            Assert.Equal(coverageByTestType, stats.CoverageByTestType);
        }

        [Theory]
        [InlineData(100, 75, 75.0)]
        [InlineData(50, 25, 50.0)]
        [InlineData(0, 0, 0.0)]
        [InlineData(10, 10, 100.0)]
        public void CoveragePercentage_WithVariousValues_ReturnsCorrectPercentage(
            int totalMethods, int coveredMethods, double expectedPercentage)
        {
            // Arrange
            var stats = new TestCoverageStatistics(
                totalMethods, coveredMethods, 10, 20, 
                new Dictionary<TestType, int>());

            // Act & Assert
            Assert.Equal(expectedPercentage, stats.CoveragePercentage);
        }

        [Theory]
        [InlineData(100, 75, 25)]
        [InlineData(50, 30, 20)]
        [InlineData(10, 10, 0)]
        public void UncoveredMethods_WithVariousValues_ReturnsCorrectCount(
            int totalMethods, int coveredMethods, int expectedUncovered)
        {
            // Arrange
            var stats = new TestCoverageStatistics(
                totalMethods, coveredMethods, 10, 20,
                new Dictionary<TestType, int>());

            // Act & Assert
            Assert.Equal(expectedUncovered, stats.UncoveredMethods);
        }

        [Fact]
        public void Constructor_WithNullCoverageByTestType_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new TestCoverageStatistics(
                100, 75, 50, 120, null!));
        }
    }
}