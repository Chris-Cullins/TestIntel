using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.Core.Models;
using TestIntelligence.Core.Services;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Models;
using TestIntelligence.ImpactAnalyzer.Services;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Services
{
    public class CodeChangeCoverageAnalyzerTests
    {
        private readonly IGitDiffParser _gitDiffParser;
        private readonly ITestCoverageAnalyzer _testCoverageAnalyzer;
        private readonly ILogger<CodeChangeCoverageAnalyzer> _logger;
        private readonly CodeChangeCoverageAnalyzer _analyzer;

        public CodeChangeCoverageAnalyzerTests()
        {
            _gitDiffParser = Substitute.For<IGitDiffParser>();
            _testCoverageAnalyzer = Substitute.For<ITestCoverageAnalyzer>();
            _logger = Substitute.For<ILogger<CodeChangeCoverageAnalyzer>>();
            
            _analyzer = new CodeChangeCoverageAnalyzer(
                _gitDiffParser,
                _testCoverageAnalyzer,
                _logger);
        }

        [Fact]
        public async Task AnalyzeCoverageAsync_WithValidDiff_ReturnsCorrectCoverageResult()
        {
            // Arrange
            var diffContent = "diff --git a/src/MyClass.cs b/src/MyClass.cs\n+public void MyMethod() { }";
            var testMethodIds = new[] { "MyNamespace.MyTestClass.TestMyMethod" };
            var solutionPath = "/test/solution.sln";

            var codeChanges = CreateTestCodeChangeSet();
            
            // Create test coverage info for the mock
            var testCoverageInfo = new TestCoverageInfo(
                "MyNamespace.MyTestClass.TestMyMethod",
                "TestMyMethod",
                "MyTestClass",
                "MyTestAssembly.dll",
                new[] { "MyTestClass.TestMyMethod", "MyClass.MyMethod" },
                0.9,
                TestType.Unit);

            _gitDiffParser.ParseDiffAsync(diffContent).Returns(codeChanges);
            // Mock the new batch lookup method used by incremental analysis
            var coverageResults = new Dictionary<string, IReadOnlyList<TestCoverageInfo>>
            {
                { "MyMethod", new List<TestCoverageInfo> { testCoverageInfo }.AsReadOnly() }
            };
            _testCoverageAnalyzer.FindTestsExercisingMethodsAsync(
                Arg.Any<IEnumerable<string>>(), 
                solutionPath, 
                Arg.Any<CancellationToken>()).Returns(coverageResults);

            // Act
            var result = await _analyzer.AnalyzeCoverageAsync(diffContent, testMethodIds, solutionPath);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(codeChanges, result.CodeChanges);
            Assert.Equal(solutionPath, result.SolutionPath);
            Assert.True(result.TotalChangedMethods > 0);
            Assert.True(result.CoveragePercentage >= 0 && result.CoveragePercentage <= 100);
        }

        [Fact]
        public async Task AnalyzeCoverageFromFileAsync_WithValidFile_ReturnsCorrectResult()
        {
            // Arrange
            var diffFilePath = "/test/diff.patch";
            var testMethodIds = new[] { "MyNamespace.MyTestClass.TestMyMethod" };
            var solutionPath = "/test/solution.sln";

            var codeChanges = CreateTestCodeChangeSet();
            var testCoverageMap = CreateTestCoverageMap();

            _gitDiffParser.ParseDiffFileAsync(diffFilePath).Returns(codeChanges);
            
            _testCoverageAnalyzer.BuildTestCoverageMapAsync(solutionPath, Arg.Any<CancellationToken>()).Returns(testCoverageMap);

            // Act
            var result = await _analyzer.AnalyzeCoverageFromFileAsync(diffFilePath, testMethodIds, solutionPath);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(codeChanges, result.CodeChanges);
            Assert.Equal(solutionPath, result.SolutionPath);
            await _gitDiffParser.Received(1).ParseDiffFileAsync(diffFilePath);
        }

        [Fact]
        public async Task AnalyzeCoverageFromGitCommandAsync_WithValidCommand_ReturnsCorrectResult()
        {
            // Arrange
            var gitCommand = "diff HEAD~1";
            var testMethodIds = new[] { "MyNamespace.MyTestClass.TestMyMethod" };
            var solutionPath = "/test/solution.sln";

            var codeChanges = CreateTestCodeChangeSet();
            var testCoverageMap = CreateTestCoverageMap();

            _gitDiffParser.ParseDiffFromCommandAsync(gitCommand).Returns(codeChanges);
            
            _testCoverageAnalyzer.BuildTestCoverageMapAsync(solutionPath, Arg.Any<CancellationToken>()).Returns(testCoverageMap);

            // Act
            var result = await _analyzer.AnalyzeCoverageFromGitCommandAsync(gitCommand, testMethodIds, solutionPath);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(codeChanges, result.CodeChanges);
            Assert.Equal(solutionPath, result.SolutionPath);
            await _gitDiffParser.Received(1).ParseDiffFromCommandAsync(gitCommand);
        }

        [Fact]
        public async Task AnalyzeCoverageAsync_WithCompleteTestCoverage_Returns100PercentCoverage()
        {
            // Arrange
            var diffContent = "test diff content";
            var testMethodIds = new[] { "MyTestClass.TestMyMethod" };
            var solutionPath = "/test/solution.sln";

            // Create code changes with one method
            var codeChanges = new CodeChangeSet(new List<CodeChange>
            {
                new CodeChange(
                    "MyClass.cs", 
                    CodeChangeType.Modified,
                    new[] { "MyMethod" },
                    new[] { "MyClass" })
            });

            // Create coverage map that covers the changed method
            var testCoverageInfo = new TestCoverageInfo(
                "MyTestClass.TestMyMethod",
                "TestMyMethod",
                "MyTestClass",
                "MyTestAssembly.dll",
                new[] { "MyTestClass.TestMyMethod", "MyClass.MyMethod" },
                0.9,
                TestType.Unit);

            _gitDiffParser.ParseDiffAsync(diffContent).Returns(codeChanges);
            // Mock the new batch lookup method used by incremental analysis
            var coverageResults = new Dictionary<string, IReadOnlyList<TestCoverageInfo>>
            {
                { "MyMethod", new List<TestCoverageInfo> { testCoverageInfo }.AsReadOnly() }
            };
            _testCoverageAnalyzer.FindTestsExercisingMethodsAsync(
                Arg.Any<IEnumerable<string>>(), 
                solutionPath, 
                Arg.Any<CancellationToken>()).Returns(coverageResults);

            // Act
            var result = await _analyzer.AnalyzeCoverageAsync(diffContent, testMethodIds, solutionPath);

            // Assert
            Assert.Equal(100.0, result.CoveragePercentage);
            Assert.Equal(1, result.TotalChangedMethods);
            Assert.Equal(1, result.CoveredChangedMethods);
            Assert.Equal(0, result.UncoveredChangedMethods);
            Assert.Empty(result.UncoveredMethods);
        }

        [Fact]
        public async Task AnalyzeCoverageAsync_WithPartialTestCoverage_ReturnsCorrectPercentage()
        {
            // Arrange
            var diffContent = "test diff content";
            var testMethodIds = new[] { "MyTestClass.TestOneMethod" };
            var solutionPath = "/test/solution.sln";

            // Create code changes with two methods
            var codeChanges = new CodeChangeSet(new List<CodeChange>
            {
                new CodeChange(
                    "MyClass.cs", 
                    CodeChangeType.Modified,
                    new[] { "MethodOne", "MethodTwo" },
                    new[] { "MyClass" })
            });

            // Create coverage map that only covers one of the methods
            var testCoverageInfo = new TestCoverageInfo(
                "MyTestClass.TestOneMethod",
                "TestOneMethod",
                "MyTestClass",
                "MyTestAssembly.dll",
                new[] { "MyTestClass.TestOneMethod", "MyClass.MethodOne" },
                0.8,
                TestType.Unit);

            _gitDiffParser.ParseDiffAsync(diffContent).Returns(codeChanges);
            // Mock the new batch lookup method used by incremental analysis
            var coverageResults = new Dictionary<string, IReadOnlyList<TestCoverageInfo>>
            {
                { "MethodOne", new List<TestCoverageInfo> { testCoverageInfo }.AsReadOnly() }
                // MethodTwo is not covered (not in the dictionary)
            };
            _testCoverageAnalyzer.FindTestsExercisingMethodsAsync(
                Arg.Any<IEnumerable<string>>(), 
                solutionPath, 
                Arg.Any<CancellationToken>()).Returns(coverageResults);

            // Act
            var result = await _analyzer.AnalyzeCoverageAsync(diffContent, testMethodIds, solutionPath);

            // Assert
            Assert.Equal(50.0, result.CoveragePercentage);
            Assert.Equal(2, result.TotalChangedMethods);
            Assert.Equal(1, result.CoveredChangedMethods);
            Assert.Equal(1, result.UncoveredChangedMethods);
            Assert.Single(result.UncoveredMethods);
            Assert.Contains("MethodTwo", result.UncoveredMethods);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task AnalyzeCoverageAsync_WithInvalidDiffContent_ThrowsArgumentException(string invalidDiffContent)
        {
            // Arrange
            var testMethodIds = new[] { "MyTestClass.TestMyMethod" };
            var solutionPath = "/test/solution.sln";

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _analyzer.AnalyzeCoverageAsync(invalidDiffContent, testMethodIds, solutionPath));
        }

        [Fact]
        public async Task AnalyzeCoverageAsync_WithNullDiffContent_ThrowsArgumentException()
        {
            // Arrange
            var testMethodIds = new[] { "MyTestClass.TestMyMethod" };
            var solutionPath = "/test/solution.sln";

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _analyzer.AnalyzeCoverageAsync(null!, testMethodIds, solutionPath));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task AnalyzeCoverageFromFileAsync_WithInvalidFilePath_ThrowsArgumentException(string invalidFilePath)
        {
            // Arrange
            var testMethodIds = new[] { "MyTestClass.TestMyMethod" };
            var solutionPath = "/test/solution.sln";

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _analyzer.AnalyzeCoverageFromFileAsync(invalidFilePath, testMethodIds, solutionPath));
        }

        [Fact]
        public async Task AnalyzeCoverageFromFileAsync_WithNullFilePath_ThrowsArgumentException()
        {
            // Arrange
            var testMethodIds = new[] { "MyTestClass.TestMyMethod" };
            var solutionPath = "/test/solution.sln";

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _analyzer.AnalyzeCoverageFromFileAsync(null!, testMethodIds, solutionPath));
        }

        [Fact]
        public async Task AnalyzeSingleTestCoverageAsync_WithValidInputs_ReturnsCorrectResult()
        {
            // Arrange
            var codeChanges = CreateTestCodeChangeSet();
            var testMethodId = "MyTestClass.TestMyMethod";
            var solutionPath = "/test/solution.sln";
            
            // Create test coverage info for the single test case
            var testCoverageInfo = new TestCoverageInfo(
                "MyTestClass.TestMyMethod",
                "TestMyMethod",
                "MyTestClass",
                "MyTestAssembly.dll",
                new[] { "MyTestClass.TestMyMethod", "MyClass.MyMethod" },
                0.9,
                TestType.Unit);

            // Mock the new batch lookup method used by incremental analysis
            var coverageResults = new Dictionary<string, IReadOnlyList<TestCoverageInfo>>
            {
                { "MyMethod", new List<TestCoverageInfo> { testCoverageInfo }.AsReadOnly() }
            };
            _testCoverageAnalyzer.FindTestsExercisingMethodsAsync(
                Arg.Any<IEnumerable<string>>(), 
                solutionPath, 
                Arg.Any<CancellationToken>()).Returns(coverageResults);

            // Act
            var result = await _analyzer.AnalyzeSingleTestCoverageAsync(codeChanges, testMethodId, solutionPath);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(codeChanges, result.CodeChanges);
            Assert.Equal(solutionPath, result.SolutionPath);
            Assert.Single(result.ProvidedTests);
        }

        private static CodeChangeSet CreateTestCodeChangeSet()
        {
            var changes = new List<CodeChange>
            {
                new CodeChange(
                    "MyClass.cs", 
                    CodeChangeType.Modified,
                    new[] { "MyMethod" },
                    new[] { "MyClass" })
            };
            return new CodeChangeSet(changes);
        }

        private static TestCoverageMap CreateTestCoverageMap()
        {
            var testCoverageInfo = new TestCoverageInfo(
                "MyTestClass.TestMyMethod",
                "TestMyMethod",
                "MyTestClass",
                "MyTestAssembly.dll",
                new[] { "MyTestClass.TestMyMethod", "MyClass.MyMethod" },
                0.8,
                TestType.Unit);

            var methodToTests = new Dictionary<string, List<TestCoverageInfo>>
            {
                { "MyMethod", new List<TestCoverageInfo> { testCoverageInfo } }
            };

            return new TestCoverageMap(methodToTests, DateTime.UtcNow, "/test/solution.sln");
        }
    }
}