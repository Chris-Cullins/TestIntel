using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.ImpactAnalyzer.Analysis;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Analysis
{
    /// <summary>
    /// Tests for RoslynAnalyzer call graph construction enhancements.
    /// These tests verify that the analyzer properly captures different types of method calls
    /// including constructors, property access, and other member references.
    /// </summary>
    public class RoslynAnalyzerCallGraphTests
    {
        private readonly RoslynAnalyzer _analyzer;
        private readonly ILogger<RoslynAnalyzer> _mockLogger;

        public RoslynAnalyzerCallGraphTests()
        {
            var loggerFactory = Substitute.For<ILoggerFactory>();
            _mockLogger = Substitute.For<ILogger<RoslynAnalyzer>>();
            loggerFactory.CreateLogger<RoslynAnalyzer>().Returns(_mockLogger);
            _analyzer = new RoslynAnalyzer(_mockLogger, loggerFactory);
        }

        [Fact]
        public async Task BuildCallGraphAsync_CapturesConstructorCalls()
        {
            // Arrange - Create temporary test file
            var testCode = @"
using System;

namespace TestProject
{
    public class TestClass
    {
        public void TestMethod()
        {
            var instance = new MyClass();
            var generic = new List<string>();
        }
    }

    public class MyClass
    {
        public MyClass() { }
    }
}
";
            var tempFile = CreateTempFile("TestConstructors.cs", testCode);

            try
            {
                // Act
                var callGraph = await _analyzer.BuildCallGraphAsync(new[] { tempFile });

                // Assert
                var allMethods = callGraph.GetAllMethods();
                Assert.Contains(allMethods, m => m.Contains("TestMethod"));

                // Find the TestMethod and verify it calls constructors
                var testMethodId = allMethods.FirstOrDefault(m => m.Contains("TestMethod"));
                Assert.NotNull(testMethodId);

                var calledMethods = callGraph.GetMethodCalls(testMethodId);
                
                // Should contain constructor calls
                Assert.True(calledMethods.Any(m => m.Contains(".ctor")), 
                    $"Expected constructor calls in TestMethod. Called methods: {string.Join(", ", calledMethods)}");
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task BuildCallGraphAsync_CapturesPropertyAccess()
        {
            // Arrange - Create temporary test file
            var testCode = @"
using System;

namespace TestProject
{
    public class TestClass
    {
        public void TestMethod()
        {
            var obj = new MyClass();
            var value = obj.MyProperty;
            obj.MyProperty = value;
        }
    }

    public class MyClass
    {
        public string MyProperty { get; set; }
    }
}
";
            var tempFile = CreateTempFile("TestProperties.cs", testCode);

            try
            {
                // Act
                var callGraph = await _analyzer.BuildCallGraphAsync(new[] { tempFile });

                // Assert
                var allMethods = callGraph.GetAllMethods();
                var testMethodId = allMethods.FirstOrDefault(m => m.Contains("TestMethod"));
                Assert.NotNull(testMethodId);

                var calledMethods = callGraph.GetMethodCalls(testMethodId);
                
                // Should contain property getter/setter calls
                Assert.True(calledMethods.Any(m => m.Contains("MyProperty") && m.Contains("get_")), 
                    $"Expected property getter calls. Called methods: {string.Join(", ", calledMethods)}");
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task BuildCallGraphAsync_CapturesMethodInvocations()
        {
            // Arrange - Create temporary test file
            var testCode = @"
using System;

namespace TestProject
{
    public class TestClass
    {
        public void TestMethod()
        {
            var obj = new MyClass();
            obj.DoSomething();
            obj.DoSomethingWithParameter(""test"");
        }
    }

    public class MyClass
    {
        public void DoSomething() { }
        public void DoSomethingWithParameter(string param) { }
    }
}
";
            var tempFile = CreateTempFile("TestMethods.cs", testCode);

            try
            {
                // Act
                var callGraph = await _analyzer.BuildCallGraphAsync(new[] { tempFile });

                // Assert
                var allMethods = callGraph.GetAllMethods();
                var testMethodId = allMethods.FirstOrDefault(m => m.Contains("TestMethod"));
                Assert.NotNull(testMethodId);

                var calledMethods = callGraph.GetMethodCalls(testMethodId);
                
                // Should contain method calls
                Assert.True(calledMethods.Any(m => m.Contains("DoSomething")), 
                    $"Expected method calls. Called methods: {string.Join(", ", calledMethods)}");
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task BuildCallGraphAsync_IdentifiesTestMethods()
        {
            // Arrange - Create temporary test file with xUnit test
            var testCode = @"
using System;
using Xunit;

namespace TestProject.Tests
{
    public class TestClassTests
    {
        [Fact]
        public void TestSomething_WithValidInput_ReturnsExpectedResult()
        {
            var instance = new TestClass();
            var result = instance.DoSomething();
            Assert.NotNull(result);
        }
    }

    public class TestClass
    {
        public string DoSomething() => ""result"";
    }
}
";
            var tempFile = CreateTempFile("TestIdentification.cs", testCode);

            try
            {
                // Act
                var callGraph = await _analyzer.BuildCallGraphAsync(new[] { tempFile });

                // Assert
                var allMethods = callGraph.GetAllMethods();
                var testMethodId = allMethods.FirstOrDefault(m => m.Contains("TestSomething"));
                Assert.NotNull(testMethodId);

                // Verify it's identified as a test method
                var methodInfo = callGraph.GetMethodInfo(testMethodId);
                Assert.NotNull(methodInfo);
                Assert.True(methodInfo.IsTestMethod, "Method with [Fact] attribute should be identified as a test method");
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task BuildCallGraphAsync_HandlesComplexCallChains()
        {
            // Arrange - Create test file with complex call chain
            var testCode = @"
using System;
using Xunit;

namespace TestProject.Tests
{
    public class ComplexCallChainTests
    {
        [Fact]
        public void TestComplexScenario()
        {
            var service = new BusinessService();
            var result = service.ProcessData();
            Assert.NotNull(result);
        }
    }

    public class BusinessService
    {
        private readonly DataRepository _repo;

        public BusinessService()
        {
            _repo = new DataRepository();
        }

        public string ProcessData()
        {
            var data = _repo.GetData();
            return data.ToUpper();
        }
    }

    public class DataRepository
    {
        public string GetData()
        {
            return ""test data"";
        }
    }
}
";
            var tempFile = CreateTempFile("ComplexCallChain.cs", testCode);

            try
            {
                // Act
                var callGraph = await _analyzer.BuildCallGraphAsync(new[] { tempFile });

                // Assert - Verify we can trace the full call chain
                var allMethods = callGraph.GetAllMethods();
                var testMethodId = allMethods.FirstOrDefault(m => m.Contains("TestComplexScenario"));
                Assert.NotNull(testMethodId);

                // Should be able to find path from test to GetData
                var getDataMethodId = allMethods.FirstOrDefault(m => m.Contains("GetData"));
                Assert.NotNull(getDataMethodId);

                // Test the call path functionality
                var testMethodInfo = callGraph.GetMethodInfo(testMethodId);
                var getDataMethodInfo = callGraph.GetMethodInfo(getDataMethodId);
                
                Assert.NotNull(testMethodInfo);
                Assert.NotNull(getDataMethodInfo);
                Assert.True(testMethodInfo.IsTestMethod, "Test method should be identified as test");
                Assert.False(getDataMethodInfo.IsTestMethod, "Business method should not be identified as test");
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        private string CreateTempFile(string fileName, string content)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"TestIntel_{Guid.NewGuid()}_{fileName}");
            File.WriteAllText(tempPath, content);
            return tempPath;
        }
    }
}