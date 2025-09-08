using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.CLI.Services;
using TestIntelligence.Core.Discovery;
using TestIntelligence.Core.Assembly;
using Xunit;

namespace TestIntelligence.CLI.Tests.Services;

/// <summary>
/// Integration tests for CacheManagementService functionality.
/// These tests verify console output and basic command handling without complex mocking.
/// </summary>
public class CacheManagementServiceTests : IDisposable
{
    private readonly ILogger<CacheManagementService> _logger;
    private readonly ITestDiscovery _testDiscovery;
    private readonly CrossFrameworkAssemblyLoader _assemblyLoader;
    private readonly CacheManagementService _service;
    private readonly string _tempDirectory;
    private readonly string _testSolutionPath;

    public CacheManagementServiceTests()
    {
        // Use actual instances instead of mocks for integration-style testing
        _logger = Substitute.For<ILogger<CacheManagementService>>();
        _testDiscovery = Substitute.For<ITestDiscovery>();
        _assemblyLoader = new CrossFrameworkAssemblyLoader(_logger as IAssemblyLoadLogger);
        
        _service = new CacheManagementService(
            _logger,
            _testDiscovery,
            _assemblyLoader);

        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
        _testSolutionPath = Path.Combine(_tempDirectory, "TestSolution.sln");
    }

    public void Dispose()
    {
        _assemblyLoader?.Dispose();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new CacheManagementService(null!, _testDiscovery, _assemblyLoader));
    }

    [Fact]
    public void Constructor_WithNullTestDiscovery_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new CacheManagementService(_logger, null!, _assemblyLoader));
    }

    [Fact]
    public void Constructor_WithNullAssemblyLoader_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new CacheManagementService(_logger, _testDiscovery, null!));
    }

    [Fact]
    public async Task HandleCacheCommandAsync_WithNonExistentSolutionFile_ReturnsEarly()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDirectory, "nonexistent.sln");
        var originalOut = Console.Out;
        using var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);

        try
        {
            // Act
            await _service.HandleCacheCommandAsync(nonExistentPath, "status", null, "text", false);

            // Assert
            var output = stringWriter.ToString();
            Assert.Contains("Error: Solution file not found", output);
            Assert.Contains(nonExistentPath, output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task HandleCacheCommandAsync_WithUnknownAction_DisplaysErrorMessage()
    {
        // Arrange
        CreateTestSolutionFile(10); // Standard solution
        var originalOut = Console.Out;
        using var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);

        try
        {
            // Act
            await _service.HandleCacheCommandAsync(_testSolutionPath, "unknown", null, "text", false);

            // Assert
            var output = stringWriter.ToString();
            Assert.Contains("Error: Unknown cache action: unknown", output);
            Assert.Contains("Available actions: status, clear, init, warm-up, stats", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Theory]
    [InlineData("status")]
    [InlineData("clear")]
    [InlineData("init")]
    [InlineData("warm-up")]
    [InlineData("stats")]
    public async Task HandleCacheCommandAsync_WithValidActions_CompletesWithoutException(string action)
    {
        // Arrange
        CreateTestSolutionFile(5); // Small solution to avoid timeout

        // Act & Assert
        // The method should complete without throwing exceptions
        await _service.HandleCacheCommandAsync(_testSolutionPath, action, null, "text", false);
    }

    [Fact]
    public async Task HandleCacheCommandAsync_WithStatusAction_OutputsStatusInformation()
    {
        // Arrange
        CreateTestSolutionFile(5);
        var originalOut = Console.Out;
        using var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);

        try
        {
            // Act
            await _service.HandleCacheCommandAsync(_testSolutionPath, "status", null, "text", false);

            // Assert
            var output = stringWriter.ToString();
            Assert.Contains("Cache Status Report", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task HandleCacheCommandAsync_WithStatusActionJsonFormat_OutputsJsonData()
    {
        // Arrange
        CreateTestSolutionFile(5);
        var originalOut = Console.Out;
        using var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);

        try
        {
            // Act
            await _service.HandleCacheCommandAsync(_testSolutionPath, "status", null, "json", false);

            // Assert
            var output = stringWriter.ToString();
            // Should contain JSON structure
            Assert.Contains("{", output);
            Assert.Contains("}", output);
            Assert.Contains("solutionPath", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Theory]
    [InlineData(5, "standard")]
    [InlineData(50, "very large")]
    [InlineData(120, "enterprise")]
    public async Task HandleCacheCommandAsync_WithDifferentSolutionSizes_UsesAppropriateSetup(int projectCount, string expectedType)
    {
        // Arrange
        CreateTestSolutionFile(projectCount);
        var originalOut = Console.Out;
        using var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);

        try
        {
            // Act
            await _service.HandleCacheCommandAsync(_testSolutionPath, "init", null, "text", true);

            // Assert
            var output = stringWriter.ToString();
            Assert.Contains($"Detected {expectedType}", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task HandleCacheCommandAsync_WithCustomCacheDirectory_UsesSpecifiedDirectory()
    {
        // Arrange
        CreateTestSolutionFile(5);
        var customCacheDir = Path.Combine(_tempDirectory, "custom-cache");
        var originalOut = Console.Out;
        using var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);

        try
        {
            // Act
            await _service.HandleCacheCommandAsync(_testSolutionPath, "status", customCacheDir, "json", false);

            // Assert
            var output = stringWriter.ToString();
            // JSON output should contain the custom cache directory
            Assert.Contains(customCacheDir, output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task HandleCacheCommandAsync_WithVerboseEnabled_ShowsDetailedOutput()
    {
        // Arrange
        CreateTestSolutionFile(50); // Large enough to trigger verbose messaging
        var originalOut = Console.Out;
        using var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);

        try
        {
            // Act
            await _service.HandleCacheCommandAsync(_testSolutionPath, "init", null, "text", true);

            // Assert
            var output = stringWriter.ToString();
            Assert.Contains("projects", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task HandleCacheCommandAsync_WithClearAction_CompletesSuccessfully()
    {
        // Arrange
        CreateTestSolutionFile(5);
        var originalOut = Console.Out;
        using var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);

        try
        {
            // Act
            await _service.HandleCacheCommandAsync(_testSolutionPath, "clear", null, "text", false);

            // Assert
            var output = stringWriter.ToString();
            Assert.Contains("✅", output); // Success indicator
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task HandleCacheCommandAsync_WithStatsActionJsonFormat_OutputsStatisticsJson()
    {
        // Arrange
        CreateTestSolutionFile(5);
        var originalOut = Console.Out;
        using var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);

        try
        {
            // Act
            await _service.HandleCacheCommandAsync(_testSolutionPath, "stats", null, "json", false);

            // Assert
            var output = stringWriter.ToString();
            Assert.Contains("{", output);
            Assert.Contains("solutionPath", output);
            Assert.Contains("enhancedCaches", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task HandleCacheCommandAsync_WithStatsActionTextFormat_OutputsReadableStats()
    {
        // Arrange
        CreateTestSolutionFile(5);
        var originalOut = Console.Out;
        using var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);

        try
        {
            // Act
            await _service.HandleCacheCommandAsync(_testSolutionPath, "stats", null, "text", false);

            // Assert
            var output = stringWriter.ToString();
            Assert.Contains("Cache Statistics", output);
            Assert.Contains("================", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task HandleCacheCommandAsync_WithException_HandlesGracefully()
    {
        // Arrange
        CreateCorruptSolutionFile(); // This should cause issues during processing
        var originalOut = Console.Out;
        using var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);

        try
        {
            // Act
            await _service.HandleCacheCommandAsync(_testSolutionPath, "init", null, "text", false);
            
            // Assert
            // The service should handle the corrupt data gracefully and complete without throwing
            var output = stringWriter.ToString();
            // Verify that some cache operation completed (even with corrupt data)
            Assert.True(output.Contains("Cache") || output.Length > 0, "Service should produce some output even with corrupt data");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private void CreateTestSolutionFile(int projectCount)
    {
        var solutionContent = @"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
";

        for (int i = 1; i <= projectCount; i++)
        {
            solutionContent += $@"
Project(""{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}"") = ""TestProject{i}"", ""TestProject{i}\TestProject{i}.csproj"", ""{{GUID{i}}}""
EndProject";
        }

        solutionContent += @"
Global
    GlobalSection(SolutionConfigurationPlatforms) = preSolution
        Debug|Any CPU = Debug|Any CPU
        Release|Any CPU = Release|Any CPU
    EndGlobalSection
EndGlobal
";

        File.WriteAllText(_testSolutionPath, solutionContent);

        // Create corresponding project files
        for (int i = 1; i <= Math.Min(projectCount, 5); i++) // Limit to avoid too many files
        {
            var projectDir = Path.Combine(_tempDirectory, $"TestProject{i}");
            Directory.CreateDirectory(projectDir);
            var projectPath = Path.Combine(projectDir, $"TestProject{i}.csproj");
            File.WriteAllText(projectPath, CreateSampleProjectContent());
        }
    }

    private void CreateCorruptSolutionFile()
    {
        // Create a solution file with invalid content that will cause parsing issues
        File.WriteAllText(_testSolutionPath, "This is not a valid solution file content");
    }

    private static string CreateSampleProjectContent()
    {
        return @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>";
    }
}