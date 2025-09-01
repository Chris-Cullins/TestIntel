using System;
using System.Collections.Generic;
using System.IO;
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
    public class TestExecutionTracerTests
    {
        private readonly IRoslynAnalyzer _mockRoslynAnalyzer;
        private readonly ILogger<TestExecutionTracer> _mockLogger;
        private readonly TestExecutionTracer _tracer;
        private readonly MethodCallGraph _mockCallGraph;

        public TestExecutionTracerTests()
        {
            _mockRoslynAnalyzer = Substitute.For<IRoslynAnalyzer>();
            _mockLogger = Substitute.For<ILogger<TestExecutionTracer>>();
            _mockCallGraph = Substitute.For<MethodCallGraph>();
            _tracer = new TestExecutionTracer(_mockRoslynAnalyzer, _mockLogger);
        }

        [Fact]
        public void Constructor_WithNullRoslynAnalyzer_ThrowsArgumentNullException()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => 
                new TestExecutionTracer(null!, _mockLogger));
            
            exception.ParamName.Should().Be("roslynAnalyzer");
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => 
                new TestExecutionTracer(_mockRoslynAnalyzer, null!));
            
            exception.ParamName.Should().Be("logger");
        }

        [Fact]
        public async Task TraceTestExecutionAsync_WithNullTestMethodId_ThrowsArgumentException()
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
                _tracer.TraceTestExecutionAsync(null!, "/path/to/solution.sln"));
            
            exception.ParamName.Should().Be("testMethodId");
        }

        [Fact]
        public async Task TraceTestExecutionAsync_WithEmptyTestMethodId_ThrowsArgumentException()
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
                _tracer.TraceTestExecutionAsync(string.Empty, "/path/to/solution.sln"));
            
            exception.ParamName.Should().Be("testMethodId");
        }

        [Fact]
        public async Task TraceTestExecutionAsync_WithNullSolutionPath_ThrowsArgumentException()
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
                _tracer.TraceTestExecutionAsync("TestMethod1", null!));
            
            exception.ParamName.Should().Be("solutionPath");
        }

        [Fact]
        public async Task TraceTestExecutionAsync_WithEmptySolutionPath_ThrowsArgumentException()
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
                _tracer.TraceTestExecutionAsync("TestMethod1", string.Empty));
            
            exception.ParamName.Should().Be("solutionPath");
        }

        [Fact]
        public async Task TraceTestExecutionAsync_WithNonExistentMethod_ThrowsArgumentException()
        {
            // Arrange
            const string testMethodId = "NonExistentMethod";
            const string solutionPath = "/path/to/solution.sln";

            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(_mockCallGraph);
            _mockCallGraph.GetMethodInfo(testMethodId).Returns((MethodInfo?)null);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
                _tracer.TraceTestExecutionAsync(testMethodId, solutionPath));
            
            exception.ParamName.Should().Be("testMethodId");
            exception.Message.Should().Contain("Test method not found");
        }

        [Fact]
        public async Task TraceTestExecutionAsync_WithNonTestMethod_ThrowsArgumentException()
        {
            // Arrange
            const string methodId = "ProductionMethod1";
            const string solutionPath = "/path/to/solution.sln";

            var methodInfo = new MethodInfo(methodId, "SomeProductionMethod", 
                "MyApp.Services.ProductionService", "/src/ProductionService.cs", 42, false);

            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(_mockCallGraph);
            _mockCallGraph.GetMethodInfo(methodId).Returns(methodInfo);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
                _tracer.TraceTestExecutionAsync(methodId, solutionPath));
            
            exception.ParamName.Should().Be("testMethodId");
            exception.Message.Should().Contain("Method is not a test method");
        }

        [Fact]
        public async Task TraceTestExecutionAsync_WithValidTestMethod_ReturnsExecutionTrace()
        {
            // Arrange
            const string testMethodId = "TestMethod1";
            const string solutionPath = "/path/to/solution.sln";

            var testMethod = CreateTestMethodInfo(testMethodId, "ShouldCalculateTotal", 
                "MyApp.Tests.CalculatorTests", "/tests/CalculatorTests.cs");
            var productionMethod1 = CreateProductionMethodInfo("ProductionMethod1", "Calculate", 
                "MyApp.Services.Calculator", "/src/Calculator.cs");
            var productionMethod2 = CreateProductionMethodInfo("ProductionMethod2", "Validate", 
                "MyApp.Services.Validator", "/src/Validator.cs");

            SetupCallGraph(testMethodId, solutionPath, testMethod, new[]
            {
                (testMethodId, new[] { "ProductionMethod1" }),
                ("ProductionMethod1", new[] { "ProductionMethod2" }),
                ("ProductionMethod2", Array.Empty<string>())
            });

            _mockCallGraph.GetMethodInfo("ProductionMethod1").Returns(productionMethod1);
            _mockCallGraph.GetMethodInfo("ProductionMethod2").Returns(productionMethod2);

            // Act
            var result = await _tracer.TraceTestExecutionAsync(testMethodId, solutionPath);

            // Assert
            result.Should().NotBeNull();
            result.TestMethodId.Should().Be(testMethodId);
            result.TestMethodName.Should().Be("ShouldCalculateTotal");
            result.TestClassName.Should().Be("MyApp.Tests.CalculatorTests");
            result.ExecutedMethods.Should().HaveCount(2); // Should exclude the test method itself
            result.TotalMethodsCalled.Should().Be(2);
            result.ProductionMethodsCalled.Should().Be(2);
            result.TraceTimestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

            var executedMethod1 = result.ExecutedMethods.First(em => em.MethodId == "ProductionMethod1");
            executedMethod1.MethodName.Should().Be("Calculate");
            executedMethod1.ContainingType.Should().Be("MyApp.Services.Calculator");
            executedMethod1.CallDepth.Should().Be(1);
            executedMethod1.IsProductionCode.Should().BeTrue();
            executedMethod1.CallPath.Should().Equal(testMethodId, "ProductionMethod1");

            var executedMethod2 = result.ExecutedMethods.First(em => em.MethodId == "ProductionMethod2");
            executedMethod2.MethodName.Should().Be("Validate");
            executedMethod2.ContainingType.Should().Be("MyApp.Services.Validator");
            executedMethod2.CallDepth.Should().Be(2);
            executedMethod2.IsProductionCode.Should().BeTrue();
            executedMethod2.CallPath.Should().Equal(testMethodId, "ProductionMethod1", "ProductionMethod2");
        }

        [Fact]
        public async Task TraceTestExecutionAsync_WithTestMethodCallingTestUtility_FiltersOutTestMethods()
        {
            // Arrange
            const string testMethodId = "TestMethod1";
            const string solutionPath = "/path/to/solution.sln";

            var testMethod = CreateTestMethodInfo(testMethodId, "ShouldDoSomething", 
                "MyApp.Tests.SomeTests", "/tests/SomeTests.cs");
            var testUtilityMethod = CreateTestMethodInfo("TestUtility1", "SetupTestData", 
                "MyApp.Tests.TestUtils", "/tests/TestUtils.cs");
            var productionMethod = CreateProductionMethodInfo("ProductionMethod1", "DoWork", 
                "MyApp.Services.Worker", "/src/Worker.cs");

            SetupCallGraph(testMethodId, solutionPath, testMethod, new[]
            {
                (testMethodId, new[] { "TestUtility1", "ProductionMethod1" }),
                ("TestUtility1", Array.Empty<string>()),
                ("ProductionMethod1", Array.Empty<string>())
            });

            _mockCallGraph.GetMethodInfo("TestUtility1").Returns(testUtilityMethod);
            _mockCallGraph.GetMethodInfo("ProductionMethod1").Returns(productionMethod);

            // Act
            var result = await _tracer.TraceTestExecutionAsync(testMethodId, solutionPath);

            // Assert
            result.ExecutedMethods.Should().HaveCount(2); // Test utility + production method
            
            var testUtilityExecution = result.ExecutedMethods.First(em => em.MethodId == "TestUtility1");
            testUtilityExecution.IsProductionCode.Should().BeFalse();
            testUtilityExecution.Category.Should().Be(MethodCategory.TestUtility);

            var productionExecution = result.ExecutedMethods.First(em => em.MethodId == "ProductionMethod1");
            productionExecution.IsProductionCode.Should().BeTrue();
            productionExecution.Category.Should().Be(MethodCategory.BusinessLogic);
            
            result.ProductionMethodsCalled.Should().Be(1); // Only the production method
        }

        [Fact]
        public async Task TraceTestExecutionAsync_WithFrameworkMethods_CategorizesProperly()
        {
            // Arrange
            const string testMethodId = "TestMethod1";
            const string solutionPath = "/path/to/solution.sln";

            var testMethod = CreateTestMethodInfo(testMethodId, "ShouldSerializeData", 
                "MyApp.Tests.SerializationTests", "/tests/SerializationTests.cs");
            var frameworkMethod = CreateProductionMethodInfo("FrameworkMethod1", "Serialize", 
                "System.Text.Json.JsonSerializer", "system_assembly");
            var productionMethod = CreateProductionMethodInfo("ProductionMethod1", "PrepareData", 
                "MyApp.Services.DataService", "/src/DataService.cs");

            SetupCallGraph(testMethodId, solutionPath, testMethod, new[]
            {
                (testMethodId, new[] { "ProductionMethod1" }),
                ("ProductionMethod1", new[] { "FrameworkMethod1" }),
                ("FrameworkMethod1", Array.Empty<string>())
            });

            _mockCallGraph.GetMethodInfo("ProductionMethod1").Returns(productionMethod);
            _mockCallGraph.GetMethodInfo("FrameworkMethod1").Returns(frameworkMethod);

            // Act
            var result = await _tracer.TraceTestExecutionAsync(testMethodId, solutionPath);

            // Assert
            var frameworkExecution = result.ExecutedMethods.First(em => em.MethodId == "FrameworkMethod1");
            frameworkExecution.IsProductionCode.Should().BeFalse();
            frameworkExecution.Category.Should().Be(MethodCategory.Framework);

            var productionExecution = result.ExecutedMethods.First(em => em.MethodId == "ProductionMethod1");
            productionExecution.IsProductionCode.Should().BeTrue();
            
            result.ProductionMethodsCalled.Should().Be(1); // Only the production method
        }

        [Fact]
        public async Task TraceMultipleTestsAsync_WithNullTestMethodIds_ThrowsArgumentNullException()
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _tracer.TraceMultipleTestsAsync(null!, "/path/to/solution.sln"));
            
            exception.ParamName.Should().Be("testMethodIds");
        }

        [Fact]
        public async Task TraceMultipleTestsAsync_WithEmptyList_ReturnsEmptyList()
        {
            // Act
            var result = await _tracer.TraceMultipleTestsAsync(Array.Empty<string>(), "/path/to/solution.sln");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task TraceMultipleTestsAsync_WithValidTestMethods_ReturnsAllTraces()
        {
            // Arrange
            const string solutionPath = "/path/to/solution.sln";
            var testMethodIds = new[] { "TestMethod1", "TestMethod2" };

            var testMethod1 = CreateTestMethodInfo("TestMethod1", "ShouldDoWork1", 
                "MyApp.Tests.Test1", "/tests/Test1.cs");
            var testMethod2 = CreateTestMethodInfo("TestMethod2", "ShouldDoWork2", 
                "MyApp.Tests.Test2", "/tests/Test2.cs");
            var productionMethod1 = CreateProductionMethodInfo("ProductionMethod1", "Work1", 
                "MyApp.Services.Service1", "/src/Service1.cs");
            var productionMethod2 = CreateProductionMethodInfo("ProductionMethod2", "Work2", 
                "MyApp.Services.Service2", "/src/Service2.cs");

            SetupCallGraphForMultipleTests(solutionPath, new Dictionary<string, MethodInfo>
            {
                { "TestMethod1", testMethod1 },
                { "TestMethod2", testMethod2 },
                { "ProductionMethod1", productionMethod1 },
                { "ProductionMethod2", productionMethod2 }
            }, new[]
            {
                ("TestMethod1", new[] { "ProductionMethod1" }),
                ("TestMethod2", new[] { "ProductionMethod2" }),
                ("ProductionMethod1", Array.Empty<string>()),
                ("ProductionMethod2", Array.Empty<string>())
            });

            // Act
            var result = await _tracer.TraceMultipleTestsAsync(testMethodIds, solutionPath);

            // Assert
            result.Should().HaveCount(2);
            result.Select(r => r.TestMethodId).Should().BeEquivalentTo(testMethodIds);
            result.All(r => r.ExecutedMethods.Count > 0).Should().BeTrue();
        }

        [Fact]
        public async Task GenerateCoverageReportAsync_WithValidSolution_ReturnsComprehensiveReport()
        {
            // Arrange
            const string solutionPath = "/path/to/solution.sln";

            var testMethod1 = CreateTestMethodInfo("TestMethod1", "ShouldWork", 
                "MyApp.Tests.WorkerTests", "/tests/WorkerTests.cs");
            var testMethod2 = CreateTestMethodInfo("TestMethod2", "ShouldCalculate", 
                "MyApp.Tests.CalculatorTests", "/tests/CalculatorTests.cs");
            var productionMethod1 = CreateProductionMethodInfo("ProductionMethod1", "DoWork", 
                "MyApp.Services.Worker", "/src/Worker.cs");
            var productionMethod2 = CreateProductionMethodInfo("ProductionMethod2", "Calculate", 
                "MyApp.Services.Calculator", "/src/Calculator.cs");
            var uncoveredMethod = CreateProductionMethodInfo("UncoveredMethod", "UnusedMethod", 
                "MyApp.Services.UnusedService", "/src/UnusedService.cs");

            var allMethods = new[] { "TestMethod1", "TestMethod2", "ProductionMethod1", "ProductionMethod2", "UncoveredMethod" };
            var methodInfoMap = new Dictionary<string, MethodInfo>
            {
                { "TestMethod1", testMethod1 },
                { "TestMethod2", testMethod2 },
                { "ProductionMethod1", productionMethod1 },
                { "ProductionMethod2", productionMethod2 },
                { "UncoveredMethod", uncoveredMethod }
            };

            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(_mockCallGraph);
            _mockCallGraph.GetAllMethods().Returns(allMethods);

            foreach (var kvp in methodInfoMap)
            {
                _mockCallGraph.GetMethodInfo(kvp.Key).Returns(kvp.Value);
            }

            _mockCallGraph.GetMethodCalls("TestMethod1").Returns(new[] { "ProductionMethod1" });
            _mockCallGraph.GetMethodCalls("TestMethod2").Returns(new[] { "ProductionMethod2" });
            _mockCallGraph.GetMethodCalls("ProductionMethod1").Returns(Array.Empty<string>());
            _mockCallGraph.GetMethodCalls("ProductionMethod2").Returns(Array.Empty<string>());
            _mockCallGraph.GetMethodCalls("UncoveredMethod").Returns(Array.Empty<string>());

            // Act
            var result = await _tracer.GenerateCoverageReportAsync(solutionPath);

            // Assert
            result.Should().NotBeNull();
            result.TestToExecutionMap.Should().HaveCount(2);
            result.TestToExecutionMap.Should().ContainKey("TestMethod1");
            result.TestToExecutionMap.Should().ContainKey("TestMethod2");
            
            result.UncoveredMethods.Should().Contain("UncoveredMethod");
            result.UncoveredMethods.Should().NotContain("ProductionMethod1");
            result.UncoveredMethods.Should().NotContain("ProductionMethod2");
            
            result.Statistics.TotalTestMethods.Should().Be(2);
            result.Statistics.TotalProductionMethods.Should().Be(3); // ProductionMethod1, ProductionMethod2, UncoveredMethod
            result.Statistics.CoveredProductionMethods.Should().Be(2); // ProductionMethod1, ProductionMethod2
            result.Statistics.CoveragePercentage.Should().BeApproximately(66.67, 0.1); // 2/3 * 100
            
            result.GeneratedTimestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        }

        [Theory]
        [InlineData(MethodCategory.BusinessLogic, "MyApp.Services.BusinessService", "ProcessOrder")]
        [InlineData(MethodCategory.DataAccess, "MyApp.Data.OrderRepository", "SaveOrder")]
        [InlineData(MethodCategory.Infrastructure, "MyApp.Infrastructure.LoggingService", "LogMessage")]
        [InlineData(MethodCategory.Framework, "System.String", "Concat")]
        [InlineData(MethodCategory.ThirdParty, "Newtonsoft.Json.JsonConvert", "SerializeObject")]
        public async Task TraceTestExecutionAsync_CategorizesMethodsCorrectly(
            MethodCategory expectedCategory, string typeName, string methodName)
        {
            // Arrange
            const string testMethodId = "TestMethod1";
            const string solutionPath = "/path/to/solution.sln";

            var testMethod = CreateTestMethodInfo(testMethodId, "ShouldWork", 
                "MyApp.Tests.SomeTests", "/tests/SomeTests.cs");
            var targetMethod = CreateProductionMethodInfo("TargetMethod", methodName, typeName, "/some/path.cs");

            SetupCallGraph(testMethodId, solutionPath, testMethod, new[]
            {
                (testMethodId, new[] { "TargetMethod" }),
                ("TargetMethod", Array.Empty<string>())
            });

            _mockCallGraph.GetMethodInfo("TargetMethod").Returns(targetMethod);

            // Act
            var result = await _tracer.TraceTestExecutionAsync(testMethodId, solutionPath);

            // Assert
            result.ExecutedMethods.Should().HaveCount(1);
            result.ExecutedMethods.First().Category.Should().Be(expectedCategory);
        }

        private MethodInfo CreateTestMethodInfo(string id, string name, string containingType, string filePath)
        {
            return new MethodInfo(id, name, containingType, filePath, 10, true);
        }

        private MethodInfo CreateProductionMethodInfo(string id, string name, string containingType, string filePath)
        {
            return new MethodInfo(id, name, containingType, filePath, 20, false);
        }

        private void SetupCallGraph(string testMethodId, string solutionPath, MethodInfo testMethod, 
            (string methodId, string[] calledMethods)[] callRelations)
        {
            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(_mockCallGraph);
            _mockCallGraph.GetMethodInfo(testMethodId).Returns(testMethod);

            foreach (var (methodId, calledMethods) in callRelations)
            {
                _mockCallGraph.GetMethodCalls(methodId).Returns(calledMethods);
            }
        }

        private void SetupCallGraphForMultipleTests(string solutionPath, 
            Dictionary<string, MethodInfo> methodInfoMap,
            (string methodId, string[] calledMethods)[] callRelations)
        {
            _mockRoslynAnalyzer.BuildCallGraphAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(_mockCallGraph);

            var allMethodIds = methodInfoMap.Keys.ToArray();
            _mockCallGraph.GetAllMethods().Returns(allMethodIds);

            foreach (var kvp in methodInfoMap)
            {
                _mockCallGraph.GetMethodInfo(kvp.Key).Returns(kvp.Value);
            }

            foreach (var (methodId, calledMethods) in callRelations)
            {
                _mockCallGraph.GetMethodCalls(methodId).Returns(calledMethods);
            }
        }
    }
}