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
        private readonly IAssemblyPathResolver _mockAssemblyPathResolver;
        private readonly ILogger<TestCoverageAnalyzer> _mockLogger;
        private readonly TestCoverageAnalyzer _analyzer;

        public TestCoverageAnalyzerTests()
        {
            _mockRoslynAnalyzer = Substitute.For<IRoslynAnalyzer>();
            _mockAssemblyPathResolver = Substitute.For<IAssemblyPathResolver>();
            _mockLogger = Substitute.For<ILogger<TestCoverageAnalyzer>>();
            _analyzer = new TestCoverageAnalyzer(_mockRoslynAnalyzer, _mockAssemblyPathResolver, _mockLogger);
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
                new TestCoverageAnalyzer(null!, _mockAssemblyPathResolver, _mockLogger));
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new TestCoverageAnalyzer(_mockRoslynAnalyzer, _mockAssemblyPathResolver, null!));
        }

        private MethodCallGraph CreateMockCallGraph()
        {
            // Create test methods
            var testMethods = new[]
            {
                new MethodInfo(
                    "CalculatorTests.TestAddition()",
                    "TestAddition",
                    "CalculatorTests",
                    "/tests/CalculatorTests.cs",
                    10,
                    true), // Mark as test method
                new MethodInfo(
                    "CalculatorTests.TestSubtraction()",
                    "TestSubtraction", 
                    "CalculatorTests",
                    "/tests/CalculatorTests.cs",
                    20,
                    true) // Mark as test method
            };

            // Create production methods
            var productionMethods = new[]
            {
                new MethodInfo(
                    "BusinessLogic.Calculator.Add(int,int)",
                    "Add",
                    "BusinessLogic.Calculator",
                    "/src/Calculator.cs",
                    15,
                    false), // Not a test method
                new MethodInfo(
                    "BusinessLogic.Calculator.Subtract(int,int)",
                    "Subtract",
                    "BusinessLogic.Calculator", 
                    "/src/Calculator.cs",
                    25,
                    false) // Not a test method
            };

            // Create call graph dictionary - who calls whom
            var callGraphData = new Dictionary<string, HashSet<string>>
            {
                // TestAddition calls Add
                ["CalculatorTests.TestAddition()"] = new HashSet<string> { "BusinessLogic.Calculator.Add(int,int)" },
                // TestSubtraction calls Subtract  
                ["CalculatorTests.TestSubtraction()"] = new HashSet<string> { "BusinessLogic.Calculator.Subtract(int,int)" },
                // Production methods don't call anything in this simple example
                ["BusinessLogic.Calculator.Add(int,int)"] = new HashSet<string>(),
                ["BusinessLogic.Calculator.Subtract(int,int)"] = new HashSet<string>()
            };

            // Create method definitions dictionary
            var methodDefinitions = new Dictionary<string, MethodInfo>();
            foreach (var method in testMethods.Concat(productionMethods))
            {
                methodDefinitions[method.Id] = method;
            }

            return new MethodCallGraph(callGraphData, methodDefinitions);
        }

        [Fact]
        public async Task FindTestsExercisingMethodAsync_FindsTargetBeyondFiveNeighbors()
        {
            // Arrange: test calls many neighbors; only the last neighbor leads to target
            var testId = "CalculatorTests.TestComplex()";
            var targetId = "BusinessLogic.Calculator.Add(int,int)";

            var neighbors = Enumerable.Range(1, 10)
                .Select(i => $"BusinessLogic.Helpers.Helper{i}.Do()")
                .ToList();

            var methodDefinitions = new Dictionary<string, MethodInfo>
            {
                [testId] = new MethodInfo(testId, "TestComplex", "CalculatorTests", "/tests/CalculatorTests.cs", 30, true),
                [targetId] = new MethodInfo(targetId, "Add", "BusinessLogic.Calculator", "/src/Calculator.cs", 15, false)
            };

            foreach (var n in neighbors)
            {
                methodDefinitions[n] = new MethodInfo(n, "Do", n.Split('.').Reverse().Skip(1).First(), "/src/Helper.cs", 1, false);
            }

            var graph = new Dictionary<string, HashSet<string>>
            {
                [testId] = new HashSet<string>(neighbors)
            };

            // Only the last neighbor calls the target
            var last = neighbors.Last();
            graph[last] = new HashSet<string> { targetId };

            // Ensure other neighbors don't lead anywhere
            foreach (var n in neighbors.Take(neighbors.Count - 1))
            {
                graph[n] = new HashSet<string>();
            }

            // Target has no outgoing edges
            graph[targetId] = new HashSet<string>();

            var callGraph = new MethodCallGraph(graph, methodDefinitions);

            _mockRoslynAnalyzer
                .BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(callGraph);

            var result = await _analyzer.FindTestsExercisingMethodAsync(targetId, "/path/to/solution.sln");

            Assert.Contains(result, r => r.TestMethodId == testId);
        }

        [Fact]
        public async Task FindTestsExercisingMethodAsync_FindsTargetWithLongerDepth()
        {
            // Arrange: build a chain longer than the historical default depth (5)
            var testId = "CalculatorTests.TestDeep()";
            var targetId = "BusinessLogic.Calculator.Compute(int,int)";

            var chain = new[] { "A.M1()", "A.M2()", "A.M3()", "A.M4()", "A.M5()", "A.M6()" };

            var methods = new Dictionary<string, MethodInfo>
            {
                [testId] = new MethodInfo(testId, "TestDeep", "CalculatorTests", "/tests/CalculatorTests.cs", 40, true),
                [targetId] = new MethodInfo(targetId, "Compute", "BusinessLogic.Calculator", "/src/Calculator.cs", 50, false)
            };

            foreach (var m in chain)
            {
                methods[m] = new MethodInfo(m, m.Split('.').Last().Trim('(',')'), m.Split('.').First(), "/src/Chain.cs", 1, false);
            }

            var g = new Dictionary<string, HashSet<string>>();
            g[testId] = new HashSet<string> { chain[0] };
            for (int i = 0; i < chain.Length - 1; i++)
            {
                g[chain[i]] = new HashSet<string> { chain[i + 1] };
            }
            g[chain[^1]] = new HashSet<string> { targetId };
            g[targetId] = new HashSet<string>();

            var callGraph = new MethodCallGraph(g, methods);

            _mockRoslynAnalyzer
                .BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(callGraph);

            var result = await _analyzer.FindTestsExercisingMethodAsync(targetId, "/path/to/solution.sln");
            Assert.Contains(result, r => r.TestMethodId == testId);
        }
    }
}
