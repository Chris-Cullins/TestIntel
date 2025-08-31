using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.API.Controllers;
using TestIntelligence.API.Models;
using TestIntelligence.ImpactAnalyzer.Models;
using TestIntelligence.ImpactAnalyzer.Services;
using TestIntelligence.SelectionEngine.Interfaces;
using TestIntelligence.SelectionEngine.Models;
using Xunit;

namespace TestIntelligence.API.Tests.Controllers;

public class TestSelectionControllerTests
{
    private readonly ITestSelectionEngine _mockSelectionEngine;
    private readonly ISimplifiedDiffImpactAnalyzer _mockImpactAnalyzer;
    private readonly ILogger<TestSelectionController> _mockLogger;
    private readonly TestSelectionController _controller;

    public TestSelectionControllerTests()
    {
        _mockSelectionEngine = Substitute.For<ITestSelectionEngine>();
        _mockImpactAnalyzer = Substitute.For<ISimplifiedDiffImpactAnalyzer>();
        _mockLogger = Substitute.For<ILogger<TestSelectionController>>();
        
        _controller = new TestSelectionController(
            _mockSelectionEngine, 
            _mockImpactAnalyzer, 
            _mockLogger);
    }

    [Fact]
    public async Task GetTestPlan_WithValidRequest_ReturnsOkResult()
    {
        // Arrange
        var request = new TestPlanRequest
        {
            ConfidenceLevel = ConfidenceLevel.Medium,
            MaxTests = 50
        };

        var expectedPlan = new TestExecutionPlan(
            Array.Empty<TestInfo>(),
            ConfidenceLevel.Medium,
            TimeSpan.FromMinutes(5),
            "Test plan");

        _mockSelectionEngine
            .GetTestPlanAsync(request.ConfidenceLevel, Arg.Any<TestSelectionOptions>(), Arg.Any<CancellationToken>())
            .Returns(expectedPlan);

        // Act
        var result = await _controller.GetTestPlan(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var plan = Assert.IsType<TestExecutionPlan>(okResult.Value);
        Assert.Equal(ConfidenceLevel.Medium, plan.ConfidenceLevel);
    }

    [Fact]
    public async Task GetTestPlan_WithCodeChanges_CallsOptimalTestPlanAsync()
    {
        // Arrange
        var codeChanges = new CodeChangeSet(new List<CodeChange>
        {
            new("test.cs", CodeChangeType.Modified, new List<string> { "TestMethod" }, new List<string> { "TestClass" })
        });

        var request = new TestPlanRequest
        {
            CodeChanges = codeChanges,
            ConfidenceLevel = ConfidenceLevel.High
        };

        var expectedPlan = new TestExecutionPlan(
            Array.Empty<TestInfo>(),
            ConfidenceLevel.High,
            TimeSpan.FromMinutes(10),
            "High confidence plan");

        _mockSelectionEngine
            .GetOptimalTestPlanAsync(codeChanges, ConfidenceLevel.High, Arg.Any<CancellationToken>())
            .Returns(expectedPlan);

        // Act
        var result = await _controller.GetTestPlan(request);

        // Assert
        await _mockSelectionEngine.Received(1).GetOptimalTestPlanAsync(
            codeChanges, 
            ConfidenceLevel.High, 
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnalyzeDiff_WithValidRequest_ReturnsAnalysisResult()
    {
        // Arrange
        var request = new DiffAnalysisRequest
        {
            SolutionPath = "/path/to/solution.sln",
            DiffContent = "diff --git a/test.cs b/test.cs\n+added line",
            ConfidenceLevel = ConfidenceLevel.Medium
        };

        var changeSet = new CodeChangeSet(new List<CodeChange>
        {
            new("test.cs", CodeChangeType.Modified, new List<string> { "Method1" }, new List<string> { "Class1" })
        });

        var testPlan = new TestExecutionPlan(
            Array.Empty<TestInfo>(),
            ConfidenceLevel.Medium,
            TimeSpan.FromMinutes(3),
            "Analysis-based plan");

        _mockImpactAnalyzer
            .AnalyzeChangeSetImpactAsync(request.SolutionPath, request.DiffContent, request.ConfidenceLevel)
            .Returns(changeSet);

        _mockSelectionEngine
            .GetOptimalTestPlanAsync(changeSet, request.ConfidenceLevel, Arg.Any<CancellationToken>())
            .Returns(testPlan);

        // Act
        var result = await _controller.AnalyzeDiff(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var analysisResult = Assert.IsType<DiffAnalysisResult>(okResult.Value);
        Assert.Equal(1, analysisResult.TotalChanges);
        Assert.Equal(changeSet, analysisResult.ChangeSet);
        Assert.Equal(testPlan, analysisResult.RecommendedTests);
    }

    [Fact]
    public async Task UpdateExecutionHistory_WithValidResults_ReturnsOk()
    {
        // Arrange
        var results = new List<TestExecutionResult>
        {
            new("test1", TimeSpan.FromSeconds(1), true, DateTimeOffset.UtcNow, "Success"),
            new("test2", TimeSpan.FromSeconds(2), false, DateTimeOffset.UtcNow, "Failed")
        };

        // Act
        var result = await _controller.UpdateExecutionHistory(results);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        await _mockSelectionEngine.Received(1).UpdateTestExecutionHistoryAsync(
            results, 
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTestHistory_WithFilter_ReturnsFilteredResults()
    {
        // Arrange
        var filter = "UserTest";
        var expectedHistory = new List<TestInfo>
        {
            new("UserTest1", "Assembly1", "Method1", TestCategory.Unit),
            new("UserTest2", "Assembly1", "Method2", TestCategory.Integration)
        };

        _mockSelectionEngine
            .GetTestHistoryAsync(filter, Arg.Any<CancellationToken>())
            .Returns(expectedHistory);

        // Act
        var result = await _controller.GetTestHistory(filter);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var history = Assert.IsType<List<TestInfo>>(okResult.Value);
        Assert.Equal(2, history.Count);
    }

    [Fact]
    public async Task GetTestPlan_WithException_ReturnsBadRequest()
    {
        // Arrange
        var request = new TestPlanRequest
        {
            ConfidenceLevel = ConfidenceLevel.Medium
        };

        _mockSelectionEngine
            .GetTestPlanAsync(Arg.Any<ConfidenceLevel>(), Arg.Any<TestSelectionOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Test error"));

        // Act
        var result = await _controller.GetTestPlan(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.NotNull(badRequestResult.Value);
    }
}