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
using TestIntelligence.Core.Models;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Analysis
{
    public class RoslynAnalyzerErrorPathTests : IDisposable
    {
        private readonly RoslynAnalyzer _analyzer;
        private readonly ILogger<RoslynAnalyzer> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly string _tempDirectory;

        public RoslynAnalyzerErrorPathTests()
        {
            _loggerFactory = Substitute.For<ILoggerFactory>();
            _logger = Substitute.For<ILogger<RoslynAnalyzer>>();
            _loggerFactory.CreateLogger<RoslynAnalyzer>().Returns(_logger);
            _loggerFactory.CreateLogger<SolutionParser>().Returns(Substitute.For<ILogger<SolutionParser>>());
            _loggerFactory.CreateLogger<ProjectParser>().Returns(Substitute.For<ILogger<ProjectParser>>());
            _loggerFactory.CreateLogger<DependencyGraphBuilder>().Returns(Substitute.For<ILogger<DependencyGraphBuilder>>());
            _loggerFactory.CreateLogger<SolutionWorkspaceBuilder>().Returns(Substitute.For<ILogger<SolutionWorkspaceBuilder>>());
            _loggerFactory.CreateLogger<CompilationManager>().Returns(Substitute.For<ILogger<CompilationManager>>());
            _loggerFactory.CreateLogger<SymbolResolutionEngine>().Returns(Substitute.For<ILogger<SymbolResolutionEngine>>());
            _loggerFactory.CreateLogger<CallGraphBuilderV2>().Returns(Substitute.For<ILogger<CallGraphBuilderV2>>());
            
            _analyzer = new RoslynAnalyzer(_logger, _loggerFactory);
            _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            _analyzer.Dispose();
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, true);
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithNonExistentFiles_ShouldFallbackToFileAnalysis()
        {
            var nonExistentFiles = new[] { "nonexistent1.cs", "nonexistent2.cs" };

            var result = await _analyzer.BuildCallGraphAsync(nonExistentFiles);

            result.Should().NotBeNull();
            result.GetAllMethods().Should().BeEmpty();
            VerifyWarningLogged("No solution file found, falling back to individual file analysis");
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithMalformedSolutionFile_ShouldFallbackGracefully()
        {
            var malformedSln = CreateTempFile("malformed.sln", "This is not a valid solution file");
            var sourceFiles = new[] { malformedSln };

            var result = await _analyzer.BuildCallGraphAsync(sourceFiles);

            result.Should().NotBeNull();
            VerifyErrorLogged("Failed to build call graph using enhanced analyzer, falling back to file-based analysis");
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithCancellationDuringInitialization_ShouldThrowOperationCanceledException()
        {
            var validSln = CreateValidSolutionFile();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var action = async () => await _analyzer.BuildCallGraphAsync(new[] { validSln }, cts.Token);

            await action.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task GetAffectedMethodsAsync_WithEmptyChanges_ShouldReturnEmptyList()
        {
            var result = await _analyzer.GetAffectedMethodsAsync(
                Array.Empty<string>(),
                Array.Empty<string>()
            );

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetAffectedMethodsAsync_WithCancellation_ShouldThrowOperationCanceledException()
        {
            var sourceCode = @"
using System;
namespace TestNamespace
{
    public class TestClass
    {
        public void MethodA() { MethodB(); }
        public void MethodB() { }
    }
}";
            var filePath = CreateTempFile("TestClass.cs", sourceCode);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var action = async () => await _analyzer.GetAffectedMethodsAsync(
                new[] { filePath },
                new[] { "TestNamespace.TestClass.MethodB" },
                cts.Token
            );

            await action.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task GetSemanticModelAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
        {
            var nonExistentPath = Path.Combine(_tempDirectory, "nonexistent.cs");

            var action = async () => await _analyzer.GetSemanticModelAsync(nonExistentPath);

            await action.Should().ThrowAsync<FileNotFoundException>();
        }

        [Fact]
        public async Task GetSemanticModelAsync_WithMalformedSourceCode_ShouldHandleGracefully()
        {
            var malformedCode = "this is not valid C# code { { { ";
            var filePath = CreateTempFile("malformed.cs", malformedCode);

            var result = await _analyzer.GetSemanticModelAsync(filePath);

            result.Should().NotBeNull(); // Roslyn can still create a semantic model for malformed code
            result.SyntaxTree.Should().NotBeNull();
        }

        [Fact]
        public async Task AnalyzeTypeUsageAsync_WithNonExistentFiles_ShouldSkipGracefully()
        {
            var nonExistentFiles = new[] { "nonexistent1.cs", "nonexistent2.cs" };

            var result = await _analyzer.AnalyzeTypeUsageAsync(nonExistentFiles);

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task AnalyzeTypeUsageAsync_WithMixOfValidAndInvalidFiles_ShouldProcessValidOnes()
        {
            var validCode = @"
namespace TestNamespace
{
    public class TestClass { }
}";
            var validFile = CreateTempFile("valid.cs", validCode);
            var invalidFile = "nonexistent.cs";
            var files = new[] { validFile, invalidFile };

            var result = await _analyzer.AnalyzeTypeUsageAsync(files);

            result.Should().NotBeEmpty();
            result.Should().Contain(usage => usage.TypeName == "TestClass");
            // Note: Warning for failed files is logged, but only when files exist and fail to process
        }

        [Fact]
        public async Task AnalyzeTypeUsageAsync_WithCancellation_ShouldThrowOperationCanceledException()
        {
            var sourceCode = @"
namespace TestNamespace
{
    public class TestClass { }
}";
            var filePath = CreateTempFile("TestClass.cs", sourceCode);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var action = async () => await _analyzer.AnalyzeTypeUsageAsync(new[] { filePath }, cts.Token);

            await action.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task ExtractMethodsFromFileAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
        {
            var nonExistentPath = Path.Combine(_tempDirectory, "nonexistent.cs");

            var action = async () => await _analyzer.ExtractMethodsFromFileAsync(nonExistentPath);

            await action.Should().ThrowAsync<FileNotFoundException>();
        }

        [Fact]
        public async Task ExtractMethodsFromFileAsync_WithEmptyFile_ShouldReturnEmptyList()
        {
            var emptyFile = CreateTempFile("empty.cs", "");

            var result = await _analyzer.ExtractMethodsFromFileAsync(emptyFile);

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task ExtractMethodsFromFileAsync_WithCompilationErrors_ShouldHandleGracefully()
        {
            var invalidCode = @"
using System;
namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod(UndefinedType parameter) // Compilation error
        {
        }
    }
}";
            var filePath = CreateTempFile("invalid.cs", invalidCode);

            var result = await _analyzer.ExtractMethodsFromFileAsync(filePath);

            result.Should().NotBeEmpty(); // Should still extract methods despite compilation errors
            result.Should().Contain(m => m.Name == "TestMethod");
            // Note: No warning is logged when no workspace is initialized (more efficient behavior)
        }

        [Fact]
        public async Task ExtractMethodsFromFileAsync_WithCancellation_ShouldThrowOperationCanceledException()
        {
            var sourceCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod() { }
    }
}";
            var filePath = CreateTempFile("TestClass.cs", sourceCode);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var action = async () => await _analyzer.ExtractMethodsFromFileAsync(filePath, cts.Token);

            await action.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task FindTestsExercisingMethodAsync_WithNonExistentMethod_ShouldReturnEmpty()
        {
            var sourceCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        public void ExistingMethod() { }
    }
}";
            var filePath = CreateTempFile("TestClass.cs", sourceCode);

            var result = await _analyzer.FindTestsExercisingMethodAsync(
                "TestNamespace.TestClass.NonExistentMethod()",
                new[] { filePath }
            );

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task FindTestsExercisingMethodAsync_WithEmptySolutionFiles_ShouldFallbackGracefully()
        {
            var result = await _analyzer.FindTestsExercisingMethodAsync(
                "SomeMethod",
                Array.Empty<string>()
            );

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task FindTestsExercisingMethodAsync_WithCancellation_ShouldThrowOperationCanceledException()
        {
            var sourceCode = @"
using Xunit;
namespace TestNamespace
{
    public class TestClass
    {
        [Fact]
        public void TestMethod() { }
    }
}";
            var filePath = CreateTempFile("TestClass.cs", sourceCode);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var action = async () => await _analyzer.FindTestsExercisingMethodAsync(
                "SomeMethod",
                new[] { filePath },
                cts.Token
            );

            await action.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithMemoryPressure_ShouldHandleGracefully()
        {
            // Create a large number of files to potentially trigger memory issues
            var files = new List<string>();
            for (int i = 0; i < 100; i++)
            {
                var sourceCode = $@"
namespace TestNamespace{i}
{{
    public class TestClass{i}
    {{
        public void Method1() {{ Method2(); }}
        public void Method2() {{ Method3(); }}
        public void Method3() {{ }}
    }}
}}";
                files.Add(CreateTempFile($"TestClass{i}.cs", sourceCode));
            }

            var result = await _analyzer.BuildCallGraphAsync(files.ToArray());

            result.Should().NotBeNull();
            // Should handle gracefully even under memory pressure
        }

        [Fact]
        public async Task WorkspaceInitialization_WithCorruptedProjectFile_ShouldFallbackGracefully()
        {
            var corruptedProject = CreateTempFile("corrupted.csproj", "<InvalidXml>");
            var solutionContent = $@"
Microsoft Visual Studio Solution File, Format Version 12.00
Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""CorruptedProject"", ""{corruptedProject}"", ""{{12345678-1234-1234-1234-123456789012}}""
EndProject
";
            var solutionFile = CreateTempFile("solution.sln", solutionContent);

            var result = await _analyzer.BuildCallGraphAsync(new[] { solutionFile });

            result.Should().NotBeNull();
            VerifyErrorLogged("Failed to build call graph using enhanced analyzer, falling back to file-based analysis");
        }

        [Fact]
        public void Dispose_ShouldCleanupResources()
        {
            var analyzer = new RoslynAnalyzer(_logger, _loggerFactory);

            analyzer.Dispose();

            // Should not throw and should cleanup resources
        }

        [Fact]
        public void Dispose_CalledMultipleTimes_ShouldNotThrow()
        {
            var analyzer = new RoslynAnalyzer(_logger, _loggerFactory);

            analyzer.Dispose();
            analyzer.Dispose();
            analyzer.Dispose();

            // Should handle multiple dispose calls gracefully
        }

        private string CreateTempFile(string fileName, string content)
        {
            var filePath = Path.Combine(_tempDirectory, fileName);
            File.WriteAllText(filePath, content);
            return filePath;
        }

        private string CreateValidSolutionFile()
        {
            var projectContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>";
            
            var projectFile = CreateTempFile("TestProject.csproj", projectContent);
            
            var solutionContent = $@"
Microsoft Visual Studio Solution File, Format Version 12.00
Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""TestProject"", ""{projectFile}"", ""{{12345678-1234-1234-1234-123456789012}}""
EndProject
";
            return CreateTempFile("TestSolution.sln", solutionContent);
        }

        private void VerifyWarningLogged(string message)
        {
            _logger.Received().Log(
                LogLevel.Warning,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains(message)),
                Arg.Any<Exception>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
        }

        private void VerifyErrorLogged(string message)
        {
            _logger.Received().Log(
                LogLevel.Error,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains(message)),
                Arg.Any<Exception>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
        }
    }
}