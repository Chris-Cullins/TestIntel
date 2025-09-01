using FluentAssertions;
using TestIntelligence.E2E.Tests.Helpers;
using TestIntelligence.E2E.Tests.Models;
using Xunit;

namespace TestIntelligence.E2E.Tests.Commands;

[Collection("E2E Tests")]
public class CallGraphCommandE2ETests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    [Fact]
    public async Task CallGraph_WithValidSolution_ReturnsCallGraphResults()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("callgraph", 
            $"--path \"{solutionPath}\"");

        // Assert
        result.Success.Should().BeTrue($"Command should succeed. Error: {result.StandardError}");
        result.StandardOutput.Should().Contain("Call Graph Analysis");
        result.StandardOutput.Should().Contain("Total methods analyzed:");
        result.StandardOutput.Should().Contain("Total call relationships:");
    }

    [Fact]
    public async Task CallGraph_WithJsonOutput_ReturnsValidJson()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();

        // Act
        var output = await CliTestHelper.RunCliCommandWithJsonOutputAsync<CallGraphJsonOutput>("callgraph",
            $"--path \"{solutionPath}\"");

        // Assert
        output.Should().NotBeNull();
        output.Summary.Should().NotBeNull();
        output.Summary.TotalMethods.Should().BeGreaterThan(0);
        output.AnalysisDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task CallGraph_WithVerboseOutput_IncludesMethodDetails()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("callgraph", 
            $"--path \"{solutionPath}\" --verbose");

        // Assert
        result.Success.Should().BeTrue();
        result.StandardOutput.Should().Contain("Method:");
        result.StandardOutput.Should().Contain("Calls:");
    }

    [Fact]
    public async Task CallGraph_WithMaxMethodsLimit_RespectsLimit()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();
        var maxMethods = 5;

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("callgraph", 
            $"--path \"{solutionPath}\" --verbose --max-methods {maxMethods}");

        // Assert
        result.Success.Should().BeTrue();
        
        // Count method entries in verbose output
        var methodCount = result.StandardOutput.Split("Method:").Length - 1;
        methodCount.Should().BeLessOrEqualTo(maxMethods);
    }

    [Fact]
    public async Task CallGraph_WithOutputFile_WritesToFile()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();
        var outputFile = CreateTempFile(".txt");

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("callgraph", 
            $"--path \"{solutionPath}\" --output \"{outputFile}\"");

        // Assert
        result.Success.Should().BeTrue();
        result.StandardOutput.Should().Contain($"Call graph analysis written to: {outputFile}");
        File.Exists(outputFile).Should().BeTrue();
        
        var fileContent = await File.ReadAllTextAsync(outputFile);
        fileContent.Should().Contain("Call Graph Analysis");
    }

    [Fact]
    public async Task CallGraph_WithJsonOutputFile_WritesValidJsonToFile()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();
        var outputFile = CreateTempFile(".json");

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("callgraph", 
            $"--path \"{solutionPath}\" --format json --output \"{outputFile}\"");

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(outputFile).Should().BeTrue();
        
        var fileContent = await File.ReadAllTextAsync(outputFile);
        fileContent.Should().NotBeEmpty();
        
        // Validate it's proper JSON
        var jsonOutput = System.Text.Json.JsonSerializer.Deserialize<CallGraphJsonOutput>(fileContent);
        jsonOutput.Should().NotBeNull();
        jsonOutput!.Summary.TotalMethods.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CallGraph_WithNonExistentPath_FailsGracefully()
    {
        // Arrange
        var invalidPath = "/path/to/nonexistent/solution.sln";

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("callgraph", 
            $"--path \"{invalidPath}\"");

        // Assert
        result.Success.Should().BeFalse();
        result.StandardError.Should().Contain("Error analyzing call graph:");
    }

    [Fact]
    public async Task CallGraph_WithMissingRequiredArguments_ShowsHelp()
    {
        // Act
        var result = await CliTestHelper.RunCliCommandAsync("callgraph", "");

        // Assert
        result.Success.Should().BeFalse();
        result.StandardError.Should().Contain("Required option");
        result.StandardError.Should().Contain("--path");
    }

    [Fact]
    public async Task CallGraph_CommandExists_InHelpOutput()
    {
        // Act
        var result = await CliTestHelper.RunCliCommandAsync("--help", "");

        // Assert
        result.Success.Should().BeTrue();
        result.StandardOutput.Should().Contain("callgraph");
        result.StandardOutput.Should().Contain("Analyze method call graph");
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