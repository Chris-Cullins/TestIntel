using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace TestIntelligence.TestComparison.Tests;

/// <summary>
/// End-to-end CLI tests for the compare-tests command.
/// These tests verify that the CLI integration works correctly from command-line invocation.
/// </summary>
public class CompareTestsCliTests : IDisposable
{
    private readonly string _cliExecutablePath;
    private readonly string _testSolutionPath;
    private readonly string _outputDirectory;

    public CompareTestsCliTests()
    {
        // Set up paths for CLI testing
        _cliExecutablePath = GetCliExecutablePath();
        _testSolutionPath = GetTestSolutionPath();
        _outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "cli-test-outputs");
        
        // Create output directory for test files
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, true);
        }
        Directory.CreateDirectory(_outputDirectory);
    }

    public void Dispose()
    {
        // Clean up test output files
        if (Directory.Exists(_outputDirectory))
        {
            try
            {
                Directory.Delete(_outputDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact(Skip = "Integration test requiring test solution setup")]
    [Trait("Category", "CLI")]
    public async Task CompareTestsCommand_WithValidArguments_ShouldSucceed()
    {
        // Arrange
        var test1 = "TestIntelligence.Tests.UnitTests.ServiceTests.UserServiceTests.GetUserById_ValidId_ReturnsUser";
        var test2 = "TestIntelligence.Tests.UnitTests.ServiceTests.UserServiceTests.GetUserById_InvalidId_ReturnsNull";
        var args = new[]
        {
            "compare-tests",
            "--test1", test1,
            "--test2", test2,
            "--solution", _testSolutionPath,
            "--format", "text",
            "--depth", "medium"
        };

        // Act
        var result = await RunCliCommandAsync(args);

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Test Comparison Report", result.Output);
        Assert.Contains("Overall Similarity", result.Output);
        Assert.Contains("Coverage Overlap", result.Output);
        Assert.Contains(test1, result.Output);
        Assert.Contains(test2, result.Output);
    }

    [Fact(Skip = "Integration test requiring test solution setup")]
    [Trait("Category", "CLI")]
    public async Task CompareTestsCommand_WithJsonOutput_ShouldReturnValidJson()
    {
        // Arrange
        var outputFile = Path.Combine(_outputDirectory, "comparison-result.json");
        var test1 = "TestIntelligence.Tests.UnitTests.ValidationTests.EmailValidatorTests.IsValid_ValidEmail_ReturnsTrue";
        var test2 = "TestIntelligence.Tests.UnitTests.ValidationTests.EmailValidatorTests.IsValid_InvalidEmail_ReturnsFalse";
        var args = new[]
        {
            "compare-tests",
            "--test1", test1,
            "--test2", test2,
            "--solution", _testSolutionPath,
            "--format", "json",
            "--output", outputFile,
            "--verbose"
        };

        // Act
        var result = await RunCliCommandAsync(args);

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputFile));
        
        // Validate JSON structure
        var jsonContent = await File.ReadAllTextAsync(outputFile);
        // Verify JSON is valid
        var exception = Record.Exception(() => JsonDocument.Parse(jsonContent));
        Assert.Null(exception);
        
        using var doc = JsonDocument.Parse(jsonContent);
        var root = doc.RootElement;
        
        Assert.True(root.TryGetProperty("testComparison", out _));
        Assert.True(root.TryGetProperty("formatVersion", out _));
        Assert.True(root.TryGetProperty("generatedAt", out _));
        
        var comparison = root.GetProperty("testComparison");
        Assert.True(comparison.TryGetProperty("test1Id", out _));
        Assert.True(comparison.TryGetProperty("test2Id", out _));
        Assert.True(comparison.TryGetProperty("overallSimilarity", out _));
        Assert.True(comparison.TryGetProperty("coverageOverlap", out _));
        Assert.True(comparison.TryGetProperty("recommendations", out _));
    }

    [Fact(Skip = "Integration test requiring test solution setup")]
    [Trait("Category", "CLI")]
    public async Task CompareTestsCommand_WithVerboseOutput_ShouldIncludeDetailedInformation()
    {
        // Arrange
        var test1 = "TestIntelligence.Tests.UnitTests.BusinessLogicTests.OrderProcessorTests.ProcessOrder_ValidOrder_ReturnsSuccess";
        var test2 = "TestIntelligence.Tests.UnitTests.BusinessLogicTests.OrderProcessorTests.ProcessOrder_InvalidOrder_ReturnsError";
        var args = new[]
        {
            "compare-tests",
            "--test1", test1,
            "--test2", test2,
            "--solution", _testSolutionPath,
            "--format", "text",
            "--verbose",
            "--depth", "deep"
        };

        // Act
        var result = await RunCliCommandAsync(args);

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Shared Production Methods", result.Output);
        Assert.Contains("Optimization Recommendations", result.Output);
        Assert.Contains("Analysis completed successfully", result.Output);
    }

    [Fact(Skip = "Integration test requiring test solution setup")]
    [Trait("Category", "CLI")]
    public async Task CompareTestsCommand_WithPerformanceMetrics_ShouldIncludeTimingInfo()
    {
        // Arrange
        var test1 = "TestIntelligence.Tests.PerformanceTests.SortingTests.QuickSort_LargeArray_PerformsEfficiently";
        var test2 = "TestIntelligence.Tests.PerformanceTests.SortingTests.MergeSort_LargeArray_PerformsEfficiently";
        var args = new[]
        {
            "compare-tests",
            "--test1", test1,
            "--test2", test2,
            "--solution", _testSolutionPath,
            "--format", "text",
            "--include-performance",
            "--depth", "medium"
        };

        // Act
        var result = await RunCliCommandAsync(args);

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Performance Analysis", result.Output);
        Assert.Contains("Analysis Duration", result.Output);
        Assert.Contains("Analysis Depth", result.Output);
    }

    [Fact(Skip = "Integration test requiring test solution setup")]
    [Trait("Category", "CLI")]
    public async Task CompareTestsCommand_WithShallowDepth_ShouldCompleteQuickly()
    {
        // Arrange
        var test1 = "TestIntelligence.Tests.UnitTests.UtilityTests.StringHelperTests.IsEmpty_EmptyString_ReturnsTrue";
        var test2 = "TestIntelligence.Tests.UnitTests.UtilityTests.StringHelperTests.IsEmpty_NullString_ReturnsTrue";
        var args = new[]
        {
            "compare-tests",
            "--test1", test1,
            "--test2", test2,
            "--solution", _testSolutionPath,
            "--format", "text",
            "--depth", "shallow",
            "--timeout-seconds", "5"
        };

        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = await RunCliCommandAsync(args);

        // Assert
        stopwatch.Stop();
        Assert.Equal(0, result.ExitCode);
        Assert.True(stopwatch.ElapsedMilliseconds < 10000);
        Assert.Contains("Overall Similarity", result.Output);
    }

    [Fact(Skip = "Integration test requiring test solution setup")]
    [Trait("Category", "CLI")]
    public async Task CompareTestsCommand_WithMissingRequiredArguments_ShouldFail()
    {
        // Arrange - Missing test2 argument
        var args = new[]
        {
            "compare-tests",
            "--test1", "SomeTest",
            "--solution", _testSolutionPath
        };

        // Act
        var result = await RunCliCommandAsync(args);

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.Error.Contains("required", StringComparison.OrdinalIgnoreCase) || result.Error.Contains("missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Skip = "Integration test requiring test solution setup")]
    [Trait("Category", "CLI")]
    public async Task CompareTestsCommand_WithInvalidTestId_ShouldFailGracefully()
    {
        // Arrange
        var args = new[]
        {
            "compare-tests",
            "--test1", "Valid.Test.Method",
            "--test2", "Invalid.NonExistent.Test.Method",
            "--solution", _testSolutionPath,
            "--format", "text"
        };

        // Act
        var result = await RunCliCommandAsync(args);

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.Error.Contains("test method not found", StringComparison.OrdinalIgnoreCase) || result.Error.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Skip = "Integration test requiring test solution setup")]
    [Trait("Category", "CLI")]
    public async Task CompareTestsCommand_WithInvalidSolutionPath_ShouldFailGracefully()
    {
        // Arrange
        var args = new[]
        {
            "compare-tests",
            "--test1", "Test.Method.One",
            "--test2", "Test.Method.Two",
            "--solution", "/path/to/nonexistent/solution.sln",
            "--format", "text"
        };

        // Act
        var result = await RunCliCommandAsync(args);

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.Error.Contains("not found", StringComparison.OrdinalIgnoreCase) || result.Error.Contains("path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Skip = "Integration test requiring test solution setup")]
    [Trait("Category", "CLI")]
    public async Task CompareTestsCommand_WithTimeout_ShouldRespectTimeLimit()
    {
        // Arrange - Set very short timeout
        var test1 = "TestIntelligence.Tests.IntegrationTests.SlowTests.DatabaseMigration_LargeDataset_CompletesSuccessfully";
        var test2 = "TestIntelligence.Tests.IntegrationTests.SlowTests.DataImport_LargeFile_ProcessesCorrectly";
        var args = new[]
        {
            "compare-tests",
            "--test1", test1,
            "--test2", test2,
            "--solution", _testSolutionPath,
            "--format", "text",
            "--depth", "deep",
            "--timeout-seconds", "1" // Very short timeout
        };

        // Act
        var result = await RunCliCommandAsync(args, timeoutMs: 5000);

        // Assert
        Assert.Equal(124, result.ExitCode); // 124 is standard timeout exit code
        Assert.Contains("timed out", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = "Integration test requiring test solution setup")]
    [Trait("Category", "CLI")]
    public async Task CompareTestsCommand_WithHelp_ShouldDisplayUsageInformation()
    {
        // Arrange
        var args = new[] { "compare-tests", "--help" };

        // Act
        var result = await RunCliCommandAsync(args);

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("compare-tests", result.Output);
        Assert.Contains("--test1", result.Output);
        Assert.Contains("--test2", result.Output);
        Assert.Contains("--solution", result.Output);
        Assert.Contains("format", result.Output);
        Assert.Contains("depth", result.Output);
    }

    /// <summary>
    /// Helper method to run CLI command and capture output.
    /// </summary>
    private async Task<CliResult> RunCliCommandAsync(string[] args, int timeoutMs = 30000)
    {
        string fileName;
        string arguments;
        
        if (_cliExecutablePath == "dotnet")
        {
            // Find the DLL path
            var currentDirectory = Directory.GetCurrentDirectory();
            var dllPath = Path.GetFullPath(Path.Combine(currentDirectory, "..", "..", "..", "..", "..", "..", "src", "TestIntelligence.CLI", "bin", "Debug", "net8.0", "TestIntelligence.CLI.dll"));
            
            if (!File.Exists(dllPath))
            {
                // Try release build
                dllPath = Path.GetFullPath(Path.Combine(currentDirectory, "..", "..", "..", "..", "..", "..", "src", "TestIntelligence.CLI", "bin", "Release", "net8.0", "TestIntelligence.CLI.dll"));
            }
            
            fileName = "dotnet";
            arguments = $"\"{dllPath}\" {string.Join(" ", args.Select(arg => arg.Contains(" ") ? $"\"{arg}\"" : arg))}";
        }
        else
        {
            fileName = _cliExecutablePath;
            arguments = string.Join(" ", args.Select(arg => arg.Contains(" ") ? $"\"{arg}\"" : arg));
        }
        
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var tcs = new TaskCompletionSource<bool>();
        process.Exited += (s, e) => tcs.TrySetResult(true);
        process.EnableRaisingEvents = true;
        
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)) == tcs.Task;
        
        if (!completed)
        {
            process.Kill(true);
            return new CliResult(124, "Process timed out", "Process was killed due to timeout");
        }

        return new CliResult(process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
    }

    /// <summary>
    /// Gets the path to the CLI executable for testing.
    /// </summary>
    private string GetCliExecutablePath()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        
        // Look for the TestIntelligence.CLI directly since it's an executable on macOS/Linux
        var possiblePaths = new[]
        {
            Path.Combine(currentDirectory, "..", "..", "..", "..", "..", "..", "src", "TestIntelligence.CLI", "bin", "Debug", "net8.0", "TestIntelligence.CLI"),
            Path.Combine(currentDirectory, "..", "..", "..", "..", "..", "..", "src", "TestIntelligence.CLI", "bin", "Debug", "net8.0", "TestIntelligence.CLI.dll"),
            Path.Combine(currentDirectory, "..", "..", "..", "..", "..", "..", "src", "TestIntelligence.CLI", "bin", "Release", "net8.0", "TestIntelligence.CLI"),
            Path.Combine(currentDirectory, "..", "..", "..", "..", "..", "..", "src", "TestIntelligence.CLI", "bin", "Release", "net8.0", "TestIntelligence.CLI.dll")
        };

        // Check for the executable (macOS/Linux)
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                // If it's the DLL, use dotnet to run it
                if (path.EndsWith(".dll"))
                {
                    return $"dotnet";  // Will use dotnet with DLL as argument
                }
                return Path.GetFullPath(path);
            }
        }

        // Fallback: try to use dotnet with the DLL
        return "dotnet";
    }

    /// <summary>
    /// Gets the path to a test solution for CLI testing.
    /// </summary>
    private string GetTestSolutionPath()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var solutionPath = Path.Combine(currentDirectory, "..", "..", "..", "..", "..", "TestIntel-TestCompare.sln");
        
        if (File.Exists(solutionPath))
        {
            return Path.GetFullPath(solutionPath);
        }
        
        return currentDirectory; // Fallback to current directory
    }

    /// <summary>
    /// Result of a CLI command execution.
    /// </summary>
    private record CliResult(int ExitCode, string Output, string Error);
}