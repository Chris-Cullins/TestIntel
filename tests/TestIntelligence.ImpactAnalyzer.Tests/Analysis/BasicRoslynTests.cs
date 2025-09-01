using System;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.ImpactAnalyzer.Analysis;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Analysis
{
    public class BasicRoslynTests : IDisposable
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly string _tempDirectory;

        public BasicRoslynTests()
        {
            _loggerFactory = Substitute.For<ILoggerFactory>();
            _loggerFactory.CreateLogger<RoslynAnalyzer>().Returns(Substitute.For<ILogger<RoslynAnalyzer>>());
            _loggerFactory.CreateLogger<CompilationManager>().Returns(Substitute.For<ILogger<CompilationManager>>());
            _loggerFactory.CreateLogger<SymbolResolutionEngine>().Returns(Substitute.For<ILogger<SymbolResolutionEngine>>());
            _loggerFactory.CreateLogger<SolutionParser>().Returns(Substitute.For<ILogger<SolutionParser>>());
            _loggerFactory.CreateLogger<ProjectParser>().Returns(Substitute.For<ILogger<ProjectParser>>());
            _loggerFactory.CreateLogger<DependencyGraphBuilder>().Returns(Substitute.For<ILogger<DependencyGraphBuilder>>());
            _loggerFactory.CreateLogger<SolutionWorkspaceBuilder>().Returns(Substitute.For<ILogger<SolutionWorkspaceBuilder>>());
            _loggerFactory.CreateLogger<CallGraphBuilderV2>().Returns(Substitute.For<ILogger<CallGraphBuilderV2>>());
            
            _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, true);
        }

        [Fact]
        public void RoslynAnalyzer_CanBeCreated()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzer>();

            var analyzer = new RoslynAnalyzer(logger, _loggerFactory);

            analyzer.Should().NotBeNull();
            analyzer.Dispose();
        }

        [Fact]
        public void RoslynAnalyzer_Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            var action = () => new RoslynAnalyzer(null!, _loggerFactory);

            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void RoslynAnalyzer_Constructor_WithNullLoggerFactory_ThrowsArgumentNullException()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzer>();

            var action = () => new RoslynAnalyzer(logger, null!);

            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void RoslynAnalyzer_Dispose_DoesNotThrow()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzer>();
            var analyzer = new RoslynAnalyzer(logger, _loggerFactory);

            analyzer.Dispose();

            // Should not throw
        }

        [Fact]
        public void RoslynAnalyzer_DoubleDispose_DoesNotThrow()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzer>();
            var analyzer = new RoslynAnalyzer(logger, _loggerFactory);

            analyzer.Dispose();
            analyzer.Dispose();

            // Should not throw
        }

        [Fact]
        public async void ExtractMethodsFromFileAsync_WithEmptyFile_ReturnsEmpty()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzer>();
            var analyzer = new RoslynAnalyzer(logger, _loggerFactory);
            var emptyFile = CreateTempFile("empty.cs", "");

            try
            {
                var result = await analyzer.ExtractMethodsFromFileAsync(emptyFile);
                result.Should().BeEmpty();
            }
            finally
            {
                analyzer.Dispose();
            }
        }

        [Fact]
        public async void ExtractMethodsFromFileAsync_WithValidClass_ExtractsMethods()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzer>();
            var analyzer = new RoslynAnalyzer(logger, _loggerFactory);
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

            try
            {
                var result = await analyzer.ExtractMethodsFromFileAsync(filePath);
                result.Should().NotBeEmpty();
                result.Should().Contain(m => m.Name == "TestMethod");
            }
            finally
            {
                analyzer.Dispose();
            }
        }

        [Fact]
        public async void GetSemanticModelAsync_WithValidFile_ReturnsSemanticModel()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzer>();
            var analyzer = new RoslynAnalyzer(logger, _loggerFactory);
            var sourceCode = @"
namespace TestNamespace
{
    public class TestClass { }
}";
            var filePath = CreateTempFile("test.cs", sourceCode);

            try
            {
                var result = await analyzer.GetSemanticModelAsync(filePath);
                result.Should().NotBeNull();
                result.SyntaxTree.Should().NotBeNull();
            }
            finally
            {
                analyzer.Dispose();
            }
        }

        [Fact]
        public async void BuildCallGraphAsync_WithEmptyFiles_ReturnsEmptyGraph()
        {
            var logger = _loggerFactory.CreateLogger<RoslynAnalyzer>();
            var analyzer = new RoslynAnalyzer(logger, _loggerFactory);

            try
            {
                var result = await analyzer.BuildCallGraphAsync(Array.Empty<string>());
                result.Should().NotBeNull();
                result.GetAllMethods().Should().BeEmpty();
            }
            finally
            {
                analyzer.Dispose();
            }
        }

        private string CreateTempFile(string fileName, string content)
        {
            var filePath = Path.Combine(_tempDirectory, fileName);
            File.WriteAllText(filePath, content);
            return filePath;
        }
    }
}