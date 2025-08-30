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
        private readonly RoslynAnalyzer _analyzer;
        private readonly ILogger<RoslynAnalyzer> _logger;
        private readonly string _tempDirectory;

        public RoslynAnalyzerTests()
        {
            _logger = Substitute.For<ILogger<RoslynAnalyzer>>();
            _analyzer = new RoslynAnalyzer(_logger);
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
        [InlineData("TestType", null, "/path/to/file.cs")]
        [InlineData("TestType", "TestNamespace", null)]
        public void Constructor_WithNullParameter_ShouldThrowArgumentNullException(string? typeName, string? @namespace, string? filePath)
        {
            var action = () => new TypeUsageInfo(typeName!, @namespace!, filePath!, 5, TypeUsageContext.Declaration);

            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void FullTypeName_WithNamespace_ShouldReturnQualifiedName()
        {
            var typeUsage = new TypeUsageInfo("TestType", "TestNamespace", "/path/to/file.cs", 5, TypeUsageContext.Declaration);

            typeUsage.FullTypeName.Should().Be("TestNamespace.TestType");
        }

        [Fact]
        public void FullTypeName_WithEmptyNamespace_ShouldReturnTypeName()
        {
            var typeUsage = new TypeUsageInfo("TestType", string.Empty, "/path/to/file.cs", 5, TypeUsageContext.Declaration);

            typeUsage.FullTypeName.Should().Be("TestType");
        }

        [Fact]
        public void ToString_ShouldReturnFormattedString()
        {
            var typeUsage = new TypeUsageInfo("TestType", "TestNamespace", "/path/to/file.cs", 5, TypeUsageContext.Declaration);

            var result = typeUsage.ToString();

            result.Should().Be("Declaration: TestNamespace.TestType at file.cs:5");
        }
    }
}