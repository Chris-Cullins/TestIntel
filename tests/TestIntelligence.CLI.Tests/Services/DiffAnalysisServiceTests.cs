using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using TestIntelligence.CLI.Services;
using TestIntelligence.ImpactAnalyzer.Services;
using TestIntelligence.ImpactAnalyzer.Models;
using System.Collections.Generic;

namespace TestIntelligence.CLI.Tests.Services
{
    public class DiffAnalysisServiceTests
    {
        private readonly ILogger<DiffAnalysisService> _logger;
        private readonly ISimplifiedDiffImpactAnalyzer _diffImpactAnalyzer;
        private readonly IOutputFormatter _outputFormatter;
        private readonly DiffAnalysisService _service;

        public DiffAnalysisServiceTests()
        {
            _logger = Substitute.For<ILogger<DiffAnalysisService>>();
            _diffImpactAnalyzer = Substitute.For<ISimplifiedDiffImpactAnalyzer>();
            _outputFormatter = Substitute.For<IOutputFormatter>();
            _service = new DiffAnalysisService(_logger, _diffImpactAnalyzer, _outputFormatter);
        }

        [Fact]
        public async Task AnalyzeDiffAsync_WithNonExistentSolution_DoesNotCallAnalyzer()
        {
            // Arrange
            var nonExistentSolution = "/path/to/nonexistent/solution.sln";
            var diffContent = "sample diff";

            // Act
            await _service.AnalyzeDiffAsync(nonExistentSolution, diffContent, null, null, null, "text", false);

            // Assert
            await _diffImpactAnalyzer.DidNotReceive().AnalyzeDiffImpactAsync(Arg.Any<string>(), Arg.Any<string>());
        }

        [Fact]
        public async Task AnalyzeDiffAsync_WithMultipleDiffSources_DoesNotCallAnalyzer()
        {
            // Arrange
            var tempSolution = Path.GetTempFileName();
            File.WriteAllText(tempSolution, "# Dummy solution file");
            
            try
            {
                // Act - providing multiple diff sources should fail validation
                await _service.AnalyzeDiffAsync(tempSolution, "diff content", "diff.patch", null, null, "text", false);

                // Assert
                await _diffImpactAnalyzer.DidNotReceive().AnalyzeDiffImpactAsync(Arg.Any<string>(), Arg.Any<string>());
                await _diffImpactAnalyzer.DidNotReceive().AnalyzeDiffFileImpactAsync(Arg.Any<string>(), Arg.Any<string>());
            }
            finally
            {
                File.Delete(tempSolution);
            }
        }

        [Fact]
        public async Task AnalyzeDiffAsync_WithNoDiffSource_DoesNotCallAnalyzer()
        {
            // Arrange
            var tempSolution = Path.GetTempFileName();
            File.WriteAllText(tempSolution, "# Dummy solution file");
            
            try
            {
                // Act - providing no diff sources should fail validation
                await _service.AnalyzeDiffAsync(tempSolution, null, null, null, null, "text", false);

                // Assert
                await _diffImpactAnalyzer.DidNotReceive().AnalyzeDiffImpactAsync(Arg.Any<string>(), Arg.Any<string>());
                await _diffImpactAnalyzer.DidNotReceive().AnalyzeDiffFileImpactAsync(Arg.Any<string>(), Arg.Any<string>());
                await _diffImpactAnalyzer.DidNotReceive().AnalyzeGitDiffImpactAsync(Arg.Any<string>(), Arg.Any<string>());
            }
            finally
            {
                File.Delete(tempSolution);
            }
        }

        [Fact]
        public async Task AnalyzeDiffAsync_WithDiffContent_CallsCorrectAnalyzer()
        {
            // Arrange
            var tempSolution = Path.GetTempFileName();
            File.WriteAllText(tempSolution, "# Dummy solution file");
            var diffContent = "sample diff content";
            var mockResult = CreateMockTestImpactResult();

            _diffImpactAnalyzer.AnalyzeDiffImpactAsync(diffContent, tempSolution).Returns(mockResult);
            
            try
            {
                // Act
                await _service.AnalyzeDiffAsync(tempSolution, diffContent, null, null, null, "text", false);

                // Assert
                await _diffImpactAnalyzer.Received(1).AnalyzeDiffImpactAsync(diffContent, tempSolution);
            }
            finally
            {
                File.Delete(tempSolution);
            }
        }

        [Fact]
        public async Task AnalyzeDiffAsync_WithDiffFile_CallsCorrectAnalyzer()
        {
            // Arrange
            var tempSolution = Path.GetTempFileName();
            File.WriteAllText(tempSolution, "# Dummy solution file");
            var diffFile = "/path/to/diff.patch";
            var mockResult = CreateMockTestImpactResult();

            _diffImpactAnalyzer.AnalyzeDiffFileImpactAsync(diffFile, tempSolution).Returns(mockResult);
            
            try
            {
                // Act
                await _service.AnalyzeDiffAsync(tempSolution, null, diffFile, null, null, "text", false);

                // Assert
                await _diffImpactAnalyzer.Received(1).AnalyzeDiffFileImpactAsync(diffFile, tempSolution);
            }
            finally
            {
                File.Delete(tempSolution);
            }
        }

        [Fact]
        public async Task AnalyzeDiffAsync_WithGitCommand_CallsCorrectAnalyzer()
        {
            // Arrange
            var tempSolution = Path.GetTempFileName();
            File.WriteAllText(tempSolution, "# Dummy solution file");
            var gitCommand = "diff HEAD~1";
            var mockResult = CreateMockTestImpactResult();

            _diffImpactAnalyzer.AnalyzeGitDiffImpactAsync(gitCommand, tempSolution).Returns(mockResult);
            
            try
            {
                // Act
                await _service.AnalyzeDiffAsync(tempSolution, null, null, gitCommand, null, "text", false);

                // Assert
                await _diffImpactAnalyzer.Received(1).AnalyzeGitDiffImpactAsync(gitCommand, tempSolution);
            }
            finally
            {
                File.Delete(tempSolution);
            }
        }

        [Fact]
        public async Task AnalyzeDiffAsync_WithOutputFile_WritesToFile()
        {
            // Arrange
            var tempSolution = Path.GetTempFileName();
            var tempOutput = Path.GetTempFileName();
            File.WriteAllText(tempSolution, "# Dummy solution file");
            
            var diffContent = "sample diff content";
            var mockResult = CreateMockTestImpactResult();

            _diffImpactAnalyzer.AnalyzeDiffImpactAsync(diffContent, tempSolution).Returns(mockResult);
            
            try
            {
                // Act
                await _service.AnalyzeDiffAsync(tempSolution, diffContent, null, null, tempOutput, "text", false);

                // Assert
                Assert.True(File.Exists(tempOutput));
                var content = await File.ReadAllTextAsync(tempOutput);
                Assert.Contains("Test Impact Analysis Results", content);
            }
            finally
            {
                File.Delete(tempSolution);
                if (File.Exists(tempOutput))
                    File.Delete(tempOutput);
            }
        }

        [Fact]
        public async Task AnalyzeDiffAsync_WithJsonFormat_ReturnsJsonOutput()
        {
            // Arrange
            var tempSolution = Path.GetTempFileName();
            var tempOutput = Path.GetTempFileName();
            File.WriteAllText(tempSolution, "# Dummy solution file");
            
            var diffContent = "sample diff content";
            var mockResult = CreateMockTestImpactResult();

            _diffImpactAnalyzer.AnalyzeDiffImpactAsync(diffContent, tempSolution).Returns(mockResult);
            
            try
            {
                // Act
                await _service.AnalyzeDiffAsync(tempSolution, diffContent, null, null, tempOutput, "json", false);

                // Assert
                Assert.True(File.Exists(tempOutput));
                var content = await File.ReadAllTextAsync(tempOutput);
                Assert.Contains("{", content); // Should contain JSON structure
                Assert.Contains("Summary", content);
                Assert.Contains("TotalChanges", content);
            }
            finally
            {
                File.Delete(tempSolution);
                if (File.Exists(tempOutput))
                    File.Delete(tempOutput);
            }
        }

        [Fact]
        public async Task AnalyzeDiffAsync_WithException_HandlesSafely()
        {
            // Arrange
            var tempSolution = Path.GetTempFileName();
            File.WriteAllText(tempSolution, "# Dummy solution file");
            var diffContent = "sample diff content";

            _diffImpactAnalyzer.AnalyzeDiffImpactAsync(diffContent, tempSolution)
                .Returns(Task.FromException<SimplifiedTestImpactResult>(new InvalidOperationException("Test exception")));
            
            try
            {
                // Act - should not throw
                await _service.AnalyzeDiffAsync(tempSolution, diffContent, null, null, null, "text", false);

                // Assert - method completed without throwing
                Assert.True(true);
            }
            finally
            {
                File.Delete(tempSolution);
            }
        }

        private SimplifiedTestImpactResult CreateMockTestImpactResult()
        {
            var testReference = new SimplifiedTestReference(
                "SampleTest",
                "TestClass",
                "MyApp.Tests",
                "tests.dll",
                0.8,
                "Method name similarity"
            );

            var codeChange = new CodeChange(
                "src/SampleClass.cs",
                CodeChangeType.Modified,
                new[] { "SampleMethod" },
                new[] { "SampleClass" }
            );

            var changeSet = new CodeChangeSet(new[] { codeChange });

            return new SimplifiedTestImpactResult(
                new[] { testReference },
                changeSet,
                new[] { "SampleClass.SampleMethod" },
                DateTime.UtcNow
            );
        }
    }
}