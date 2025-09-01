using FluentAssertions;
using TestIntelligence.E2E.Tests.Helpers;
using TestIntelligence.E2E.Tests.Models;
using Xunit;

namespace TestIntelligence.E2E.Tests.Commands;

[Collection("E2E Tests")]
public class AnalyzeCommandE2ETests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    [Fact]
    public async Task Analyze_WithValidSolution_ReturnsAnalysisResults()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("analyze", 
            $"--path \"{solutionPath}\"");

        // Assert
        result.Success.Should().BeTrue($"Command should succeed. Error: {result.StandardError}");
        result.StandardOutput.Should().Contain("Test Assembly Analysis");
        result.StandardOutput.Should().Contain("Total test methods:");
        result.StandardOutput.Should().Contain("Total test fixtures:");
    }

    [Fact]
    public async Task Analyze_WithJsonOutput_ReturnsValidJson()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();

        // Act
        var output = await CliTestHelper.RunCliCommandWithJsonOutputAsync<AnalyzeJsonOutput>("analyze",
            $"--path \"{solutionPath}\"");

        // Assert
        output.Should().NotBeNull();
        output.Summary.Should().NotBeNull();
        output.Summary.TotalTestMethods.Should().BeGreaterThan(0);
        output.Summary.TotalTestFixtures.Should().BeGreaterThan(0);
        output.Summary.TotalAssemblies.Should().BeGreaterThan(0);
        output.TestAssemblies.Should().NotBeEmpty();
        output.AnalysisDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Analyze_WithVerboseOutput_IncludesDetailedInformation()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("analyze", 
            $"--path \"{solutionPath}\" --verbose");

        // Assert
        result.Success.Should().BeTrue();
        result.StandardOutput.Should().Contain("Assembly:");
        result.StandardOutput.Should().Contain("Test methods:");
    }

    [Fact]
    public async Task Analyze_WithOutputFile_WritesToFile()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();
        var outputFile = CreateTempFile(".txt");

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("analyze", 
            $"--path \"{solutionPath}\" --output \"{outputFile}\"");

        // Assert
        result.Success.Should().BeTrue();
        result.StandardOutput.Should().Contain($"Analysis results written to: {outputFile}");
        File.Exists(outputFile).Should().BeTrue();
        
        var fileContent = await File.ReadAllTextAsync(outputFile);
        fileContent.Should().Contain("Test Assembly Analysis");
    }

    [Fact]
    public async Task Analyze_WithJsonOutputFile_WritesValidJsonToFile()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();
        var outputFile = CreateTempFile(".json");

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("analyze", 
            $"--path \"{solutionPath}\" --format json --output \"{outputFile}\"");

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(outputFile).Should().BeTrue();
        
        var fileContent = await File.ReadAllTextAsync(outputFile);
        fileContent.Should().NotBeEmpty();
        
        // Validate it's proper JSON
        var jsonOutput = System.Text.Json.JsonSerializer.Deserialize<AnalyzeJsonOutput>(fileContent);
        jsonOutput.Should().NotBeNull();
        jsonOutput!.Summary.TotalTestMethods.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Analyze_WithNonExistentPath_FailsGracefully()
    {
        // Arrange
        var invalidPath = "/path/to/nonexistent/solution.sln";

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("analyze", 
            $"--path \"{invalidPath}\"");

        // Assert
        result.Success.Should().BeFalse();
        result.StandardError.Should().Contain("Error during analysis:");
    }

    [Fact]
    public async Task Analyze_WithMissingRequiredArguments_ShowsHelp()
    {
        // Act
        var result = await CliTestHelper.RunCliCommandAsync("analyze", "");

        // Assert
        result.Success.Should().BeFalse();
        result.StandardError.Should().Contain("Required option");
        result.StandardError.Should().Contain("--path");
    }

    [Fact]
    public async Task Analyze_CommandExists_InHelpOutput()
    {
        // Act
        var result = await CliTestHelper.RunCliCommandAsync("--help", "");

        // Assert
        result.Success.Should().BeTrue();
        result.StandardOutput.Should().Contain("analyze");
        result.StandardOutput.Should().Contain("Analyze test assemblies");
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