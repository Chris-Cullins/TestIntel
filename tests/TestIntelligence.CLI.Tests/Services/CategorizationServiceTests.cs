using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.CLI.Models;
using TestIntelligence.CLI.Services;
using TestIntelligence.Core.Models;
using Xunit;

namespace TestIntelligence.CLI.Tests.Services;

/// <summary>
/// Tests for CategorizationService functionality.
/// </summary>
public class CategorizationServiceTests
{
    private readonly ILogger<CategorizationService> _mockLogger;
    private readonly IAnalysisService _mockAnalysisService;
    private readonly IOutputFormatter _mockOutputFormatter;
    private readonly CategorizationService _service;

    public CategorizationServiceTests()
    {
        _mockLogger = Substitute.For<ILogger<CategorizationService>>();
        _mockAnalysisService = Substitute.For<IAnalysisService>();
        _mockOutputFormatter = Substitute.For<IOutputFormatter>();
        _service = new CategorizationService(_mockLogger, _mockAnalysisService, _mockOutputFormatter);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new CategorizationService(null!, _mockAnalysisService, _mockOutputFormatter));
    }

    [Fact]
    public void Constructor_WithNullAnalysisService_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new CategorizationService(_mockLogger, null!, _mockOutputFormatter));
    }

    [Fact]
    public void Constructor_WithNullOutputFormatter_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new CategorizationService(_mockLogger, _mockAnalysisService, null!));
    }

    [Fact]
    public async Task CategorizeAsync_WithValidPath_SuccessfullyCategorizesTests()
    {
        // Arrange
        var testPath = "/test/path";
        var outputPath = "/output/path";
        var analysisResult = CreateSampleAnalysisResult();
        var analysisJson = Newtonsoft.Json.JsonConvert.SerializeObject(analysisResult);

        // Create temp file to simulate analysis service output
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, analysisJson);

        _mockAnalysisService.AnalyzeAsync(testPath, Arg.Any<string>(), "json", false)
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo =>
            {
                var tempPath = callInfo.ArgAt<string>(1);
                File.WriteAllText(tempPath!, analysisJson);
            });

        // Act
        await _service.CategorizeAsync(testPath, outputPath);

        // Assert
        await _mockAnalysisService.Received(1).AnalyzeAsync(testPath, Arg.Any<string>(), "json", false);
        await _mockOutputFormatter.Received(1).WriteOutputAsync(Arg.Any<CategorizationResult>(), "text", outputPath);

        // Cleanup
        if (File.Exists(tempFile))
            File.Delete(tempFile);
    }

    [Fact]
    public async Task CategorizeAsync_WithNullAnalysisResult_ThrowsInvalidOperationException()
    {
        // Arrange
        var testPath = "/test/path";
        var outputPath = "/output/path";

        _mockAnalysisService.AnalyzeAsync(testPath, Arg.Any<string>(), "json", false)
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo =>
            {
                var tempPath = callInfo.ArgAt<string>(1);
                File.WriteAllText(tempPath!, "null");
            });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CategorizeAsync(testPath, outputPath));
        
        Assert.Equal("Failed to analyze assemblies", exception.Message);
    }

    [Fact]
    public async Task CategorizeAsync_WithAnalysisServiceException_PropagatesException()
    {
        // Arrange
        var testPath = "/test/path";
        var outputPath = "/output/path";
        var expectedException = new InvalidOperationException("Analysis failed");

        _mockAnalysisService.AnalyzeAsync(testPath, Arg.Any<string>(), "json", false)
            .Returns(Task.FromException(expectedException));

        // Act & Assert
        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CategorizeAsync(testPath, outputPath));
        
        Assert.Equal(expectedException, actualException);
    }

    [Fact]
    public async Task CategorizeAsync_WithOutputFormatterException_PropagatesException()
    {
        // Arrange
        var testPath = "/test/path";
        var outputPath = "/output/path";
        var analysisResult = CreateSampleAnalysisResult();
        var analysisJson = Newtonsoft.Json.JsonConvert.SerializeObject(analysisResult);
        var expectedException = new IOException("Output write failed");

        _mockAnalysisService.AnalyzeAsync(testPath, Arg.Any<string>(), "json", false)
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo =>
            {
                var tempPath = callInfo.ArgAt<string>(1);
                File.WriteAllText(tempPath!, analysisJson);
            });

        _mockOutputFormatter.WriteOutputAsync(Arg.Any<CategorizationResult>(), "text", outputPath)
            .Returns(Task.FromException(expectedException));

        // Act & Assert
        var actualException = await Assert.ThrowsAsync<IOException>(
            () => _service.CategorizeAsync(testPath, outputPath));
        
        Assert.Equal(expectedException, actualException);
    }

    [Fact]
    public async Task CategorizeAsync_WithEmptyAnalysisResult_CreatesEmptyCategorizationResult()
    {
        // Arrange
        var testPath = "/test/path";
        var outputPath = "/output/path";
        var emptyAnalysisResult = new AnalysisResult
        {
            AnalyzedPath = testPath,
            Timestamp = DateTimeOffset.Now,
            Assemblies = new List<AssemblyAnalysis>()
        };
        var analysisJson = Newtonsoft.Json.JsonConvert.SerializeObject(emptyAnalysisResult);

        _mockAnalysisService.AnalyzeAsync(testPath, Arg.Any<string>(), "json", false)
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo =>
            {
                var tempPath = callInfo.ArgAt<string>(1);
                File.WriteAllText(tempPath!, analysisJson);
            });

        CategorizationResult? capturedResult = null;
        _mockOutputFormatter.WriteOutputAsync(Arg.Any<CategorizationResult>(), "text", outputPath)
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo =>
            {
                capturedResult = callInfo.ArgAt<CategorizationResult>(0);
            });

        // Act
        await _service.CategorizeAsync(testPath, outputPath);

        // Assert
        Assert.NotNull(capturedResult);
        Assert.Equal(testPath, capturedResult.AnalyzedPath);
        Assert.Equal(0, capturedResult.TotalTests);
        Assert.Empty(capturedResult.Categories);
    }

    [Fact]
    public async Task CategorizeAsync_WithMultipleCategoriesAndTests_GroupsTestsCorrectly()
    {
        // Arrange
        var testPath = "/test/path";
        var outputPath = "/output/path";
        var analysisResult = CreateMultiCategoryAnalysisResult();
        var analysisJson = Newtonsoft.Json.JsonConvert.SerializeObject(analysisResult);

        _mockAnalysisService.AnalyzeAsync(testPath, Arg.Any<string>(), "json", false)
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo =>
            {
                var tempPath = callInfo.ArgAt<string>(1);
                File.WriteAllText(tempPath!, analysisJson);
            });

        CategorizationResult? capturedResult = null;
        _mockOutputFormatter.WriteOutputAsync(Arg.Any<CategorizationResult>(), "text", outputPath)
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo =>
            {
                capturedResult = callInfo.ArgAt<CategorizationResult>(0);
            });

        // Act
        await _service.CategorizeAsync(testPath, outputPath);

        // Assert
        Assert.NotNull(capturedResult);
        Assert.Equal(6, capturedResult.TotalTests);
        Assert.Equal(3, capturedResult.Categories.Count);
        
        Assert.Contains(TestCategory.Unit, capturedResult.Categories.Keys);
        Assert.Contains(TestCategory.Integration, capturedResult.Categories.Keys);
        Assert.Contains(TestCategory.EndToEnd, capturedResult.Categories.Keys);

        Assert.Equal(2, capturedResult.Categories[TestCategory.Unit].Count);
        Assert.Equal(3, capturedResult.Categories[TestCategory.Integration].Count);
        Assert.Single(capturedResult.Categories[TestCategory.EndToEnd]);

        // Verify tests are sorted within categories
        Assert.True(capturedResult.Categories[TestCategory.Unit].SequenceEqual(
            capturedResult.Categories[TestCategory.Unit].OrderBy(x => x)));
    }

    [Fact]
    public async Task CategorizeAsync_WithNullOutputPath_PassesNullToOutputFormatter()
    {
        // Arrange
        var testPath = "/test/path";
        var analysisResult = CreateSampleAnalysisResult();
        var analysisJson = Newtonsoft.Json.JsonConvert.SerializeObject(analysisResult);

        _mockAnalysisService.AnalyzeAsync(testPath, Arg.Any<string>(), "json", false)
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo =>
            {
                var tempPath = callInfo.ArgAt<string>(1);
                File.WriteAllText(tempPath!, analysisJson);
            });

        // Act
        await _service.CategorizeAsync(testPath, null);

        // Assert
        await _mockOutputFormatter.Received(1).WriteOutputAsync(Arg.Any<CategorizationResult>(), "text", null);
    }

    [Fact]
    public async Task CategorizeAsync_LogsStartAndCompletion()
    {
        // Arrange
        var testPath = "/test/path";
        var analysisResult = CreateSampleAnalysisResult();
        var analysisJson = Newtonsoft.Json.JsonConvert.SerializeObject(analysisResult);

        _mockAnalysisService.AnalyzeAsync(testPath, Arg.Any<string>(), "json", false)
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo =>
            {
                var tempPath = callInfo.ArgAt<string>(1);
                File.WriteAllText(tempPath!, analysisJson);
            });

        // Act
        await _service.CategorizeAsync(testPath, null);

        // Assert
        _mockLogger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(v => v.ToString()!.Contains("Starting categorization")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());

        _mockLogger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(v => v.ToString()!.Contains("Categorization completed successfully")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task CategorizeAsync_WithException_LogsError()
    {
        // Arrange
        var testPath = "/test/path";
        var expectedException = new InvalidOperationException("Test error");

        _mockAnalysisService.AnalyzeAsync(testPath, Arg.Any<string>(), "json", false)
            .Returns(Task.FromException(expectedException));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CategorizeAsync(testPath, null));

        _mockLogger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(v => v.ToString()!.Contains("Error during categorization")),
            expectedException,
            Arg.Any<Func<object, Exception?, string>>());
    }

    private static AnalysisResult CreateSampleAnalysisResult()
    {
        return new AnalysisResult
        {
            AnalyzedPath = "/test/path",
            Timestamp = DateTimeOffset.Now,
            Assemblies = new List<AssemblyAnalysis>
            {
                new AssemblyAnalysis
                {
                    AssemblyPath = "/test/assembly.dll",
                    Framework = "net8.0",
                    TestMethods = new List<TestMethodAnalysis>
                    {
                        new TestMethodAnalysis
                        {
                            MethodName = "TestMethod1",
                            Category = TestCategory.Unit,
                            EstimatedDuration = TimeSpan.FromMilliseconds(100)
                        }
                    }
                }
            }
        };
    }

    private static AnalysisResult CreateMultiCategoryAnalysisResult()
    {
        return new AnalysisResult
        {
            AnalyzedPath = "/test/path",
            Timestamp = DateTimeOffset.Now,
            Assemblies = new List<AssemblyAnalysis>
            {
                new AssemblyAnalysis
                {
                    AssemblyPath = "/test/assembly1.dll",
                    Framework = "net8.0",
                    TestMethods = new List<TestMethodAnalysis>
                    {
                        new TestMethodAnalysis
                        {
                            MethodName = "UnitTest1",
                            Category = TestCategory.Unit,
                            EstimatedDuration = TimeSpan.FromMilliseconds(50)
                        },
                        new TestMethodAnalysis
                        {
                            MethodName = "IntegrationTest1",
                            Category = TestCategory.Integration,
                            EstimatedDuration = TimeSpan.FromSeconds(1)
                        },
                        new TestMethodAnalysis
                        {
                            MethodName = "IntegrationTest2",
                            Category = TestCategory.Integration,
                            EstimatedDuration = TimeSpan.FromSeconds(2)
                        }
                    }
                },
                new AssemblyAnalysis
                {
                    AssemblyPath = "/test/assembly2.dll",
                    Framework = "net8.0",
                    TestMethods = new List<TestMethodAnalysis>
                    {
                        new TestMethodAnalysis
                        {
                            MethodName = "UnitTest2",
                            Category = TestCategory.Unit,
                            EstimatedDuration = TimeSpan.FromMilliseconds(75)
                        },
                        new TestMethodAnalysis
                        {
                            MethodName = "IntegrationTest3",
                            Category = TestCategory.Integration,
                            EstimatedDuration = TimeSpan.FromSeconds(1.5)
                        },
                        new TestMethodAnalysis
                        {
                            MethodName = "E2ETest1",
                            Category = TestCategory.EndToEnd,
                            EstimatedDuration = TimeSpan.FromMinutes(5)
                        }
                    }
                }
            }
        };
    }
}