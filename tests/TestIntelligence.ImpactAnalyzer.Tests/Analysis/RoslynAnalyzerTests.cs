using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.ImpactAnalyzer.Analysis;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Analysis
{
    public class RoslynAnalyzerTests : IDisposable
    {
        private readonly RoslynAnalyzerV2 _analyzer;
        private readonly ILogger<RoslynAnalyzerV2> _logger;
        private readonly string _tempDirectory;

        public RoslynAnalyzerTests()
        {
            var loggerFactory = Substitute.For<ILoggerFactory>();
            _logger = Substitute.For<ILogger<RoslynAnalyzerV2>>();
            loggerFactory.CreateLogger<RoslynAnalyzerV2>().Returns(_logger);
            _analyzer = new RoslynAnalyzerV2(_logger, loggerFactory);
            _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, true);
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithSimpleClass_ShouldReturnCallGraph()
        {
            var sourceCode = @"
using System;

namespace TestNamespace
{
    public class TestClass
    {
        public void MethodA()
        {
            MethodB();
        }

        public void MethodB()
        {
            Console.WriteLine(""Hello"");
        }
    }
}";

            var filePath = CreateTempFile("TestClass.cs", sourceCode);
            var result = await _analyzer.BuildCallGraphAsync(new[] { filePath });

            result.Should().NotBeNull();
            result.GetAllMethods().Should().NotBeEmpty();
        }

        [Fact]
        public async Task GetAffectedMethodsAsync_WithMethodChanges_ShouldReturnTransitiveDependents()
        {
            var sourceCode = @"
using System;

namespace TestNamespace
{
    public class TestClass
    {
        public void MethodA()
        {
            MethodB();
        }

        public void MethodB()
        {
            MethodC();
        }

        public void MethodC()
        {
            Console.WriteLine(""Hello"");
        }
    }
}";

            var filePath = CreateTempFile("TestClass.cs", sourceCode);
            var result = await _analyzer.GetAffectedMethodsAsync(
                new[] { filePath }, 
                new[] { "TestNamespace.TestClass.MethodC" }
            );

            result.Should().NotBeEmpty();
            result.Should().Contain("TestNamespace.TestClass.MethodC");
        }

        [Fact]
        public async Task GetSemanticModelAsync_WithValidFile_ShouldReturnSemanticModel()
        {
            var sourceCode = @"
using System;

namespace TestNamespace
{
    public class TestClass
    {
        public string TestMethod()
        {
            return ""Hello World"";
        }
    }
}";

            var filePath = CreateTempFile("TestClass.cs", sourceCode);
            var result = await _analyzer.GetSemanticModelAsync(filePath);

            result.Should().NotBeNull();
            result.SyntaxTree.Should().NotBeNull();
        }

        [Fact]
        public async Task GetSemanticModelAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
        {
            var nonExistentPath = Path.Combine(_tempDirectory, "nonexistent.cs");

            var action = async () => await _analyzer.GetSemanticModelAsync(nonExistentPath);

            await action.Should().ThrowAsync<FileNotFoundException>();
        }

        [Fact]
        public async Task AnalyzeTypeUsageAsync_WithMultipleTypes_ShouldReturnAllUsages()
        {
            var sourceCode = @"
using System;
using System.Collections.Generic;

namespace TestNamespace
{
    public class TestClass
    {
        public List<string> Items { get; set; }
        
        public void ProcessItems()
        {
            var processor = new ItemProcessor();
            processor.Process(Items);
        }
    }

    public class ItemProcessor
    {
        public void Process(List<string> items)
        {
            Console.WriteLine($""Processing {items.Count} items"");
        }
    }
}";

            var filePath = CreateTempFile("TestTypes.cs", sourceCode);
            var result = await _analyzer.AnalyzeTypeUsageAsync(new[] { filePath });

            result.Should().NotBeEmpty();
            result.Should().Contain(usage => usage.TypeName == "TestClass");
            result.Should().Contain(usage => usage.TypeName == "ItemProcessor");
        }

        [Fact]
        public async Task ExtractMethodsFromFileAsync_WithMultipleMethods_ShouldReturnAllMethods()
        {
            var sourceCode = @"
using System;

namespace TestNamespace
{
    public class TestClass
    {
        public void Method1()
        {
            Console.WriteLine(""Method1"");
        }

        public string Method2(int parameter)
        {
            return parameter.ToString();
        }

        private void Method3()
        {
            Method1();
        }
    }
}";

            var filePath = CreateTempFile("TestClass.cs", sourceCode);
            var result = await _analyzer.ExtractMethodsFromFileAsync(filePath);

            result.Should().HaveCount(3);
            result.Should().Contain(m => m.Name == "Method1");
            result.Should().Contain(m => m.Name == "Method2");
            result.Should().Contain(m => m.Name == "Method3");
        }

        [Fact]
        public async Task ExtractMethodsFromFileAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
        {
            var nonExistentPath = Path.Combine(_tempDirectory, "nonexistent.cs");

            var action = async () => await _analyzer.ExtractMethodsFromFileAsync(nonExistentPath);

            await action.Should().ThrowAsync<FileNotFoundException>();
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithCancellationToken_ShouldRespectCancellation()
        {
            var sourceCode = @"
using System;

namespace TestNamespace
{
    public class TestClass
    {
        public void Method1() { }
    }
}";

            var filePath = CreateTempFile("TestClass.cs", sourceCode);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var action = async () => await _analyzer.BuildCallGraphAsync(new[] { filePath }, cts.Token);

            await action.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task AnalyzeTypeUsageAsync_WithEmptyFileArray_ShouldReturnEmptyList()
        {
            var result = await _analyzer.AnalyzeTypeUsageAsync(Array.Empty<string>());

            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Theory]
        [InlineData("class")]
        [InlineData("interface")]
        [InlineData("struct")]
        [InlineData("enum")]
        public async Task AnalyzeTypeUsageAsync_WithDifferentTypeDeclarations_ShouldDetectAll(string typeKeyword)
        {
            var sourceCode = $@"
namespace TestNamespace
{{
    public {typeKeyword} TestType
    {{
        {(typeKeyword == "enum" ? "Value1, Value2" : "")}
    }}
}}";

            var filePath = CreateTempFile($"Test{typeKeyword}.cs", sourceCode);
            var result = await _analyzer.AnalyzeTypeUsageAsync(new[] { filePath });

            result.Should().Contain(usage => 
                usage.TypeName == "TestType" && 
                usage.Context == TypeUsageContext.Declaration);
        }

        [Fact]
        public async Task FindTestsExercisingMethodAsync_WithDirectTestCall_ShouldReturnTest()
        {
            var sourceCode = @"
using System;
using Xunit;

namespace TestNamespace
{
    public class ProductionClass
    {
        public void BusinessMethod()
        {
            Console.WriteLine(""Business logic"");
        }
    }

    public class ProductionTests
    {
        [Fact]
        public void BusinessMethod_ShouldWork()
        {
            var instance = new ProductionClass();
            instance.BusinessMethod();
        }
    }
}";

            var filePath = CreateTempFile("TestCase.cs", sourceCode);
            var result = await _analyzer.FindTestsExercisingMethodAsync(
                "TestNamespace.ProductionClass.BusinessMethod()", 
                new[] { filePath }
            );

            result.Should().HaveCount(1);
            result.First().TestMethodName.Should().Be("BusinessMethod_ShouldWork");
            result.First().TestClassName.Should().Be("ProductionTests");
            result.First().CallDepth.Should().Be(1);
            result.First().Confidence.Should().BeGreaterThan(0.8);
        }

        [Fact]
        public async Task FindTestsExercisingMethodAsync_WithIndirectTestCall_ShouldReturnTest()
        {
            var sourceCode = @"
using System;
using Xunit;

namespace TestNamespace
{
    public class ProductionClass
    {
        public void LowLevelMethod()
        {
            Console.WriteLine(""Low level logic"");
        }

        public void HighLevelMethod()
        {
            LowLevelMethod();
        }
    }

    public class ProductionTests
    {
        [Fact]
        public void HighLevelMethod_ShouldWork()
        {
            var instance = new ProductionClass();
            instance.HighLevelMethod();
        }
    }
}";

            var filePath = CreateTempFile("IndirectTestCase.cs", sourceCode);
            var result = await _analyzer.FindTestsExercisingMethodAsync(
                "TestNamespace.ProductionClass.LowLevelMethod()", 
                new[] { filePath }
            );

            result.Should().HaveCount(1);
            result.First().TestMethodName.Should().Be("HighLevelMethod_ShouldWork");
            result.First().CallDepth.Should().BeGreaterThan(0);
            result.First().CallDepth.Should().Be(2); // LowLevelMethod -> HighLevelMethod -> Test
        }

        [Fact]
        public async Task FindTestsExercisingMethodAsync_WithNoTestCoverage_ShouldReturnEmpty()
        {
            var sourceCode = @"
using System;
using Xunit;

namespace TestNamespace
{
    public class ProductionClass
    {
        public void UntestedMethod()
        {
            Console.WriteLine(""No test coverage"");
        }
    }

    public class ProductionTests
    {
        [Fact]
        public void SomeOtherTest()
        {
            Console.WriteLine(""Testing something else"");
        }
    }
}";

            var filePath = CreateTempFile("NoTestCase.cs", sourceCode);
            var result = await _analyzer.FindTestsExercisingMethodAsync(
                "TestNamespace.ProductionClass.UntestedMethod()", 
                new[] { filePath }
            );

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task FindTestsExercisingMethodAsync_WithMultipleTests_ShouldReturnAll()
        {
            var sourceCode = @"
using System;
using Xunit;

namespace TestNamespace
{
    public class ProductionClass
    {
        public void SharedMethod()
        {
            Console.WriteLine(""Shared logic"");
        }
    }

    public class ProductionTests
    {
        [Fact]
        public void Test1()
        {
            var instance = new ProductionClass();
            instance.SharedMethod();
        }

        [Fact]
        public void Test2()
        {
            var instance = new ProductionClass();
            instance.SharedMethod();
        }
    }
}";

            var filePath = CreateTempFile("MultipleTestCase.cs", sourceCode);
            var result = await _analyzer.FindTestsExercisingMethodAsync(
                "TestNamespace.ProductionClass.SharedMethod()", 
                new[] { filePath }
            );

            result.Should().HaveCount(2);
            result.Should().Contain(r => r.TestMethodName == "Test1");
            result.Should().Contain(r => r.TestMethodName == "Test2");
        }

        private string CreateTempFile(string fileName, string content)
        {
            var filePath = Path.Combine(_tempDirectory, fileName);
            File.WriteAllText(filePath, content);
            return filePath;
        }
    }

    public class MethodCallGraphTests
    {
        [Fact]
        public void Constructor_WithNullCallGraph_ShouldThrowArgumentNullException()
        {
            var methodDefinitions = new Dictionary<string, MethodInfo>();

            var action = () => new MethodCallGraph(null!, methodDefinitions);

            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_WithNullMethodDefinitions_ShouldThrowArgumentNullException()
        {
            var callGraph = new Dictionary<string, HashSet<string>>();

            var action = () => new MethodCallGraph(callGraph, null!);

            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void GetMethodCalls_WithExistingMethod_ShouldReturnCalls()
        {
            var callGraph = new Dictionary<string, HashSet<string>>
            {
                ["Method1"] = new HashSet<string> { "Method2", "Method3" }
            };
            var methodDefinitions = new Dictionary<string, MethodInfo>();
            var graph = new MethodCallGraph(callGraph, methodDefinitions);

            var result = graph.GetMethodCalls("Method1");

            result.Should().Contain("Method2");
            result.Should().Contain("Method3");
            result.Should().HaveCount(2);
        }

        [Fact]
        public void GetMethodCalls_WithNonExistentMethod_ShouldReturnEmptySet()
        {
            var callGraph = new Dictionary<string, HashSet<string>>();
            var methodDefinitions = new Dictionary<string, MethodInfo>();
            var graph = new MethodCallGraph(callGraph, methodDefinitions);

            var result = graph.GetMethodCalls("NonExistentMethod");

            result.Should().BeEmpty();
        }

        [Fact]
        public void GetMethodDependents_WithMethodThatIsCalled_ShouldReturnCallers()
        {
            var callGraph = new Dictionary<string, HashSet<string>>
            {
                ["Method1"] = new HashSet<string> { "Method2" },
                ["Method3"] = new HashSet<string> { "Method2" }
            };
            var methodDefinitions = new Dictionary<string, MethodInfo>();
            var graph = new MethodCallGraph(callGraph, methodDefinitions);

            var result = graph.GetMethodDependents("Method2");

            result.Should().Contain("Method1");
            result.Should().Contain("Method3");
            result.Should().HaveCount(2);
        }

        [Fact]
        public void GetTransitiveDependents_WithDeepCallChain_ShouldReturnAllTransitiveCallers()
        {
            var callGraph = new Dictionary<string, HashSet<string>>
            {
                ["Method1"] = new HashSet<string> { "Method2" },
                ["Method2"] = new HashSet<string> { "Method3" },
                ["Method4"] = new HashSet<string> { "Method3" }
            };
            var methodDefinitions = new Dictionary<string, MethodInfo>();
            var graph = new MethodCallGraph(callGraph, methodDefinitions);

            var result = graph.GetTransitiveDependents("Method3");

            result.Should().Contain("Method2");
            result.Should().Contain("Method1");
            result.Should().Contain("Method4");
            result.Should().HaveCount(3);
        }

        [Fact]
        public void GetMethodInfo_WithExistingMethod_ShouldReturnMethodInfo()
        {
            var callGraph = new Dictionary<string, HashSet<string>>();
            var methodInfo = new MethodInfo("Method1", "TestMethod", "TestClass", "/path/to/file.cs", 10);
            var methodDefinitions = new Dictionary<string, MethodInfo>
            {
                ["Method1"] = methodInfo
            };
            var graph = new MethodCallGraph(callGraph, methodDefinitions);

            var result = graph.GetMethodInfo("Method1");

            result.Should().Be(methodInfo);
        }

        [Fact]
        public void GetMethodInfo_WithNonExistentMethod_ShouldReturnNull()
        {
            var callGraph = new Dictionary<string, HashSet<string>>();
            var methodDefinitions = new Dictionary<string, MethodInfo>();
            var graph = new MethodCallGraph(callGraph, methodDefinitions);

            var result = graph.GetMethodInfo("NonExistentMethod");

            result.Should().BeNull();
        }

        [Fact]
        public void GetTestMethodsExercisingMethod_WithDirectTestCall_ShouldReturnTestMethod()
        {
            var callGraph = new Dictionary<string, HashSet<string>>
            {
                ["TestMethod"] = new HashSet<string> { "ProductionMethod" }
            };
            var methodDefinitions = new Dictionary<string, MethodInfo>
            {
                ["TestMethod"] = new MethodInfo("TestMethod", "TestMethod", "TestClass", "/test.cs", 10, true),
                ["ProductionMethod"] = new MethodInfo("ProductionMethod", "ProductionMethod", "ProductionClass", "/prod.cs", 20, false)
            };
            var graph = new MethodCallGraph(callGraph, methodDefinitions);

            var result = graph.GetTestMethodsExercisingMethod("ProductionMethod");

            result.Should().Contain("TestMethod");
            result.Should().HaveCount(1);
        }

        [Fact]
        public void GetTestCoverageForMethod_WithDirectTestCall_ShouldReturnCoverageResult()
        {
            var callGraph = new Dictionary<string, HashSet<string>>
            {
                ["TestMethod"] = new HashSet<string> { "ProductionMethod" }
            };
            var methodDefinitions = new Dictionary<string, MethodInfo>
            {
                ["TestMethod"] = new MethodInfo("TestMethod", "TestMethod", "TestClass", "/test.cs", 10, true),
                ["ProductionMethod"] = new MethodInfo("ProductionMethod", "ProductionMethod", "ProductionClass", "/prod.cs", 20, false)
            };
            var graph = new MethodCallGraph(callGraph, methodDefinitions);

            var result = graph.GetTestCoverageForMethod("ProductionMethod");

            result.Should().HaveCount(1);
            result.First().TestMethodName.Should().Be("TestMethod");
            result.First().TestClassName.Should().Be("TestClass");
            result.First().CallDepth.Should().Be(1);
        }
    }

    public class MethodInfoTests
    {
        [Fact]
        public void Constructor_WithValidParameters_ShouldCreateInstance()
        {
            var methodInfo = new MethodInfo("id", "TestMethod", "TestClass", "/path/to/file.cs", 10);

            methodInfo.Id.Should().Be("id");
            methodInfo.Name.Should().Be("TestMethod");
            methodInfo.ContainingType.Should().Be("TestClass");
            methodInfo.FilePath.Should().Be("/path/to/file.cs");
            methodInfo.LineNumber.Should().Be(10);
        }

        [Theory]
        [InlineData(null, "TestMethod", "TestClass", "/path/to/file.cs")]
        [InlineData("id", null, "TestClass", "/path/to/file.cs")]
        [InlineData("id", "TestMethod", null, "/path/to/file.cs")]
        [InlineData("id", "TestMethod", "TestClass", null)]
        public void Constructor_WithNullParameter_ShouldThrowArgumentNullException(string? id, string? name, string? containingType, string? filePath)
        {
            var action = () => new MethodInfo(id!, name!, containingType!, filePath!, 10);

            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void ToString_ShouldReturnFormattedString()
        {
            var methodInfo = new MethodInfo("id", "TestMethod", "TestClass", "/path/to/file.cs", 10);

            var result = methodInfo.ToString();

            result.Should().Be("TestClass.TestMethod at file.cs:10");
        }
    }

    public class TypeUsageInfoTests
    {
        [Fact]
        public void Constructor_WithValidParameters_ShouldCreateInstance()
        {
            var typeUsage = new TypeUsageInfo("TestType", "TestNamespace", "/path/to/file.cs", 5, TypeUsageContext.Declaration);

            typeUsage.TypeName.Should().Be("TestType");
            typeUsage.Namespace.Should().Be("TestNamespace");
            typeUsage.FilePath.Should().Be("/path/to/file.cs");
            typeUsage.LineNumber.Should().Be(5);
            typeUsage.Context.Should().Be(TypeUsageContext.Declaration);
        }

        [Theory]
        [InlineData(null, "TestNamespace", "/path/to/file.cs")]
        [InlineData("TestType", "TestNamespace", null)]
        public void Constructor_WithNullParameter_ShouldThrowArgumentNullException(string? typeName, string? @namespace, string? filePath)
        {
            var action = () => new TypeUsageInfo(typeName!, @namespace!, filePath!, 5, TypeUsageContext.Declaration);

            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_WithNullNamespace_ShouldAllowNullAndConvertToEmpty()
        {
            var typeUsage = new TypeUsageInfo("TestType", null!, "/path/to/file.cs", 5, TypeUsageContext.Declaration);

            typeUsage.Namespace.Should().BeEmpty();
            typeUsage.TypeName.Should().Be("TestType");
        }

        [Fact]
        public void FullTypeName_WithNamespace_ShouldReturnQualifiedName()
        {
            var typeUsage = new TypeUsageInfo("TestType", "TestNamespace", "/path/to/file.cs", 5, TypeUsageContext.Declaration);

            var fullName = string.IsNullOrEmpty(typeUsage.Namespace) ? typeUsage.TypeName : $"{typeUsage.Namespace}.{typeUsage.TypeName}";
            fullName.Should().Be("TestNamespace.TestType");
        }

        [Fact]
        public void FullTypeName_WithEmptyNamespace_ShouldReturnTypeName()
        {
            var typeUsage = new TypeUsageInfo("TestType", string.Empty, "/path/to/file.cs", 5, TypeUsageContext.Declaration);

            var fullName = string.IsNullOrEmpty(typeUsage.Namespace) ? typeUsage.TypeName : $"{typeUsage.Namespace}.{typeUsage.TypeName}";
            fullName.Should().Be("TestType");
        }

        [Fact]
        public void ToString_ShouldReturnFormattedString()
        {
            var typeUsage = new TypeUsageInfo("TestType", "TestNamespace", "/path/to/file.cs", 5, TypeUsageContext.Declaration);

            var result = typeUsage.ToString();

            result.Should().Be("Declaration: TestNamespace.TestType at file.cs:5");
        }
    }

    public class TestCoverageResultTests
    {
        [Fact]
        public void Constructor_WithValidParameters_ShouldCreateInstance()
        {
            var callPath = new[] { "ProductionMethod", "TestMethod" };
            var result = new TestCoverageResult("TestMethod", "TestName", "TestClass", "/test.cs", callPath, 0.85);

            result.TestMethodId.Should().Be("TestMethod");
            result.TestMethodName.Should().Be("TestName");
            result.TestClassName.Should().Be("TestClass");
            result.TestFilePath.Should().Be("/test.cs");
            result.CallPath.Should().Equal(callPath);
            result.Confidence.Should().Be(0.85);
        }

        [Fact]
        public void CallDepth_WithTwoElementPath_ShouldReturnOne()
        {
            var callPath = new[] { "ProductionMethod", "TestMethod" };
            var result = new TestCoverageResult("TestMethod", "TestName", "TestClass", "/test.cs", callPath, 0.85);

            result.CallDepth.Should().Be(1);
        }

        [Fact]
        public void CallDepth_WithThreeElementPath_ShouldReturnTwo()
        {
            var callPath = new[] { "ProductionMethod", "IntermediateMethod", "TestMethod" };
            var result = new TestCoverageResult("TestMethod", "TestName", "TestClass", "/test.cs", callPath, 0.75);

            result.CallDepth.Should().Be(2);
        }

        [Fact]
        public void CallDepth_ShouldReturnCorrectCount()
        {
            var callPath = new[] { "Method1", "Method2", "Method3", "TestMethod" };
            var result = new TestCoverageResult("TestMethod", "TestName", "TestClass", "/test.cs", callPath, 0.65);

            result.CallDepth.Should().Be(3);
        }

        [Fact]
        public void ToString_ShouldReturnFormattedString()
        {
            var callPath = new[] { "ProductionClass.ProductionMethod", "TestClass.TestMethod" };
            var result = new TestCoverageResult("TestMethod", "TestName", "TestClass", "/test.cs", callPath, 0.85);

            var output = result.ToString();

            output.Should().Contain("[0.85]");
            output.Should().Contain("TestClass.TestName");
            output.Should().Contain("ProductionMethod");
            output.Should().Contain("TestMethod");
        }

        [Theory]
        [InlineData(null, "TestName", "TestClass", "/test.cs")]
        [InlineData("TestMethod", null, "TestClass", "/test.cs")]
        [InlineData("TestMethod", "TestName", null, "/test.cs")]
        [InlineData("TestMethod", "TestName", "TestClass", null)]
        public void Constructor_WithNullParameter_ShouldThrowArgumentNullException(string? testMethodId, string? testMethodName, string? testClassName, string? testFilePath)
        {
            var callPath = new[] { "ProductionMethod", "TestMethod" };
            var action = () => new TestCoverageResult(testMethodId!, testMethodName!, testClassName!, testFilePath!, callPath, 0.85);

            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_WithNullCallPath_ShouldThrowArgumentNullException()
        {
            var action = () => new TestCoverageResult("TestMethod", "TestName", "TestClass", "/test.cs", null!, 0.85);

            action.Should().Throw<ArgumentNullException>();
        }
    }
}