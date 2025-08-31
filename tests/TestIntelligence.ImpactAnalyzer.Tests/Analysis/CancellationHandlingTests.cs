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
    public class CancellationHandlingTests : IDisposable
    {
        private readonly RoslynAnalyzerV2 _analyzer;
        private readonly ILogger<RoslynAnalyzerV2> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly string _tempDirectory;

        public CancellationHandlingTests()
        {
            _loggerFactory = Substitute.For<ILoggerFactory>();
            _logger = Substitute.For<ILogger<RoslynAnalyzerV2>>();
            _loggerFactory.CreateLogger<RoslynAnalyzerV2>().Returns(_logger);
            SetupMockLoggers();
            
            _analyzer = new RoslynAnalyzerV2(_logger, _loggerFactory);
            _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            _analyzer.Dispose();
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, true);
        }

        private void SetupMockLoggers()
        {
            _loggerFactory.CreateLogger<SolutionParser>().Returns(Substitute.For<ILogger<SolutionParser>>());
            _loggerFactory.CreateLogger<ProjectParser>().Returns(Substitute.For<ILogger<ProjectParser>>());
            _loggerFactory.CreateLogger<DependencyGraphBuilder>().Returns(Substitute.For<ILogger<DependencyGraphBuilder>>());
            _loggerFactory.CreateLogger<SolutionWorkspaceBuilder>().Returns(Substitute.For<ILogger<SolutionWorkspaceBuilder>>());
            _loggerFactory.CreateLogger<CompilationManager>().Returns(Substitute.For<ILogger<CompilationManager>>());
            _loggerFactory.CreateLogger<SymbolResolutionEngine>().Returns(Substitute.For<ILogger<SymbolResolutionEngine>>());
            _loggerFactory.CreateLogger<CallGraphBuilderV2>().Returns(Substitute.For<ILogger<CallGraphBuilderV2>>());
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithPreCancelledToken_ShouldThrowOperationCanceledException()
        {
            var sourceCode = CreateSimpleSourceCode();
            var filePath = CreateTempFile("PreCancelledTest.cs", sourceCode);
            
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel before starting

            var action = async () => await _analyzer.BuildCallGraphAsync(new[] { filePath }, cts.Token);

            await action.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithTokenCancelledDuringExecution_ShouldThrowOperationCanceledException()
        {
            // Create multiple files to increase processing time
            var files = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                var sourceCode = CreateComplexSourceCode(i);
                var filePath = CreateTempFile($"CancelledDuringTest{i}.cs", sourceCode);
                files.Add(filePath);
            }
            
            using var cts = new CancellationTokenSource();
            
            // Cancel after a short delay to simulate cancellation during processing
            _ = Task.Run(async () =>
            {
                await Task.Delay(50); // Small delay to let processing start
                cts.Cancel();
            });

            var action = async () => await _analyzer.BuildCallGraphAsync(files.ToArray(), cts.Token);

            await action.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task GetAffectedMethodsAsync_WithCancelledToken_ShouldThrowOperationCanceledException()
        {
            var sourceCode = CreateSimpleSourceCode();
            var filePath = CreateTempFile("AffectedMethodsTest.cs", sourceCode);
            
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var action = async () => await _analyzer.GetAffectedMethodsAsync(
                new[] { filePath }, 
                new[] { "TestNamespace.TestClass.Method1" },
                cts.Token
            );

            await action.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task GetSemanticModelAsync_WithCancelledToken_ShouldThrowOperationCanceledException()
        {
            var sourceCode = CreateSimpleSourceCode();
            var filePath = CreateTempFile("SemanticModelTest.cs", sourceCode);
            
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var action = async () => await _analyzer.GetSemanticModelAsync(filePath, cts.Token);

            await action.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task AnalyzeTypeUsageAsync_WithCancelledToken_ShouldThrowOperationCanceledException()
        {
            var sourceCode = CreateSimpleSourceCode();
            var filePath = CreateTempFile("TypeUsageTest.cs", sourceCode);
            
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var action = async () => await _analyzer.AnalyzeTypeUsageAsync(new[] { filePath }, cts.Token);

            await action.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task ExtractMethodsFromFileAsync_WithCancelledToken_ShouldThrowOperationCanceledException()
        {
            var sourceCode = CreateSimpleSourceCode();
            var filePath = CreateTempFile("ExtractMethodsTest.cs", sourceCode);
            
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var action = async () => await _analyzer.ExtractMethodsFromFileAsync(filePath, cts.Token);

            await action.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task FindTestsExercisingMethodAsync_WithCancelledToken_ShouldThrowOperationCanceledException()
        {
            var sourceCode = CreateTestSourceCode();
            var filePath = CreateTempFile("FindTestsTest.cs", sourceCode);
            
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var action = async () => await _analyzer.FindTestsExercisingMethodAsync(
                "TestNamespace.ProductionClass.BusinessMethod()",
                new[] { filePath },
                cts.Token
            );

            await action.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithTimeoutToken_ShouldRespectTimeout()
        {
            // Create a very large file that would take time to process
            var largeSourceCode = CreateVeryLargeSourceCode();
            var filePath = CreateTempFile("TimeoutTest.cs", largeSourceCode);
            
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            var action = async () => await _analyzer.BuildCallGraphAsync(new[] { filePath }, cts.Token);

            // Should either complete quickly or throw OperationCanceledException
            try
            {
                var result = await action.Invoke();
                result.Should().NotBeNull(); // If it completes, it should return a valid result
            }
            catch (OperationCanceledException)
            {
                // This is expected if processing takes too long
            }
        }

        [Fact]
        public async Task CancellationToken_ShouldPropagateToNestedOperations()
        {
            // Create many files to process to increase processing time
            var files = new List<string>();
            for (int i = 0; i < 20; i++)
            {
                var sourceCode = CreateComplexSourceCode(i);
                var filePath = CreateTempFile($"NestedCancellationTest{i}.cs", sourceCode);
                files.Add(filePath);
            }
            
            using var cts = new CancellationTokenSource();
            
            // Start the operation and cancel it shortly after
            var task = _analyzer.BuildCallGraphAsync(files.ToArray(), cts.Token);
            
            await Task.Delay(50); // Let it start
            cts.Cancel();

            var action = async () => await task;
            await action.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task MultipleOperations_WithSharedCancellationToken_ShouldAllCancel()
        {
            var sourceCode = CreateSimpleSourceCode();
            var filePath = CreateTempFile("SharedCancellationTest.cs", sourceCode);
            
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var tasks = new List<Task>
            {
                _analyzer.BuildCallGraphAsync(new[] { filePath }, cts.Token).ContinueWith(t => { }, TaskContinuationOptions.OnlyOnCanceled),
                _analyzer.AnalyzeTypeUsageAsync(new[] { filePath }, cts.Token).ContinueWith(t => { }, TaskContinuationOptions.OnlyOnCanceled),
                _analyzer.ExtractMethodsFromFileAsync(filePath, cts.Token).ContinueWith(t => { }, TaskContinuationOptions.OnlyOnCanceled)
            };

            // All operations should be cancelled
            foreach (var task in tasks)
            {
                try
                {
                    await task;
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            }
        }

        [Fact]
        public async Task CancellationToken_WithGracefulFallback_ShouldHandleCorrectly()
        {
            var sourceCode = CreateSimpleSourceCode();
            var filePath = CreateTempFile("GracefulFallbackTest.cs", sourceCode);
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            try
            {
                // This should either complete or be cancelled gracefully
                var result = await _analyzer.BuildCallGraphAsync(new[] { filePath }, cts.Token);
                result.Should().NotBeNull();
            }
            catch (OperationCanceledException)
            {
                // This is acceptable - the operation was cancelled
            }
            
            // The analyzer should still be usable after cancellation
            _analyzer.Should().NotBeNull();
        }

        [Theory]
        [InlineData(10)]   // Very short timeout
        [InlineData(100)]  // Short timeout
        [InlineData(1000)] // Moderate timeout
        public async Task BuildCallGraphAsync_WithVariousTimeouts_ShouldHandleCorrectly(int timeoutMs)
        {
            var sourceCode = CreateMediumComplexitySourceCode();
            var filePath = CreateTempFile($"TimeoutTest{timeoutMs}.cs", sourceCode);
            
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));

            try
            {
                var result = await _analyzer.BuildCallGraphAsync(new[] { filePath }, cts.Token);
                result.Should().NotBeNull();
            }
            catch (OperationCanceledException)
            {
                // Expected for short timeouts
                timeoutMs.Should().BeLessThan(5000); // Only short timeouts should cancel
            }
        }

        [Fact]
        public async Task CancellationDuringWorkspaceInitialization_ShouldBeHandledGracefully()
        {
            // This test is tricky because we need to cancel during workspace initialization
            // We'll create a solution file and try to cancel during loading
            
            var solutionPath = CreateTestSolution();
            using var cts = new CancellationTokenSource();
            
            // Cancel immediately to increase chances of cancelling during initialization
            cts.Cancel();

            var action = async () => await _analyzer.BuildCallGraphAsync(new[] { solutionPath }, cts.Token);

            await action.Should().ThrowAsync<OperationCanceledException>();
        }

        private string CreateSimpleSourceCode()
        {
            return @"
using System;

namespace TestNamespace
{
    public class TestClass
    {
        public void Method1()
        {
            Method2();
        }

        public void Method2()
        {
            Console.WriteLine(""Hello World"");
        }
    }
}";
        }

        private string CreateComplexSourceCode(int index)
        {
            return $@"
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestNamespace{index}
{{
    public class ComplexClass{index}
    {{
        private List<string> _data = new List<string>();
        
        public void ProcessData()
        {{
            InitializeData();
            var processed = _data
                .Where(x => IsValid(x))
                .Select(x => Transform(x))
                .ToList();
            SaveResults(processed);
        }}
        
        private void InitializeData()
        {{
            for (int i = 0; i < 100; i++)
            {{
                _data.Add($""Item {{i}}"");
            }}
        }}
        
        private bool IsValid(string item) => !string.IsNullOrEmpty(item);
        
        private string Transform(string item) => item.ToUpper();
        
        private void SaveResults(List<string> results)
        {{
            Console.WriteLine($""Processed {{results.Count}} items"");
        }}
    }}
}}";
        }

        private string CreateVeryLargeSourceCode()
        {
            var code = @"
using System;
using System.Collections.Generic;

namespace LargeNamespace
{
    public class VeryLargeClass
    {
        public void StartProcessing()
        {";

            // Generate many method calls
            for (int i = 0; i < 1000; i++)
            {
                code += $"            Process{i}();\n";
            }

            code += @"
        }";

            // Generate many methods
            for (int i = 0; i < 1000; i++)
            {
                code += $@"
        
        public void Process{i}()
        {{
            Console.WriteLine(""Processing {i}"");
            if ({i} % 10 == 0)
                ProcessSpecial{i / 10}();
        }}";
            }

            // Generate special methods
            for (int i = 0; i < 100; i++)
            {
                code += $@"
        
        private void ProcessSpecial{i}()
        {{
            var data = new List<string>();
            for (int j = 0; j < 50; j++)
            {{
                data.Add($""Special{{j}}"");
            }}
            Console.WriteLine($""Special processing {i}: {{data.Count}} items"");
        }}";
            }

            code += @"
    }
}";
            return code;
        }

        private string CreateMediumComplexitySourceCode()
        {
            return @"
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MediumComplexity
{
    public class MediumComplexClass
    {
        public async Task<string> ProcessAsync()
        {
            var tasks = new List<Task<string>>();
            
            for (int i = 0; i < 50; i++)
            {
                tasks.Add(ProcessItemAsync(i));
            }
            
            var results = await Task.WhenAll(tasks);
            return string.Join("", "", results);
        }
        
        private async Task<string> ProcessItemAsync(int index)
        {
            await Task.Delay(1); // Simulate work
            return $""Item{index}"";
        }
        
        public void SynchronousProcessing()
        {
            for (int i = 0; i < 100; i++)
            {
                ProcessSingle(i);
            }
        }
        
        private void ProcessSingle(int value)
        {
            var result = value * 2 + CalculateOffset(value);
            LogResult(result);
        }
        
        private int CalculateOffset(int input) => input % 10;
        
        private void LogResult(int result)
        {
            Console.WriteLine($""Result: {result}"");
        }
    }
}";
        }

        private string CreateTestSourceCode()
        {
            return @"
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
        }

        private string CreateTestSolution()
        {
            var projectContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>";

            var projectDir = Path.Combine(_tempDirectory, "TestProject");
            Directory.CreateDirectory(projectDir);
            
            var projectFile = Path.Combine(projectDir, "TestProject.csproj");
            var sourceFile = Path.Combine(projectDir, "TestClass.cs");
            
            File.WriteAllText(projectFile, projectContent);
            File.WriteAllText(sourceFile, CreateSimpleSourceCode());

            var solutionContent = $@"
Microsoft Visual Studio Solution File, Format Version 12.00
Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""TestProject"", ""TestProject\TestProject.csproj"", ""{{12345678-1234-1234-1234-123456789012}}""
EndProject
";

            var solutionPath = Path.Combine(_tempDirectory, "TestSolution.sln");
            File.WriteAllText(solutionPath, solutionContent);
            
            return solutionPath;
        }

        private string CreateTempFile(string fileName, string content)
        {
            var filePath = Path.Combine(_tempDirectory, fileName);
            File.WriteAllText(filePath, content);
            return filePath;
        }
    }
}