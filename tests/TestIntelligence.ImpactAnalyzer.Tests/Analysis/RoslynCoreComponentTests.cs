using System;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.ImpactAnalyzer.Analysis;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Analysis
{
    public class RoslynCoreComponentTests : IDisposable
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly string _tempDirectory;

        public RoslynCoreComponentTests()
        {
            _loggerFactory = Substitute.For<ILoggerFactory>();
            SetupLoggers();
            
            _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, true);
        }

        [Fact]
        public void RoslynAnalyzerV2_Constructor_WithValidParameters_ShouldCreateInstance()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzerV2>();

            var analyzer = new RoslynAnalyzerV2(logger, _loggerFactory);

            analyzer.Should().NotBeNull();
        }

        [Fact]
        public void RoslynAnalyzerV2_Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            var action = () => new RoslynAnalyzerV2(null!, _loggerFactory);

            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void RoslynAnalyzerV2_Constructor_WithNullLoggerFactory_ShouldThrowArgumentNullException()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzerV2>();

            var action = () => new RoslynAnalyzerV2(logger, null!);

            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void RoslynAnalyzerV2_Dispose_ShouldNotThrow()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzerV2>();
            var analyzer = new RoslynAnalyzerV2(logger, _loggerFactory);

            var action = () => analyzer.Dispose();

            action.Should().NotThrow();
        }

        [Fact]
        public void RoslynAnalyzerV2_DoubleDispose_ShouldNotThrow()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzerV2>();
            var analyzer = new RoslynAnalyzerV2(logger, _loggerFactory);

            analyzer.Dispose();
            var action = () => analyzer.Dispose();

            action.Should().NotThrow();
        }

        [Fact]
        public async void GetAffectedMethodsAsync_WithEmptyInputs_ShouldReturnEmpty()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzerV2>();
            var analyzer = new RoslynAnalyzerV2(logger, _loggerFactory);

            var result = await analyzer.GetAffectedMethodsAsync(
                Array.Empty<string>(),
                Array.Empty<string>()
            );

            result.Should().BeEmpty();
            analyzer.Dispose();
        }

        [Fact]
        public async void FindTestsExercisingMethodAsync_WithEmptyInputs_ShouldReturnEmpty()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzerV2>();
            var analyzer = new RoslynAnalyzerV2(logger, _loggerFactory);

            var result = await analyzer.FindTestsExercisingMethodAsync(
                "NonExistentMethod",
                Array.Empty<string>()
            );

            result.Should().BeEmpty();
        }

        [Fact]
        public async void AnalyzeTypeUsageAsync_WithEmptyFiles_ShouldReturnEmpty()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzerV2>();
            var analyzer = new RoslynAnalyzerV2(logger, _loggerFactory);

            var result = await analyzer.AnalyzeTypeUsageAsync(Array.Empty<string>());

            result.Should().BeEmpty();
        }

        [Fact]
        public async void ExtractMethodsFromFileAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzerV2>();
            var analyzer = new RoslynAnalyzerV2(logger, _loggerFactory);
            var nonExistentFile = Path.Combine(_tempDirectory, "nonexistent.cs");

            var action = async () => await analyzer.ExtractMethodsFromFileAsync(nonExistentFile);

            await action.Should().ThrowAsync<FileNotFoundException>();
        }

        [Fact]
        public async void ExtractMethodsFromFileAsync_WithEmptyFile_ShouldReturnEmpty()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzerV2>();
            var analyzer = new RoslynAnalyzerV2(logger, _loggerFactory);
            var emptyFile = CreateTempFile("empty.cs", "");

            var result = await analyzer.ExtractMethodsFromFileAsync(emptyFile);

            result.Should().BeEmpty();
        }

        [Fact]
        public async void ExtractMethodsFromFileAsync_WithValidCSharpFile_ShouldExtractMethods()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzerV2>();
            var analyzer = new RoslynAnalyzerV2(logger, _loggerFactory);
            var sourceCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod()
        {
        }
    }
}";
            var filePath = CreateTempFile("test.cs", sourceCode);

            var result = await analyzer.ExtractMethodsFromFileAsync(filePath);

            result.Should().NotBeEmpty();
            result.Should().Contain(m => m.Name == "TestMethod");
        }

        [Fact]
        public async void BuildCallGraphAsync_WithEmptyFiles_ShouldReturnEmptyCallGraph()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzerV2>();
            var analyzer = new RoslynAnalyzerV2(logger, _loggerFactory);

            var result = await analyzer.BuildCallGraphAsync(Array.Empty<string>());

            result.Should().NotBeNull();
            result.GetAllMethods().Should().BeEmpty();
        }

        [Fact]
        public async void GetSemanticModelAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzerV2>();
            var analyzer = new RoslynAnalyzerV2(logger, _loggerFactory);
            var nonExistentFile = Path.Combine(_tempDirectory, "nonexistent.cs");

            var action = async () => await analyzer.GetSemanticModelAsync(nonExistentFile);

            await action.Should().ThrowAsync<FileNotFoundException>();
        }

        [Fact]
        public async void GetSemanticModelAsync_WithValidFile_ShouldReturnSemanticModel()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzerV2>();
            var analyzer = new RoslynAnalyzerV2(logger, _loggerFactory);
            var sourceCode = @"
namespace TestNamespace
{
    public class TestClass { }
}";
            var filePath = CreateTempFile("test.cs", sourceCode);

            var result = await analyzer.GetSemanticModelAsync(filePath);

            result.Should().NotBeNull();
            result.SyntaxTree.Should().NotBeNull();
        }

        private string CreateTempFile(string fileName, string content)
        {
            var filePath = Path.Combine(_tempDirectory, fileName);
            File.WriteAllText(filePath, content);
            return filePath;
        }

        private void SetupLoggers()
        {
            _loggerFactory.CreateLogger<RoslynAnalyzerV2>().Returns(Substitute.For<ILogger<RoslynAnalyzerV2>>());
            _loggerFactory.CreateLogger<CompilationManager>().Returns(Substitute.For<ILogger<CompilationManager>>());
            _loggerFactory.CreateLogger<SymbolResolutionEngine>().Returns(Substitute.For<ILogger<SymbolResolutionEngine>>());
            _loggerFactory.CreateLogger<SolutionParser>().Returns(Substitute.For<ILogger<SolutionParser>>());
            _loggerFactory.CreateLogger<ProjectParser>().Returns(Substitute.For<ILogger<ProjectParser>>());
            _loggerFactory.CreateLogger<DependencyGraphBuilder>().Returns(Substitute.For<ILogger<DependencyGraphBuilder>>());
            _loggerFactory.CreateLogger<SolutionWorkspaceBuilder>().Returns(Substitute.For<ILogger<SolutionWorkspaceBuilder>>());
            _loggerFactory.CreateLogger<CallGraphBuilderV2>().Returns(Substitute.For<ILogger<CallGraphBuilderV2>>());
        }
    }
}