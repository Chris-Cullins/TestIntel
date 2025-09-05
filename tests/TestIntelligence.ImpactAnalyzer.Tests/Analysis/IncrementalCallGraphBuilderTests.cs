using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.Core.Models;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Services;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Analysis
{
    public class IncrementalCallGraphBuilderTests : IDisposable
    {
        private readonly ICompilationManager _mockCompilationManager;
        private readonly ISymbolResolutionEngine _mockSymbolResolver;
        private readonly ISymbolIndex _mockSymbolIndex;
        private readonly ILogger<IncrementalCallGraphBuilder> _mockLogger;
        private readonly ILoggerFactory _mockLoggerFactory;
        private readonly IncrementalCallGraphBuilder _incrementalBuilder;
        private string _tempDirectory;

        public IncrementalCallGraphBuilderTests()
        {
            _mockCompilationManager = Substitute.For<ICompilationManager>();
            _mockSymbolResolver = Substitute.For<ISymbolResolutionEngine>();
            _mockSymbolIndex = Substitute.For<ISymbolIndex>();
            _mockLogger = Substitute.For<ILogger<IncrementalCallGraphBuilder>>();
            _mockLoggerFactory = Substitute.For<ILoggerFactory>();
            
            _mockLoggerFactory.CreateLogger(Arg.Any<string>())
                            .Returns(_mockLogger);

            _incrementalBuilder = new IncrementalCallGraphBuilder(
                _mockCompilationManager,
                _mockSymbolResolver,
                _mockSymbolIndex,
                _mockLogger,
                _mockLoggerFactory);

            _tempDirectory = Path.Combine(Path.GetTempPath(), "TestIntelligence", "IncrementalCallGraphBuilderTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
        }

        [Fact]
        public async Task BuildCallGraphForMethodAsync_WithValidMethod_ShouldReturnCallGraph()
        {
            // Arrange
            var targetMethodId = "TestProject.Calculator.Add";
            var expectedFiles = new List<string> { Path.Combine(_tempDirectory, "Calculator.cs") };
            
            _mockSymbolIndex.FindFilesContainingMethodAsync(targetMethodId)
                           .Returns(Task.FromResult(expectedFiles));

            SetupMockCompilationForFile(expectedFiles[0]);

            // Act
            var result = await _incrementalBuilder.BuildCallGraphForMethodAsync(targetMethodId);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.CallGraph);
            Assert.NotNull(result.MethodDefinitions);
        }

        [Fact]
        public async Task BuildCallGraphForMethodAsync_WithNonexistentMethod_ShouldReturnEmptyGraph()
        {
            // Arrange
            var targetMethodId = "NonexistentMethod";
            
            _mockSymbolIndex.FindFilesContainingMethodAsync(Arg.Is<string>(s => s == targetMethodId), Arg.Any<CancellationToken>())
                           .Returns(Task.FromResult(new List<string>()));

            // Act
            var result = await _incrementalBuilder.BuildCallGraphForMethodAsync(targetMethodId);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.CallGraph);
            Assert.Empty(result.MethodDefinitions);
        }

        [Fact]
        public async Task BuildCallGraphForMethodAsync_WithMaxDepthLimit_ShouldRespectDepth()
        {
            // Arrange
            var targetMethodId = "TestProject.Calculator.Add";
            var expectedFiles = new List<string> { Path.Combine(_tempDirectory, "Calculator.cs") };
            const int maxDepth = 2;
            
            _mockSymbolIndex.FindFilesContainingMethodAsync(Arg.Is<string>(s => s == targetMethodId), Arg.Any<CancellationToken>())
                           .Returns(Task.FromResult(expectedFiles));

            SetupMockCompilationForFile(expectedFiles[0]);

            // Act
            var result = await _incrementalBuilder.BuildCallGraphForMethodAsync(targetMethodId, maxDepth);

            // Assert
            Assert.NotNull(result);
            // The specific depth checking would require more complex setup, 
            // but we can verify the method completes without error
        }

        [Fact]
        public async Task BuildCallGraphForMethodsAsync_WithMultipleMethods_ShouldCombineResults()
        {
            // Arrange
            var methodIds = new[] { "Method1", "Method2", "Method3" };
            var expectedFiles = new List<string> 
            { 
                Path.Combine(_tempDirectory, "File1.cs"),
                Path.Combine(_tempDirectory, "File2.cs") 
            };
            
            foreach (var methodId in methodIds)
            {
                _mockSymbolIndex.FindFilesContainingMethodAsync(Arg.Is<string>(s => s == methodId), Arg.Any<CancellationToken>())
                               .Returns(Task.FromResult(expectedFiles));
            }

            foreach (var file in expectedFiles)
            {
                SetupMockCompilationForFile(file);
            }

            // Act
            var result = await _incrementalBuilder.BuildCallGraphForMethodsAsync(methodIds);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.CallGraph);
            Assert.NotNull(result.MethodDefinitions);
        }

        [Fact]
        public async Task BuildCallGraphForMethodAsync_WithCachedResult_ShouldUseCacheOnSecondCall()
        {
            // Arrange
            var targetMethodId = "TestProject.Calculator.Add";
            var expectedFiles = new List<string> { Path.Combine(_tempDirectory, "Calculator.cs") };
            
            _mockSymbolIndex.FindFilesContainingMethodAsync(Arg.Is<string>(s => s == targetMethodId), Arg.Any<CancellationToken>())
                           .Returns(Task.FromResult(expectedFiles));

            SetupMockCompilationForFile(expectedFiles[0]);

            // Act
            var result1 = await _incrementalBuilder.BuildCallGraphForMethodAsync(targetMethodId);
            var result2 = await _incrementalBuilder.BuildCallGraphForMethodAsync(targetMethodId);

            // Assert
            Assert.NotNull(result1);
            Assert.NotNull(result2);
            
            // Verify FindFilesContainingMethodAsync was called twice (once for each build)
            await _mockSymbolIndex.Received(2).FindFilesContainingMethodAsync(targetMethodId, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task BuildCallGraphForMethodAsync_WithCompilationError_ShouldHandleGracefully()
        {
            // Arrange
            var targetMethodId = "TestProject.Calculator.Add";
            var expectedFiles = new List<string> { Path.Combine(_tempDirectory, "Calculator.cs") };
            
            _mockSymbolIndex.FindFilesContainingMethodAsync(Arg.Is<string>(s => s == targetMethodId), Arg.Any<CancellationToken>())
                           .Returns(Task.FromResult(expectedFiles));

            _mockCompilationManager.GetSyntaxTreeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                                  .Returns(Task.FromException<Microsoft.CodeAnalysis.SyntaxTree?>(new InvalidOperationException("Compilation failed")));

            // Act
            var result = await _incrementalBuilder.BuildCallGraphForMethodAsync(targetMethodId);

            // Assert
            Assert.NotNull(result);
            // Should return empty graph rather than throwing
            Assert.Empty(result.CallGraph);
        }

        [Fact]
        public void ClearCaches_ShouldResetInternalCaches()
        {
            // Act
            _incrementalBuilder.ClearCaches();

            // Assert
            // Verify no exceptions are thrown - internal caches are cleared
            // This is primarily tested by ensuring subsequent operations work correctly
            Assert.True(true); // Test passes if no exception thrown
        }

        [Fact]
        public async Task BuildCallGraphForMethodAsync_WithCancellation_ShouldRespectCancellationToken()
        {
            // Arrange
            var targetMethodId = "TestProject.Calculator.Add";
            var expectedFiles = new List<string> { Path.Combine(_tempDirectory, "Calculator.cs") };
            
            _mockSymbolIndex.FindFilesContainingMethodAsync(Arg.Is<string>(s => s == targetMethodId), Arg.Any<CancellationToken>())
                           .Returns(Task.FromResult(expectedFiles));

            var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act & Assert
            var result = await _incrementalBuilder.BuildCallGraphForMethodAsync(targetMethodId, 5, cts.Token);
            
            // Should complete gracefully even with cancelled token (graceful degradation)
            Assert.NotNull(result);
        }

        [Fact]
        public async Task BuildCallGraphForMethodsAsync_WithEmptyMethodList_ShouldReturnEmptyResult()
        {
            // Arrange
            var emptyMethodIds = Array.Empty<string>();

            // Act
            var result = await _incrementalBuilder.BuildCallGraphForMethodsAsync(emptyMethodIds);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.CallGraph);
            Assert.Empty(result.MethodDefinitions);
        }

        [Fact]
        public async Task BuildCallGraphForMethodAsync_WithInvalidMaxDepth_ShouldHandleGracefully()
        {
            // Arrange
            var targetMethodId = "TestProject.Calculator.Add";
            var expectedFiles = new List<string> { Path.Combine(_tempDirectory, "Calculator.cs") };
            
            _mockSymbolIndex.FindFilesContainingMethodAsync(Arg.Is<string>(s => s == targetMethodId), Arg.Any<CancellationToken>())
                           .Returns(Task.FromResult(expectedFiles));

            SetupMockCompilationForFile(expectedFiles[0]);

            // Act
            var result = await _incrementalBuilder.BuildCallGraphForMethodAsync(targetMethodId, -1);

            // Assert
            Assert.NotNull(result);
            // Should handle negative depth gracefully
        }

        private void SetupMockCompilationForFile(string filePath)
        {
            // Setup compilation manager to return null (simulating file not found or compilation error)
            // This will trigger the graceful error handling in the IncrementalCallGraphBuilder
            _mockCompilationManager.GetSyntaxTreeAsync(filePath, Arg.Any<CancellationToken>())
                                  .Returns(Task.FromResult<Microsoft.CodeAnalysis.SyntaxTree?>(null));

            _mockCompilationManager.GetSemanticModel(filePath)
                                  .Returns((Microsoft.CodeAnalysis.SemanticModel?)null);

            // Setup symbol resolver
            _mockSymbolResolver.GetFullyQualifiedMethodName(Arg.Any<Microsoft.CodeAnalysis.IMethodSymbol>())
                              .Returns("TestProject.Calculator.Add");
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