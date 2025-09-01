using System.Diagnostics;
using FluentAssertions;
using TestIntelligence.E2E.Tests.Helpers;
using TestIntelligence.E2E.Tests.Models;
using Xunit;

namespace TestIntelligence.E2E.Tests.Commands;

[Collection("E2E Tests")]
public class FindTestsCommandE2ETests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    [Fact]
    public async Task FindTests_WithValidMethod_ReturnsExpectedResults()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();
        var methodName = "TestIntelligence.Core.Discovery.NUnitTestDiscovery.DiscoverTestsAsync";

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("find-tests", 
            $"--method \"{methodName}\" --solution \"{solutionPath}\"");

        // Assert
        result.Success.Should().BeTrue($"Command should succeed. Error: {result.StandardError}");
        result.StandardOutput.Should().Contain("Finding tests that exercise method:");
        result.StandardOutput.Should().Contain(methodName);
    }

    [Fact]
    public async Task FindTests_WithJsonOutput_ReturnsValidJson()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();
        var methodName = "TestIntelligence.Core.Discovery.NUnitTestDiscovery.DiscoverTestsAsync";

        // Act
        var output = await CliTestHelper.RunCliCommandWithJsonOutputAsync<FindTestsJsonOutput>("find-tests",
            $"--method \"{methodName}\" --solution \"{solutionPath}\"");

        // Assert
        output.Should().NotBeNull();
        output.TargetMethod.Should().Be(methodName);
        output.AnalysisDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task FindTests_WithVerboseOutput_IncludesCallPaths()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();
        var methodName = "TestIntelligence.Core.Discovery.NUnitTestDiscovery.DiscoverTestsAsync";

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("find-tests", 
            $"--method \"{methodName}\" --solution \"{solutionPath}\" --verbose");

        // Assert
        result.Success.Should().BeTrue();
        if (result.StandardOutput.Contains("Found") && !result.StandardOutput.Contains("No tests found"))
        {
            result.StandardOutput.Should().Contain("Call Path:");
        }
    }

    [Fact]
    public async Task FindTests_WithOutputFile_WritesToFile()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();
        var methodName = "TestIntelligence.Core.Discovery.NUnitTestDiscovery.DiscoverTestsAsync";
        var outputFile = CreateTempFile(".txt");

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("find-tests", 
            $"--method \"{methodName}\" --solution \"{solutionPath}\" --output \"{outputFile}\"");

        // Assert
        result.Success.Should().BeTrue();
        result.StandardOutput.Should().Contain($"Results written to: {outputFile}");
        File.Exists(outputFile).Should().BeTrue();
        
        var fileContent = await File.ReadAllTextAsync(outputFile);
        fileContent.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FindTests_WithNonExistentMethod_ReturnsNoResults()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();
        var methodName = "NonExistent.Class.NonExistentMethod";

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("find-tests", 
            $"--method \"{methodName}\" --solution \"{solutionPath}\"");

        // Assert
        result.Success.Should().BeTrue();
        result.StandardOutput.Should().Contain("No tests found that exercise this method");
    }

    [Fact]
    public async Task FindTests_WithInvalidSolution_FailsGracefully()
    {
        // Arrange
        var invalidSolutionPath = "/path/to/nonexistent/solution.sln";
        var methodName = "Some.Method";

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("find-tests", 
            $"--method \"{methodName}\" --solution \"{invalidSolutionPath}\"");

        // Assert
        result.Success.Should().BeFalse();
        result.StandardError.Should().Contain("Error finding tests:");
    }

    [Fact]
    public async Task FindTests_WithMissingRequiredArguments_ShowsHelp()
    {
        // Act
        var result = await CliTestHelper.RunCliCommandAsync("find-tests", "");

        // Assert
        result.Success.Should().BeFalse();
        result.StandardError.Should().Contain("Required option");
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

[CollectionDefinition("E2E Tests")]
public class E2ETestCollection : ICollectionFixture<E2ETestFixture>
{
}

public class E2ETestFixture : IDisposable
{
    public E2ETestFixture()
    {
        EnsureCliIsBuilt();
    }

    private void EnsureCliIsBuilt()
    {
        var solutionPath = FindSolutionPath();
        if (solutionPath != null)
        {
            var buildProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{solutionPath}\" --configuration Debug",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            buildProcess?.WaitForExit();
            
            if (buildProcess?.ExitCode != 0)
            {
                throw new InvalidOperationException("Failed to build CLI for E2E tests");
            }
        }
    }

    private string? FindSolutionPath()
    {
        var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        
        while (current != null)
        {
            var solutionFiles = current.GetFiles("*.sln");
            if (solutionFiles.Any())
                return solutionFiles.First().FullName;
            current = current.Parent;
        }
        
        return null;
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}