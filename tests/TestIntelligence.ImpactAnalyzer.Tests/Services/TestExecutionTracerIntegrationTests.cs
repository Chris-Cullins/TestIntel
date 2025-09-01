using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.Core.Interfaces;
using TestIntelligence.Core.Models;
using TestIntelligence.ImpactAnalyzer.Services;
using TestIntelligence.ImpactAnalyzer.Analysis;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Services
{
    public class TestExecutionTracerIntegrationTests
    {
        private readonly IRoslynAnalyzer _mockRoslynAnalyzer;
        private readonly ILogger<TestExecutionTracer> _mockLogger;
        private readonly TestExecutionTracer _tracer;
        private readonly MethodCallGraph _mockCallGraph;

        public TestExecutionTracerIntegrationTests()
        {
            _mockRoslynAnalyzer = Substitute.For<IRoslynAnalyzer>();
            _mockLogger = Substitute.For<ILogger<TestExecutionTracer>>();
            _mockCallGraph = Substitute.For<MethodCallGraph>();
            _tracer = new TestExecutionTracer(_mockRoslynAnalyzer, _mockLogger);
        }

        [Fact]
        public async Task TraceTestExecutionAsync_WithDeepCallChain_HandlesMaxDepthCorrectly()
        {
            // Arrange
            const string testMethodId = "TestMethod1";
            const string solutionPath = "/path/to/solution.sln";
            const int chainDepth = 25; // Exceeds MaxCallDepth (20)

            var testMethod = CreateTestMethodInfo(testMethodId, "ShouldHandleDeepChain");
            SetupDeepCallChain(testMethodId, solutionPath, testMethod, chainDepth);

            // Act
            var result = await _tracer.TraceTestExecutionAsync(testMethodId, solutionPath);

            // Assert
            result.Should().NotBeNull();
            // Should stop at max depth, so we expect fewer than the full chain
            result.ExecutedMethods.Should().HaveCountLessThan(chainDepth);
            result.ExecutedMethods.Max(em => em.CallDepth).Should().BeLessOrEqualTo(20);
        }

        [Fact]
        public async Task TraceTestExecutionAsync_WithCircularReferences_PreventsInfiniteLoop()
        {
            // Arrange
            const string testMethodId = "TestMethod1";
            const string solutionPath = "/path/to/solution.sln";

            var testMethod = CreateTestMethodInfo(testMethodId, "ShouldHandleCircularRefs");
            var method1 = CreateProductionMethodInfo("Method1", "CallsMethod2", "MyApp.Service1");
            var method2 = CreateProductionMethodInfo("Method2", "CallsMethod1", "MyApp.Service2");

            SetupCircularCallChain(testMethodId, solutionPath, testMethod, method1, method2);

            // Act
            var result = await _tracer.TraceTestExecutionAsync(testMethodId, solutionPath);

            // Assert
            result.Should().NotBeNull();
            // Should not hang and should complete in reasonable time
            result.ExecutedMethods.Should().HaveCountLessOrEqualTo(50); // Some reasonable upper bound
            
            // Should have visited both methods in the circular reference
            var visitedMethodIds = result.ExecutedMethods.Select(em => em.MethodId).ToHashSet();
            visitedMethodIds.Should().Contain("Method1");
            visitedMethodIds.Should().Contain("Method2");
        }

        [Fact]
        public async Task TraceTestExecutionAsync_WithLargeBreadth_LimitsBreadthCorrectly()
        {
            // Arrange
            const string testMethodId = "TestMethod1";
            const string solutionPath = "/path/to/solution.sln";
            const int methodCount = 100; // Large number of methods called

            var testMethod = CreateTestMethodInfo(testMethodId, "ShouldHandleLargeBreadth");
            SetupLargeBreadthCallGraph(testMethodId, solutionPath, testMethod, methodCount);

            // Act
            var result = await _tracer.TraceTestExecutionAsync(testMethodId, solutionPath);

            // Assert
            result.Should().NotBeNull();
            // Implementation limits breadth to 50 methods per level
            result.ExecutedMethods.Should().HaveCountLessOrEqualTo(50);
        }

        [Fact]
        public async Task TraceTestExecutionAsync_WithCachingEnabled_UsesCachedResults()
        {
            // Arrange
            const string testMethodId = "TestMethod1";
            const string solutionPath = "/path/to/solution.sln";

            var testMethod = CreateTestMethodInfo(testMethodId, "ShouldUseCache");
            var productionMethod = CreateProductionMethodInfo("ProductionMethod1", "DoWork", "MyApp.Service");

            SetupSimpleCallGraph(testMethodId, solutionPath, testMethod, productionMethod);

            // Act - First call
            var result1 = await _tracer.TraceTestExecutionAsync(testMethodId, solutionPath);
            
            // Act - Second call (should use cache)
            var result2 = await _tracer.TraceTestExecutionAsync(testMethodId, solutionPath);

            // Assert
            result1.Should().NotBeNull();
            result2.Should().NotBeNull();
            result1.TestMethodId.Should().Be(result2.TestMethodId);
            result1.ExecutedMethods.Count.Should().Be(result2.ExecutedMethods.Count);
            
            // Should have only called BuildCallGraphAsync once due to caching
            await _mockRoslynAnalyzer.Received(1).BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task TraceMultipleTestsAsync_WithLargeTestSuite_UsesParallelProcessing()
        {
            // Arrange
            const string solutionPath = "/path/to/solution.sln";
            var testMethodIds = Enumerable.Range(1, 15) // > 10 to trigger parallel processing
                .Select(i => $"TestMethod{i}")
                .ToArray();

            SetupLargeTestSuite(solutionPath, testMethodIds);

            // Act
            var result = await _tracer.TraceMultipleTestsAsync(testMethodIds, solutionPath);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(testMethodIds.Length);
            result.Select(r => r.TestMethodId).Should().BeEquivalentTo(testMethodIds);
        }

        [Fact]
        public async Task TraceTestExecutionAsync_WithAsyncMethods_TracesCorrectly()
        {
            // Arrange
            const string testMethodId = "AsyncTestMethod";
            const string solutionPath = "/path/to/solution.sln";

            var testMethod = CreateTestMethodInfo(testMethodId, "ShouldTraceAsyncMethods");
            var asyncMethod = CreateProductionMethodInfo("AsyncMethod", "ProcessAsync", "MyApp.AsyncService");
            var helperMethod = CreateProductionMethodInfo("HelperMethod", "ValidateAsync", "MyApp.ValidationService");

            SetupAsyncCallGraph(testMethodId, solutionPath, testMethod, asyncMethod, helperMethod);

            // Act
            var result = await _tracer.TraceTestExecutionAsync(testMethodId, solutionPath);

            // Assert
            result.Should().NotBeNull();
            result.ExecutedMethods.Should().HaveCount(2);
            result.ExecutedMethods.Should().Contain(em => em.MethodId == "AsyncMethod");
            result.ExecutedMethods.Should().Contain(em => em.MethodId == "HelperMethod");
        }

        [Fact]
        public async Task GenerateCoverageReportAsync_WithMixedCodebase_GeneratesAccurateReport()
        {
            // Arrange
            const string solutionPath = "/path/to/solution.sln";

            SetupMixedCodebase(solutionPath);

            // Act
            var result = await _tracer.GenerateCoverageReportAsync(solutionPath);

            // Assert
            result.Should().NotBeNull();
            result.TestToExecutionMap.Should().HaveCountGreaterThan(0);
            result.UncoveredMethods.Should().HaveCountGreaterThan(0);
            
            result.Statistics.TotalTestMethods.Should().BeGreaterThan(0);
            result.Statistics.TotalProductionMethods.Should().BeGreaterThan(0);
            result.Statistics.CoveragePercentage.Should().BeInRange(0, 100);
            
            result.Statistics.CategoryBreakdown.Should().ContainKey(MethodCategory.BusinessLogic);
            result.Statistics.CategoryBreakdown.Should().ContainKey(MethodCategory.Infrastructure);
            
            result.GeneratedTimestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        }

        [Fact]
        public async Task TraceTestExecutionAsync_WithGenericMethods_HandlesCorrectly()
        {
            // Arrange
            const string testMethodId = "GenericTestMethod";
            const string solutionPath = "/path/to/solution.sln";

            var testMethod = CreateTestMethodInfo(testMethodId, "ShouldHandleGenerics");
            var genericMethod = CreateProductionMethodInfo("GenericMethod", "Process<T>", "MyApp.GenericService<T>");

            SetupGenericMethodCallGraph(testMethodId, solutionPath, testMethod, genericMethod);

            // Act
            var result = await _tracer.TraceTestExecutionAsync(testMethodId, solutionPath);

            // Assert
            result.Should().NotBeNull();
            result.ExecutedMethods.Should().HaveCount(1);
            result.ExecutedMethods.First().MethodId.Should().Be("GenericMethod");
            result.ExecutedMethods.First().MethodName.Should().Be("Process<T>");
            result.ExecutedMethods.First().ContainingType.Should().Be("MyApp.GenericService<T>");
        }

        private MethodInfo CreateTestMethodInfo(string id, string name)
        {
            return new MethodInfo(id, name, "MyApp.Tests.TestClass", "/tests/TestClass.cs", 10, true);
        }

        private MethodInfo CreateProductionMethodInfo(string id, string name, string containingType)
        {
            return new MethodInfo(id, name, containingType, "/src/ProductionClass.cs", 20, false);
        }

        private void SetupDeepCallChain(string testMethodId, string solutionPath, MethodInfo testMethod, int depth)
        {
            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(_mockCallGraph);
            _mockCallGraph.GetMethodInfo(testMethodId).Returns(testMethod);

            var currentMethodId = testMethodId;
            for (int i = 1; i <= depth; i++)
            {
                var nextMethodId = $"Method{i}";
                var nextMethod = CreateProductionMethodInfo(nextMethodId, $"Method{i}", $"MyApp.Service{i}");
                
                _mockCallGraph.GetMethodCalls(currentMethodId).Returns(new[] { nextMethodId });
                _mockCallGraph.GetMethodInfo(nextMethodId).Returns(nextMethod);
                
                currentMethodId = nextMethodId;
            }
            
            // Last method calls nothing
            _mockCallGraph.GetMethodCalls(currentMethodId).Returns(Array.Empty<string>());
        }

        private void SetupCircularCallChain(string testMethodId, string solutionPath, 
            MethodInfo testMethod, MethodInfo method1, MethodInfo method2)
        {
            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(_mockCallGraph);
                
            _mockCallGraph.GetMethodInfo(testMethodId).Returns(testMethod);
            _mockCallGraph.GetMethodInfo("Method1").Returns(method1);
            _mockCallGraph.GetMethodInfo("Method2").Returns(method2);

            // Create circular reference
            _mockCallGraph.GetMethodCalls(testMethodId).Returns(new[] { "Method1" });
            _mockCallGraph.GetMethodCalls("Method1").Returns(new[] { "Method2" });
            _mockCallGraph.GetMethodCalls("Method2").Returns(new[] { "Method1" }); // Back to Method1
        }

        private void SetupLargeBreadthCallGraph(string testMethodId, string solutionPath, 
            MethodInfo testMethod, int methodCount)
        {
            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(_mockCallGraph);
            _mockCallGraph.GetMethodInfo(testMethodId).Returns(testMethod);

            var calledMethods = Enumerable.Range(1, methodCount)
                .Select(i => $"Method{i}")
                .ToArray();

            _mockCallGraph.GetMethodCalls(testMethodId).Returns(calledMethods);

            // Set up all the called methods
            foreach (var methodId in calledMethods)
            {
                var method = CreateProductionMethodInfo(methodId, methodId, $"MyApp.Service{methodId}");
                _mockCallGraph.GetMethodInfo(methodId).Returns(method);
                _mockCallGraph.GetMethodCalls(methodId).Returns(Array.Empty<string>());
            }
        }

        private void SetupSimpleCallGraph(string testMethodId, string solutionPath, 
            MethodInfo testMethod, MethodInfo productionMethod)
        {
            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(_mockCallGraph);
            _mockCallGraph.GetMethodInfo(testMethodId).Returns(testMethod);
            _mockCallGraph.GetMethodInfo("ProductionMethod1").Returns(productionMethod);
            _mockCallGraph.GetMethodCalls(testMethodId).Returns(new[] { "ProductionMethod1" });
            _mockCallGraph.GetMethodCalls("ProductionMethod1").Returns(Array.Empty<string>());
        }

        private void SetupLargeTestSuite(string solutionPath, string[] testMethodIds)
        {
            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(_mockCallGraph);

            var allMethods = new List<string>(testMethodIds);
            
            foreach (var testMethodId in testMethodIds)
            {
                var testMethod = CreateTestMethodInfo(testMethodId, $"Test{testMethodId}");
                var productionMethodId = $"Production{testMethodId}";
                var productionMethod = CreateProductionMethodInfo(productionMethodId, 
                    $"DoWork{testMethodId}", $"MyApp.Service{testMethodId}");

                allMethods.Add(productionMethodId);

                _mockCallGraph.GetMethodInfo(testMethodId).Returns(testMethod);
                _mockCallGraph.GetMethodInfo(productionMethodId).Returns(productionMethod);
                _mockCallGraph.GetMethodCalls(testMethodId).Returns(new[] { productionMethodId });
                _mockCallGraph.GetMethodCalls(productionMethodId).Returns(Array.Empty<string>());
            }

            _mockCallGraph.GetAllMethods().Returns(allMethods.ToArray());
        }

        private void SetupAsyncCallGraph(string testMethodId, string solutionPath, 
            MethodInfo testMethod, MethodInfo asyncMethod, MethodInfo helperMethod)
        {
            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(_mockCallGraph);
                
            _mockCallGraph.GetMethodInfo(testMethodId).Returns(testMethod);
            _mockCallGraph.GetMethodInfo("AsyncMethod").Returns(asyncMethod);
            _mockCallGraph.GetMethodInfo("HelperMethod").Returns(helperMethod);

            _mockCallGraph.GetMethodCalls(testMethodId).Returns(new[] { "AsyncMethod" });
            _mockCallGraph.GetMethodCalls("AsyncMethod").Returns(new[] { "HelperMethod" });
            _mockCallGraph.GetMethodCalls("HelperMethod").Returns(Array.Empty<string>());
        }

        private void SetupMixedCodebase(string solutionPath)
        {
            var testMethods = new[] { "Test1", "Test2", "Test3" };
            var productionMethods = new[] { "Business1", "Business2", "Infrastructure1", "Uncovered1" };
            var allMethods = testMethods.Concat(productionMethods).ToArray();

            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(_mockCallGraph);
            _mockCallGraph.GetAllMethods().Returns(allMethods);

            // Setup test methods
            foreach (var testId in testMethods)
            {
                var test = CreateTestMethodInfo(testId, $"Should{testId}");
                _mockCallGraph.GetMethodInfo(testId).Returns(test);
            }

            // Setup production methods
            _mockCallGraph.GetMethodInfo("Business1").Returns(CreateProductionMethodInfo("Business1", "ProcessOrder", "MyApp.Business.OrderService"));
            _mockCallGraph.GetMethodInfo("Business2").Returns(CreateProductionMethodInfo("Business2", "CalculatePrice", "MyApp.Business.PriceCalculator"));
            _mockCallGraph.GetMethodInfo("Infrastructure1").Returns(CreateProductionMethodInfo("Infrastructure1", "LogMessage", "MyApp.Infrastructure.Logger"));
            _mockCallGraph.GetMethodInfo("Uncovered1").Returns(CreateProductionMethodInfo("Uncovered1", "UnusedMethod", "MyApp.Unused.Service"));

            // Setup call relationships
            _mockCallGraph.GetMethodCalls("Test1").Returns(new[] { "Business1" });
            _mockCallGraph.GetMethodCalls("Test2").Returns(new[] { "Business2", "Infrastructure1" });
            _mockCallGraph.GetMethodCalls("Test3").Returns(new[] { "Infrastructure1" });
            _mockCallGraph.GetMethodCalls("Business1").Returns(Array.Empty<string>());
            _mockCallGraph.GetMethodCalls("Business2").Returns(Array.Empty<string>());
            _mockCallGraph.GetMethodCalls("Infrastructure1").Returns(Array.Empty<string>());
            _mockCallGraph.GetMethodCalls("Uncovered1").Returns(Array.Empty<string>());
        }

        private void SetupGenericMethodCallGraph(string testMethodId, string solutionPath, 
            MethodInfo testMethod, MethodInfo genericMethod)
        {
            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(_mockCallGraph);
                
            _mockCallGraph.GetMethodInfo(testMethodId).Returns(testMethod);
            _mockCallGraph.GetMethodInfo("GenericMethod").Returns(genericMethod);
            _mockCallGraph.GetMethodCalls(testMethodId).Returns(new[] { "GenericMethod" });
            _mockCallGraph.GetMethodCalls("GenericMethod").Returns(Array.Empty<string>());
        }
    }
}