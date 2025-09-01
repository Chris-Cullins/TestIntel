using FluentAssertions;
using TestIntelligence.E2E.Tests.Helpers;
using TestIntelligence.E2E.Tests.Models;
using Xunit;

namespace TestIntelligence.E2E.Tests.Integration;

[Collection("E2E Tests")]
public class FullWorkflowE2ETests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    [Fact]
    public async Task FullWorkflow_AnalyzeThenFindTests_WorksEndToEnd()
    {
        // This test validates the complete workflow:
        // 1. Analyze the solution to discover all tests
        // 2. Use find-tests to locate tests for a specific method
        // 3. Verify the results are consistent and accurate

        // Arrange
        var solutionPath = GetTestSolutionPath();

        // Act & Assert - Step 1: Analyze the solution
        var analyzeResult = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>("analyze",
            $"--path \"{solutionPath}\"");

        analyzeResult.Should().NotBeNull();
        analyzeResult.Summary.TotalTestMethods.Should().BeGreaterThan(0);
        analyzeResult.TestAssemblies.Should().NotBeEmpty();

        // Act & Assert - Step 2: Find tests for a known method
        var knownMethod = "TestIntelligence.Core.Discovery.NUnitTestDiscovery.DiscoverTestsAsync";
        var findTestsResult = await CliTestHelper.RunCliCommandWithJsonOutputAsync<FindTestsJsonOutput>("find-tests",
            $"--method \"{knownMethod}\" --solution \"{solutionPath}\"");

        findTestsResult.Should().NotBeNull();
        findTestsResult.TargetMethod.Should().Be(knownMethod);
        
        // Verify that any found tests are valid
        foreach (var test in findTestsResult.Tests)
        {
            test.TestClassName.Should().NotBeNullOrEmpty();
            test.TestMethodName.Should().NotBeNullOrEmpty();
            test.TestAssembly.Should().NotBeNullOrEmpty();
            test.Confidence.Should().BeInRange(0.0, 1.0);
            test.CallDepth.Should().BeGreaterOrEqualTo(0);
        }
    }

    [Fact]
    public async Task FullWorkflow_CallGraphThenFindTests_ShowsConsistentResults()
    {
        // This test validates that call graph analysis and find-tests are consistent:
        // 1. Generate call graph to understand method relationships
        // 2. Use find-tests to find coverage for methods in the call graph
        // 3. Verify the results make sense together

        // Arrange
        var solutionPath = GetTestSolutionPath();

        // Act & Assert - Step 1: Generate call graph
        var callGraphResult = await CliTestHelper.RunCliCommandWithJsonOutputAsync<CallGraphJsonOutput>("callgraph",
            $"--path \"{solutionPath}\"");

        callGraphResult.Should().NotBeNull();
        callGraphResult.Summary.TotalMethods.Should().BeGreaterThan(0);

        // Act & Assert - Step 2: Pick a method from call graph and find its tests
        if (callGraphResult.Methods.Any())
        {
            var targetMethod = callGraphResult.Methods.First();
            var methodFullName = $"{targetMethod.ClassName}.{targetMethod.MethodName}";

            var findTestsResult = await CliTestHelper.RunCliCommandAsync("find-tests",
                $"--method \"{methodFullName}\" --solution \"{solutionPath}\"");

            findTestsResult.Success.Should().BeTrue();
            // Should either find tests or report no tests found - both are valid outcomes
            findTestsResult.StandardOutput.Should().Contain("Finding tests that exercise method:");
        }
    }

    [Fact]
    public async Task FullWorkflow_ErrorHandling_FailsGracefullyWithInvalidInputs()
    {
        // Test that all commands handle invalid inputs gracefully

        // Test with non-existent solution
        var invalidSolution = "/path/to/nonexistent/solution.sln";

        var analyzeResult = await CliTestHelper.RunCliCommandAsync("analyze", 
            $"--path \"{invalidSolution}\"");
        analyzeResult.Success.Should().BeFalse();
        analyzeResult.StandardError.Should().Contain("Error during analysis:");

        var callGraphResult = await CliTestHelper.RunCliCommandAsync("callgraph", 
            $"--path \"{invalidSolution}\"");
        callGraphResult.Success.Should().BeFalse();
        callGraphResult.StandardError.Should().Contain("Error analyzing call graph:");

        var findTestsResult = await CliTestHelper.RunCliCommandAsync("find-tests", 
            $"--method \"Some.Method\" --solution \"{invalidSolution}\"");
        findTestsResult.Success.Should().BeFalse();
        findTestsResult.StandardError.Should().Contain("Error finding tests:");
    }

    [Fact]
    public async Task FullWorkflow_OutputConsistency_JsonAndTextOutputsAreConsistent()
    {
        // Verify that JSON and text outputs contain the same core information

        // Arrange
        var solutionPath = GetTestSolutionPath();

        // Act - Get both text and JSON outputs for analyze command
        var textResult = await CliTestHelper.RunCliCommandAsync("analyze", 
            $"--path \"{solutionPath}\"");
        var jsonResult = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>("analyze",
            $"--path \"{solutionPath}\"");

        // Assert
        textResult.Success.Should().BeTrue();
        jsonResult.Should().NotBeNull();

        // Extract numbers from text output and compare with JSON
        var textOutput = textResult.StandardOutput;
        if (textOutput.Contains("Total test methods:"))
        {
            // Text should contain the same totals as JSON
            textOutput.Should().Contain(jsonResult.Summary.TotalTestMethods.ToString());
            textOutput.Should().Contain(jsonResult.Summary.TotalTestFixtures.ToString());
        }
    }

    [Fact]
    public async Task FullWorkflow_PerformanceBaseline_CompletesWithinReasonableTime()
    {
        // Ensure E2E operations complete within reasonable timeframes

        // Arrange
        var solutionPath = GetTestSolutionPath();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act - Run a basic analyze command
        var result = await CliTestHelper.RunCliCommandAsync("analyze", 
            $"--path \"{solutionPath}\"", timeoutMs: 60000);

        stopwatch.Stop();

        // Assert
        result.Success.Should().BeTrue();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(60000, "Analysis should complete within 60 seconds");
        
        // Log performance for monitoring
        Console.WriteLine($"Analysis completed in {stopwatch.ElapsedMilliseconds}ms");
    }

    private string GetTestSolutionPath()
    {
        var solutionPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, 
            "..", "..", "..", "..", "..", 
            "TestIntelligence.sln");
        
        return Path.GetFullPath(solutionPath);
    }

    private string CreateTempFile(string extension)
    {
        var tempFile = Path.GetTempFileName();
        var newFile = Path.ChangeExtension(tempFile, extension);
        File.Delete(tempFile);
        _tempFiles.Add(newFile);
        return newFile;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }
}