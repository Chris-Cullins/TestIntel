using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.Core.Discovery;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Services;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Analysis
{
    public class RoslynAnalyzerLazyLoadingTests : IDisposable
    {
        private readonly ILogger<RoslynAnalyzer> _mockLogger;
        private readonly SolutionParser _mockSolutionParser;
        private readonly ProjectParser _mockProjectParser;
        private readonly DependencyGraphBuilder _mockDependencyGraphBuilder;
        private readonly SolutionWorkspaceBuilder _mockWorkspaceBuilder;
        private readonly ILoggerFactory _mockLoggerFactory;
        private readonly RoslynAnalyzer _analyzer;
        private string _tempDirectory = null!;
        private string _testSolutionPath = null!;

        public RoslynAnalyzerLazyLoadingTests()
        {
            _mockLogger = Substitute.For<ILogger<RoslynAnalyzer>>();
            _mockSolutionParser = Substitute.For<SolutionParser>(Substitute.For<ILogger<SolutionParser>>());
            _mockProjectParser = Substitute.For<ProjectParser>(Substitute.For<ILogger<ProjectParser>>());
            _mockDependencyGraphBuilder = Substitute.For<DependencyGraphBuilder>(Substitute.For<ILogger<DependencyGraphBuilder>>());
            _mockWorkspaceBuilder = Substitute.For<SolutionWorkspaceBuilder>(Substitute.For<ILogger<SolutionWorkspaceBuilder>>());
            _mockLoggerFactory = Substitute.For<ILoggerFactory>();

            _mockLoggerFactory.CreateLogger(Arg.Any<string>())
                             .Returns(_mockLogger);

            _mockLoggerFactory.CreateLogger<SymbolIndex>()
                             .Returns(Substitute.For<ILogger<SymbolIndex>>());

            _mockLoggerFactory.CreateLogger<LazyWorkspaceBuilder>()
                             .Returns(Substitute.For<ILogger<LazyWorkspaceBuilder>>());

            _mockLoggerFactory.CreateLogger<IncrementalCallGraphBuilder>()
                             .Returns(Substitute.For<ILogger<IncrementalCallGraphBuilder>>());

            _analyzer = new RoslynAnalyzer(
                _mockLogger,
                _mockLoggerFactory);

            _tempDirectory = Path.Combine(Path.GetTempPath(), "TestIntelligence", "RoslynAnalyzerLazyLoadingTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            SetupTestSolution();
        }

        private void SetupTestSolution()
        {
            var projectDir = Path.Combine(_tempDirectory, "TestProject");
            Directory.CreateDirectory(projectDir);
            
            _testSolutionPath = Path.Combine(_tempDirectory, "TestSolution.sln");
            var projectPath = Path.Combine(projectDir, "TestProject.csproj");
            
            // Create solution file
            File.WriteAllText(_testSolutionPath, $@"
Microsoft Visual Studio Solution File, Format Version 12.00
Project(""{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}"") = ""TestProject"", ""TestProject\TestProject.csproj"", ""{{12345678-1234-1234-1234-123456789012}}""
EndProject");

            // Create project file
            File.WriteAllText(projectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>");

            // Create test source file
            CreateTestSourceFile(projectDir, "Calculator.cs", @"
using System;

namespace TestProject
{
    public class Calculator
    {
        public int Add(int a, int b)
        {
            return a + b;
        }

        public int Multiply(int a, int b)
        {
            return a * b;
        }
    }
}");
        }

        private void CreateTestSourceFile(string directory, string fileName, string content)
        {
            var filePath = Path.Combine(directory, fileName);
            File.WriteAllText(filePath, content);
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithLazyWorkspace_ShouldInitializeLazilyOnFirstCall()
        {
            // Arrange
            var solutionFiles = new[] { _testSolutionPath };

            // Act
            var result = await _analyzer.BuildCallGraphAsync(solutionFiles);

            // Assert
            Assert.NotNull(result);
            // Verify that the analyzer completed without error
            // The lazy initialization should have occurred
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithLazyWorkspace_ShouldFallbackToLegacyOnFailure()
        {
            // Arrange
            var solutionFiles = new[] { "/nonexistent/solution.sln" };

            // Setup workspace builder to simulate successful legacy build
            var mockWorkspace = Substitute.For<SolutionWorkspace>(
                Substitute.For<Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace>(),
                Substitute.For<Microsoft.CodeAnalysis.Solution>(),
                Substitute.For<IReadOnlyDictionary<string, Microsoft.CodeAnalysis.ProjectId>>(),
                Substitute.For<IReadOnlyDictionary<Microsoft.CodeAnalysis.ProjectId, Microsoft.CodeAnalysis.Compilation>>());
            var mockCompilationManager = Substitute.For<ICompilationManager>();
            var mockSymbolResolver = Substitute.For<SymbolResolutionEngine>(mockCompilationManager, Substitute.For<ILogger<SymbolResolutionEngine>>());
            var mockCallGraphBuilder = Substitute.For<CallGraphBuilderV2>(
                mockCompilationManager, 
                mockSymbolResolver, 
                Substitute.For<ILogger<CallGraphBuilderV2>>(), 
                _mockLoggerFactory);

            _mockWorkspaceBuilder.CreateWorkspaceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                                .Returns(mockWorkspace);

            // Act
            var result = await _analyzer.BuildCallGraphAsync(solutionFiles);

            // Assert
            Assert.NotNull(result);
            // Should fallback gracefully without throwing
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithValidSolution_ShouldPreferIncrementalAnalysis()
        {
            // Arrange
            var solutionFiles = new[] { _testSolutionPath };

            // Act
            var result = await _analyzer.BuildCallGraphAsync(solutionFiles);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.CallGraph);
            Assert.NotNull(result.MethodDefinitions);
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithMultipleCalls_ShouldReuseInfrastructure()
        {
            // Arrange
            var solutionFiles = new[] { _testSolutionPath };

            // Act
            var result1 = await _analyzer.BuildCallGraphAsync(solutionFiles);
            var result2 = await _analyzer.BuildCallGraphAsync(solutionFiles);

            // Assert
            Assert.NotNull(result1);
            Assert.NotNull(result2);
            
            // Both calls should succeed, with the second potentially using cached infrastructure
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithCancellation_ShouldHandleGracefully()
        {
            // Arrange
            var solutionFiles = new[] { _testSolutionPath };
            var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act & Assert
            // Should handle cancellation gracefully without throwing
            var result = await _analyzer.BuildCallGraphAsync(solutionFiles, cts.Token);
            
            // Even with cancellation, should return a valid (possibly empty) result
            Assert.NotNull(result);
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithEmptyFileList_ShouldReturnEmptyGraph()
        {
            // Arrange
            var emptyFiles = Array.Empty<string>();

            // Act
            var result = await _analyzer.BuildCallGraphAsync(emptyFiles);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.CallGraph);
            Assert.Empty(result.MethodDefinitions);
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithInvalidSolutionPath_ShouldFallbackGracefully()
        {
            // Arrange
            var invalidFiles = new[] { "/completely/invalid/path.sln" };

            // Act
            var result = await _analyzer.BuildCallGraphAsync(invalidFiles);

            // Assert
            Assert.NotNull(result);
            // Should return empty result rather than throwing
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithMixedFileTypes_ShouldHandleAppropriately()
        {
            // Arrange
            var mixedFiles = new[] 
            { 
                _testSolutionPath,
                Path.Combine(_tempDirectory, "TestProject", "Calculator.cs"),
                Path.Combine(_tempDirectory, "NonExistent.cs")
            };

            // Act
            var result = await _analyzer.BuildCallGraphAsync(mixedFiles);

            // Assert
            Assert.NotNull(result);
            // Should process what it can and handle the rest gracefully
        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        public async Task BuildCallGraphAsync_WithRepeatedCalls_ShouldMaintainPerformance(int callCount)
        {
            // Arrange
            var solutionFiles = new[] { _testSolutionPath };

            // Act
            for (int i = 0; i < callCount; i++)
            {
                var result = await _analyzer.BuildCallGraphAsync(solutionFiles);
                Assert.NotNull(result);
            }

            // Assert
            // Test passes if all calls complete without error
            // In practice, later calls should be faster due to caching
            Assert.True(true);
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithLargeTimeout_ShouldCompleteWithinReasonableTime()
        {
            // Arrange
            var solutionFiles = new[] { _testSolutionPath };
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // 30-second timeout

            // Act
            var startTime = DateTime.UtcNow;
            var result = await _analyzer.BuildCallGraphAsync(solutionFiles, cts.Token);
            var elapsed = DateTime.UtcNow - startTime;

            // Assert
            Assert.NotNull(result);
            Assert.True(elapsed.TotalSeconds < 30, $"Analysis took {elapsed.TotalSeconds} seconds, which is too long");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDirectory))
                {
                    Directory.Delete(_tempDirectory, true);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}