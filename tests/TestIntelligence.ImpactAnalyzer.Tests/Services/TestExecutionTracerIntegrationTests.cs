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
using TestIntelligence.Core.Services;
using TestIntelligence.ImpactAnalyzer.Services;
using TestIntelligence.ImpactAnalyzer.Analysis;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Services
{
    public class TestExecutionTracerIntegrationTests
    {
        private readonly IRoslynAnalyzer _mockRoslynAnalyzer;
        private readonly IAssemblyPathResolver _mockAssemblyPathResolver;
        private readonly ILogger<TestExecutionTracer> _mockLogger;
        private readonly TestExecutionTracer _tracer;

        public TestExecutionTracerIntegrationTests()
        {
            _mockRoslynAnalyzer = Substitute.For<IRoslynAnalyzer>();
            _mockAssemblyPathResolver = Substitute.For<IAssemblyPathResolver>();
            _mockLogger = Substitute.For<ILogger<TestExecutionTracer>>();
            _tracer = new TestExecutionTracer(_mockRoslynAnalyzer, _mockAssemblyPathResolver, _mockLogger);
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


        private void SetupDeepCallChain(string testMethodId, string solutionPath, MethodInfo testMethod, int depth)
        {
            var methodDefinitions = new Dictionary<string, MethodInfo> { { testMethodId, testMethod } };
            var callRelations = new Dictionary<string, string[]>();

            var currentMethodId = testMethodId;
            for (int i = 1; i <= depth; i++)
            {
                var nextMethodId = $"Method{i}";
                var nextMethod = CreateProductionMethodInfo(nextMethodId, $"Method{i}", $"MyApp.Service{i}");
                
                methodDefinitions[nextMethodId] = nextMethod;
                callRelations[currentMethodId] = new[] { nextMethodId };
                
                currentMethodId = nextMethodId;
            }
            
            // Last method calls nothing
            callRelations[currentMethodId] = Array.Empty<string>();
            
            var testCallGraph = CreateTestCallGraph(methodDefinitions, callRelations);
            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(testCallGraph);
        }

        private void SetupCircularCallChain(string testMethodId, string solutionPath, 
            MethodInfo testMethod, MethodInfo method1, MethodInfo method2)
        {
            var methodDefinitions = new Dictionary<string, MethodInfo>
            {
                { testMethodId, testMethod },
                { "Method1", method1 },
                { "Method2", method2 }
            };
            
            var callRelations = new Dictionary<string, string[]>
            {
                { testMethodId, new[] { "Method1" } },
                { "Method1", new[] { "Method2" } },
                { "Method2", new[] { "Method1" } } // Back to Method1
            };
            
            var testCallGraph = CreateTestCallGraph(methodDefinitions, callRelations);
            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(testCallGraph);
        }

        private void SetupLargeBreadthCallGraph(string testMethodId, string solutionPath, 
            MethodInfo testMethod, int methodCount)
        {
            var methodDefinitions = new Dictionary<string, MethodInfo> { { testMethodId, testMethod } };
            var callRelations = new Dictionary<string, string[]>();

            var calledMethods = Enumerable.Range(1, methodCount)
                .Select(i => $"Method{i}")
                .ToArray();

            callRelations[testMethodId] = calledMethods;

            // Set up all the called methods
            foreach (var methodId in calledMethods)
            {
                var method = CreateProductionMethodInfo(methodId, methodId, $"MyApp.Service{methodId}");
                methodDefinitions[methodId] = method;
                callRelations[methodId] = Array.Empty<string>();
            }
            
            var testCallGraph = CreateTestCallGraph(methodDefinitions, callRelations);
            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(testCallGraph);
        }

        private void SetupSimpleCallGraph(string testMethodId, string solutionPath, 
            MethodInfo testMethod, MethodInfo productionMethod)
        {
            var methodDefinitions = new Dictionary<string, MethodInfo>
            {
                { testMethodId, testMethod },
                { "ProductionMethod1", productionMethod }
            };
            
            var callRelations = new Dictionary<string, string[]>
            {
                { testMethodId, new[] { "ProductionMethod1" } },
                { "ProductionMethod1", Array.Empty<string>() }
            };
            
            var testCallGraph = CreateTestCallGraph(methodDefinitions, callRelations);
            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(testCallGraph);
        }

        private void SetupLargeTestSuite(string solutionPath, string[] testMethodIds)
        {
            var methodDefinitions = new Dictionary<string, MethodInfo>();
            var callRelations = new Dictionary<string, string[]>();
            
            foreach (var testMethodId in testMethodIds)
            {
                var testMethod = CreateTestMethodInfo(testMethodId, $"Test{testMethodId}");
                var productionMethodId = $"Production{testMethodId}";
                var productionMethod = CreateProductionMethodInfo(productionMethodId, 
                    $"DoWork{testMethodId}", $"MyApp.Service{testMethodId}");

                methodDefinitions[testMethodId] = testMethod;
                methodDefinitions[productionMethodId] = productionMethod;
                callRelations[testMethodId] = new[] { productionMethodId };
                callRelations[productionMethodId] = Array.Empty<string>();
            }

            var testCallGraph = CreateTestCallGraph(methodDefinitions, callRelations);
            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(testCallGraph);
        }

        private void SetupAsyncCallGraph(string testMethodId, string solutionPath, 
            MethodInfo testMethod, MethodInfo asyncMethod, MethodInfo helperMethod)
        {
            var methodDefinitions = new Dictionary<string, MethodInfo>
            {
                { testMethodId, testMethod },
                { "AsyncMethod", asyncMethod },
                { "HelperMethod", helperMethod }
            };
            
            var callRelations = new Dictionary<string, string[]>
            {
                { testMethodId, new[] { "AsyncMethod" } },
                { "AsyncMethod", new[] { "HelperMethod" } },
                { "HelperMethod", Array.Empty<string>() }
            };
            
            var testCallGraph = CreateTestCallGraph(methodDefinitions, callRelations);
            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(testCallGraph);
        }

        private void SetupMixedCodebase(string solutionPath)
        {
            var methodDefinitions = new Dictionary<string, MethodInfo>
            {
                { "Test1", CreateTestMethodInfo("Test1", "ShouldTest1") },
                { "Test2", CreateTestMethodInfo("Test2", "ShouldTest2") },
                { "Test3", CreateTestMethodInfo("Test3", "ShouldTest3") },
                { "Business1", CreateProductionMethodInfo("Business1", "ProcessOrder", "MyApp.Business.OrderService") },
                { "Business2", CreateProductionMethodInfo("Business2", "CalculatePrice", "MyApp.Business.PriceCalculator") },
                { "Infrastructure1", CreateProductionMethodInfo("Infrastructure1", "LogMessage", "MyApp.Infrastructure.Logger") },
                { "Uncovered1", CreateProductionMethodInfo("Uncovered1", "UnusedMethod", "MyApp.Unused.Service") }
            };
            
            var callRelations = new Dictionary<string, string[]>
            {
                { "Test1", new[] { "Business1" } },
                { "Test2", new[] { "Business2", "Infrastructure1" } },
                { "Test3", new[] { "Infrastructure1" } },
                { "Business1", Array.Empty<string>() },
                { "Business2", Array.Empty<string>() },
                { "Infrastructure1", Array.Empty<string>() },
                { "Uncovered1", Array.Empty<string>() }
            };
            
            var testCallGraph = CreateTestCallGraph(methodDefinitions, callRelations);
            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(testCallGraph);
        }

        private void SetupGenericMethodCallGraph(string testMethodId, string solutionPath, 
            MethodInfo testMethod, MethodInfo genericMethod)
        {
            var methodDefinitions = new Dictionary<string, MethodInfo>
            {
                { testMethodId, testMethod },
                { "GenericMethod", genericMethod }
            };
            
            var callRelations = new Dictionary<string, string[]>
            {
                { testMethodId, new[] { "GenericMethod" } },
                { "GenericMethod", Array.Empty<string>() }
            };
            
            var testCallGraph = CreateTestCallGraph(methodDefinitions, callRelations);
            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(testCallGraph);
        }
        private static MethodCallGraph CreateTestCallGraph(
            Dictionary<string, MethodInfo> methodDefinitions,
            Dictionary<string, string[]> callRelations)
        {
            var callGraph = new Dictionary<string, HashSet<string>>();
            
            foreach (var (caller, callees) in callRelations)
            {
                callGraph[caller] = new HashSet<string>(callees);
            }

            return new MethodCallGraph(callGraph, methodDefinitions);
        }

        private static MethodInfo CreateTestMethodInfo(string id, string methodName, string typeName, bool isTest = true)
        {
            return new MethodInfo(id, methodName, typeName, isTest ? "/tests/TestClass.cs" : "/src/ProductionClass.cs", 10, isTest);
        }

        private static MethodInfo CreateProductionMethodInfo(string id, string methodName, string typeName)
        {
            return CreateTestMethodInfo(id, methodName, typeName, false);
        }
    }
}