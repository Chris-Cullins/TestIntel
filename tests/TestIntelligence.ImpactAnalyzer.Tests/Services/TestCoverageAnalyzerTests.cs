using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.Core.Models;
using TestIntelligence.Core.Services;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Services;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Services
{
    public class TestCoverageAnalyzerTests
    {
        private readonly IRoslynAnalyzer _mockRoslynAnalyzer;
        private readonly ILogger<TestCoverageAnalyzer> _mockLogger;
        private readonly TestCoverageAnalyzer _analyzer;

        public TestCoverageAnalyzerTests()
        {
            _mockRoslynAnalyzer = Substitute.For<IRoslynAnalyzer>();
            _mockLogger = Substitute.For<ILogger<TestCoverageAnalyzer>>();
            _analyzer = new TestCoverageAnalyzer(_mockRoslynAnalyzer, _mockLogger);
        }

        [Fact]
        public async Task FindTestsExercisingMethodAsync_WithValidMethodId_ReturnsTestCoverageInfo()
        {
            // Arrange
            var methodId = "BusinessLogic.Calculator.Add(int,int)";
            var solutionPath = "/path/to/solution.sln";

            var mockCallGraph = CreateMockCallGraph();
            _mockRoslynAnalyzer
                .BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(mockCallGraph);

            // Act
            var result = await _analyzer.FindTestsExercisingMethodAsync(methodId, solutionPath);

            // Assert
            Assert.NotNull(result);
            var coverageInfo = result.FirstOrDefault(c => c.TestMethodName == "TestAddition");
            Assert.NotNull(coverageInfo);
            Assert.Equal("CalculatorTests.TestAddition()", coverageInfo.TestMethodId);
            Assert.Equal(TestType.Unit, coverageInfo.TestType);
        }

        [Fact]
        public async Task FindTestsExercisingMethodAsync_WithNullMethodId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _analyzer.FindTestsExercisingMethodAsync(null!, "/path/to/solution.sln"));
        }

        [Fact]
        public async Task FindTestsExercisingMethodAsync_WithEmptyMethodId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _analyzer.FindTestsExercisingMethodAsync("", "/path/to/solution.sln"));
        }

        [Fact]
        public async Task FindTestsExercisingMethodAsync_WithNullSolutionPath_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _analyzer.FindTestsExercisingMethodAsync("SomeMethod", null!));
        }

        [Fact]
        public async Task BuildTestCoverageMapAsync_WithValidSolution_ReturnsTestCoverageMap()
        {
            // Arrange
            var solutionPath = "/path/to/solution.sln";
            var mockCallGraph = CreateMockCallGraph();
            
            _mockRoslynAnalyzer
                .BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(mockCallGraph);

            // Act
            var result = await _analyzer.BuildTestCoverageMapAsync(solutionPath);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(solutionPath, result.SolutionPath);
            Assert.True(result.CoveredMethodCount > 0);
            
            var testsForAddMethod = result.GetTestsForMethod("BusinessLogic.Calculator.Add(int,int)");
            Assert.NotEmpty(testsForAddMethod);
        }

        [Fact]
        public async Task FindTestsExercisingMethodsAsync_WithMultipleMethodIds_ReturnsCorrectMapping()
        {
            // Arrange
            var methodIds = new[]
            {
                "BusinessLogic.Calculator.Add(int,int)",
                "BusinessLogic.Calculator.Subtract(int,int)"
            };
            var solutionPath = "/path/to/solution.sln";

            var mockCallGraph = CreateMockCallGraph();
            _mockRoslynAnalyzer
                .BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(mockCallGraph);

            // Act
            var result = await _analyzer.FindTestsExercisingMethodsAsync(methodIds, solutionPath);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.True(result.ContainsKey("BusinessLogic.Calculator.Add(int,int)"));
            Assert.True(result.ContainsKey("BusinessLogic.Calculator.Subtract(int,int)"));
        }

        [Fact]
        public async Task FindTestsExercisingMethodsAsync_WithEmptyMethodIds_ReturnsEmptyDictionary()
        {
            // Act
            var result = await _analyzer.FindTestsExercisingMethodsAsync(
                new string[0], "/path/to/solution.sln");

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetCoverageStatisticsAsync_WithValidSolution_ReturnsStatistics()
        {
            // Arrange
            var solutionPath = "/path/to/solution.sln";
            var mockCallGraph = CreateMockCallGraph();
            
            _mockRoslynAnalyzer
                .BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(mockCallGraph);

            // Act
            var result = await _analyzer.GetCoverageStatisticsAsync(solutionPath);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.TotalMethods > 0);
            Assert.True(result.TotalTests > 0);
            Assert.True(result.CoveredMethods > 0);
            Assert.True(result.CoveragePercentage >= 0.0 && result.CoveragePercentage <= 100.0);
            Assert.NotNull(result.CoverageByTestType);
        }

        [Fact]
        public void Constructor_WithNullRoslynAnalyzer_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new TestCoverageAnalyzer(null!, _mockLogger));
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new TestCoverageAnalyzer(_mockRoslynAnalyzer, null!));
        }

        private MethodCallGraph CreateMockCallGraph()
        {
            var mockCallGraph = Substitute.For<MethodCallGraph>(
                new Dictionary<string, HashSet<string>>(),
                new Dictionary<string, MethodInfo>());

            // Create test methods
            var testMethods = new[]
            {
                new MethodInfo(
                    "CalculatorTests.TestAddition()",
                    "TestAddition",
                    "CalculatorTests",
                    "/tests/CalculatorTests.cs",
                    10),
                new MethodInfo(
                    "CalculatorTests.TestSubtraction()",
                    "TestSubtraction", 
                    "CalculatorTests",
                    "/tests/CalculatorTests.cs",
                    20)
            };

            // Create production methods
            var productionMethods = new[]
            {
                new MethodInfo(
                    "BusinessLogic.Calculator.Add(int,int)",
                    "Add",
                    "BusinessLogic.Calculator",
                    "/src/Calculator.cs",
                    15),
                new MethodInfo(
                    "BusinessLogic.Calculator.Subtract(int,int)",
                    "Subtract",
                    "BusinessLogic.Calculator", 
                    "/src/Calculator.cs",
                    25)
            };

            var allMethods = testMethods.Concat(productionMethods).Select(m => m.Id).ToList();

            mockCallGraph.GetAllMethods().Returns(allMethods);

            // Setup method info retrieval
            foreach (var method in testMethods.Concat(productionMethods))
            {
                mockCallGraph.GetMethodInfo(method.Id).Returns(method);
            }

            // Setup call relationships: TestAddition -> Add, TestSubtraction -> Subtract
            mockCallGraph.GetMethodCalls("CalculatorTests.TestAddition()")
                .Returns(new HashSet<string> { "BusinessLogic.Calculator.Add(int,int)" });

            mockCallGraph.GetMethodCalls("CalculatorTests.TestSubtraction()")
                .Returns(new HashSet<string> { "BusinessLogic.Calculator.Subtract(int,int)" });

            // Setup empty calls for production methods
            mockCallGraph.GetMethodCalls("BusinessLogic.Calculator.Add(int,int)")
                .Returns(new HashSet<string>());

            mockCallGraph.GetMethodCalls("BusinessLogic.Calculator.Subtract(int,int)")
                .Returns(new HashSet<string>());

            return mockCallGraph;
        }
    }
}