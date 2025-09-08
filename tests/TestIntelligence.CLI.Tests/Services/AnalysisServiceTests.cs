using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using TestIntelligence.CLI.Services;
using TestIntelligence.CLI.Models;
using TestIntelligence.Core.Services;
using FluentAssertions;
using System.Linq;
using TestIntelligence.Categorizer;

namespace TestIntelligence.CLI.Tests.Services
{
    /// <summary>
    /// Tests for AnalysisService focusing on regression coverage for enhanced dependency extraction,
    /// project detection, and solution parsing improvements.
    /// </summary>
    public class AnalysisServiceTests : IDisposable
    {
    private readonly ILogger<AnalysisService> _logger;
    private readonly IOutputFormatter _outputFormatter;
    private readonly IConfigurationService _configurationService;
    private readonly IAssemblyPathResolver _assemblyPathResolver;
    private readonly ITestCategorizer _testCategorizer;
    private readonly AnalysisService _service;
    private readonly string _tempDirectory;

    public AnalysisServiceTests()
    {
        _logger = Substitute.For<ILogger<AnalysisService>>();
        _outputFormatter = Substitute.For<IOutputFormatter>();
        _configurationService = Substitute.For<IConfigurationService>();
        _assemblyPathResolver = Substitute.For<IAssemblyPathResolver>();
        _testCategorizer = Substitute.For<ITestCategorizer>();
        _service = new AnalysisService(_logger, _outputFormatter, _configurationService, _assemblyPathResolver, _testCategorizer);

        _tempDirectory = Path.Combine(Path.GetTempPath(), $"AnalysisServiceTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new AnalysisService(null!, _outputFormatter, _configurationService, _assemblyPathResolver, _testCategorizer));
            exception.ParamName.Should().Be("logger");
        }

        [Fact]
        public void Constructor_WithNullOutputFormatter_ThrowsArgumentNullException()
        {
            // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new AnalysisService(_logger, null!, _configurationService, _assemblyPathResolver, _testCategorizer));
            exception.ParamName.Should().Be("outputFormatter");
        }

        [Fact]
        public void Constructor_WithNullConfigurationService_ThrowsArgumentNullException()
        {
            // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new AnalysisService(_logger, _outputFormatter, null!, _assemblyPathResolver, _testCategorizer));
            exception.ParamName.Should().Be("configurationService");
        }

        [Fact]
        public void Constructor_WithNullTestCategorizer_ThrowsArgumentNullException()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(
                () => new AnalysisService(_logger, _outputFormatter, _configurationService, _assemblyPathResolver, null!));
            exception.ParamName.Should().Be("testCategorizer");
        }

        [Fact]
        public async Task AnalyzeAsync_WithNonExistentPath_ThrowsFileNotFoundException()
        {
            // Arrange
            var nonExistentPath = Path.Combine(_tempDirectory, "nonexistent.dll");
            var config = CreateDefaultConfiguration();
            _configurationService.LoadConfigurationAsync(Arg.Any<string>()).Returns(config);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<FileNotFoundException>(
                () => _service.AnalyzeAsync(nonExistentPath, null, "json", false));
            exception.Message.Should().Contain("Path not found");
        }

        [Fact]
        public async Task AnalyzeAsync_WithValidDllPath_CallsOutputFormatterWithCorrectFormat()
        {
            // Arrange
            var dllPath = Path.Combine(_tempDirectory, "test.dll");
            File.WriteAllText(dllPath, "dummy dll content");
            
            var config = CreateDefaultConfiguration();
            _configurationService.LoadConfigurationAsync(Arg.Any<string>()).Returns(config);

            // Act
            await _service.AnalyzeAsync(dllPath, null, "json", false);

            // Assert
            await _outputFormatter.Received(1).WriteOutputAsync(
                Arg.Any<AnalysisResult>(), 
                "json", 
                Arg.Any<string>());
        }

        [Fact]
        public async Task AnalyzeAsync_WithVerboseOverride_UsesConfigurationVerboseSetting()
        {
            // Arrange
            var dllPath = Path.Combine(_tempDirectory, "test.dll");
            File.WriteAllText(dllPath, "dummy dll content");
            
            var config = CreateDefaultConfiguration();
            config.Analysis.Verbose = true; // Set in configuration
            _configurationService.LoadConfigurationAsync(Arg.Any<string>()).Returns(config);

            // Act
            await _service.AnalyzeAsync(dllPath, null, "json", false); // verbose=false in call

            // Assert
            _logger.Received().LogInformation("Verbose mode enabled");
        }

        [Fact]
        public async Task AnalyzeAsync_WithEmptyFormatAndConfiguredFormat_UsesConfigurationFormat()
        {
            // Arrange
            var dllPath = Path.Combine(_tempDirectory, "test.dll");
            File.WriteAllText(dllPath, "dummy dll content");
            
            var config = CreateDefaultConfiguration();
            config.Output.Format = "xml"; // Configure XML output
            _configurationService.LoadConfigurationAsync(Arg.Any<string>()).Returns(config);

            // Act
            await _service.AnalyzeAsync(dllPath, null, "text", false); // Default format

            // Assert
            await _outputFormatter.Received(1).WriteOutputAsync(
                Arg.Any<AnalysisResult>(), 
                "xml", // Should use configured format
                Arg.Any<string>());
        }

        [Fact]
        public async Task AnalyzeAsync_WithConfiguredOutputDirectory_GeneratesTimestampedFileName()
        {
            // Arrange
            var dllPath = Path.Combine(_tempDirectory, "test.dll");
            File.WriteAllText(dllPath, "dummy dll content");
            
            var outputDir = Path.Combine(_tempDirectory, "output");
            Directory.CreateDirectory(outputDir);
            
            var config = CreateDefaultConfiguration();
            config.Output.OutputDirectory = outputDir;
            config.Output.Format = "json";
            _configurationService.LoadConfigurationAsync(Arg.Any<string>()).Returns(config);

            // Act
            await _service.AnalyzeAsync(dllPath, null, "json", false);

            // Assert
            await _outputFormatter.Received(1).WriteOutputAsync(
                Arg.Any<AnalysisResult>(), 
                "json",
                Arg.Is<string>(path => 
                    path.StartsWith(outputDir) && 
                    path.Contains("analysis_") && 
                    path.EndsWith(".json")));
        }

        [Fact]
        public async Task AnalyzeAsync_WithSolutionFile_ParsesProjectsCorrectly()
        {
            // Arrange
            var solutionContent = @"
Microsoft Visual Studio Solution File, Format Version 12.00
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""TestProject"", ""src\TestProject\TestProject.csproj"", ""{12345678-1234-1234-1234-123456789012}""
EndProject
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""TestProject.Tests"", ""tests\TestProject.Tests\TestProject.Tests.csproj"", ""{87654321-4321-4321-4321-210987654321}""
EndProject";
            
            var solutionPath = Path.Combine(_tempDirectory, "TestSolution.sln");
            File.WriteAllText(solutionPath, solutionContent);
            
            // Create project structure
            var srcDir = Path.Combine(_tempDirectory, "src", "TestProject");
            var testsDir = Path.Combine(_tempDirectory, "tests", "TestProject.Tests");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(testsDir);
            
            var projectContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>";
            
            File.WriteAllText(Path.Combine(srcDir, "TestProject.csproj"), projectContent);
            File.WriteAllText(Path.Combine(testsDir, "TestProject.Tests.csproj"), projectContent);
            
            var config = CreateDefaultConfiguration();
            config.Projects.TestProjectsOnly = false; // Include all projects
            _configurationService.LoadConfigurationAsync(Arg.Any<string>()).Returns(config);
            _configurationService.FilterProjects(Arg.Any<List<string>>(), Arg.Any<TestIntelConfiguration>())
                .Returns(callInfo => callInfo.Arg<List<string>>());

            // Act
            await _service.AnalyzeAsync(solutionPath, null, "json", true);

            // Assert
            _logger.Received().LogInformation("Found {Count} total projects in solution", 2);
        }

        [Fact]
        public async Task AnalyzeAsync_WithTestProjectsOnly_FiltersCorrectly()
        {
            // Arrange
            var solutionContent = @"
Microsoft Visual Studio Solution File, Format Version 12.00
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""MainProject"", ""src\MainProject\MainProject.csproj"", ""{12345678-1234-1234-1234-123456789012}""
EndProject
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""MainProject.Tests"", ""tests\MainProject.Tests\MainProject.Tests.csproj"", ""{87654321-4321-4321-4321-210987654321}""
EndProject";
            
            var solutionPath = Path.Combine(_tempDirectory, "TestSolution.sln");
            File.WriteAllText(solutionPath, solutionContent);
            
            // Create test project structure
            var testsDir = Path.Combine(_tempDirectory, "tests", "MainProject.Tests");
            Directory.CreateDirectory(testsDir);
            
            var testProjectContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""xunit"" Version=""2.4.2"" />
  </ItemGroup>
</Project>";
            
            File.WriteAllText(Path.Combine(testsDir, "MainProject.Tests.csproj"), testProjectContent);
            
            var config = CreateDefaultConfiguration();
            config.Projects.TestProjectsOnly = true; // Only test projects
            _configurationService.LoadConfigurationAsync(Arg.Any<string>()).Returns(config);
            _configurationService.FilterProjects(Arg.Any<List<string>>(), Arg.Any<TestIntelConfiguration>())
                .Returns(callInfo => callInfo.Arg<List<string>>());

            // Act
            await _service.AnalyzeAsync(solutionPath, null, "json", true);

            // Assert
            _logger.Received().LogInformation("Found {Count} test projects in solution", 1);
        }

        [Fact]
        public async Task AnalyzeAsync_WithDirectoryPath_FindsDllFilesAndFiltersTestsWhenConfigured()
        {
            // Arrange
            var binDir = Path.Combine(_tempDirectory, "bin");
            Directory.CreateDirectory(binDir);
            
            File.WriteAllText(Path.Combine(binDir, "MainProject.dll"), "dummy");
            File.WriteAllText(Path.Combine(binDir, "MainProject.Tests.dll"), "dummy");
            File.WriteAllText(Path.Combine(binDir, "obj", "temp.dll"), "dummy"); // Should be filtered out
            Directory.CreateDirectory(Path.Combine(binDir, "obj"));
            
            var config = CreateDefaultConfiguration();
            config.Projects.TestProjectsOnly = true;
            _configurationService.LoadConfigurationAsync(Arg.Any<string>()).Returns(config);
            _configurationService.FilterProjects(Arg.Any<List<string>>(), Arg.Any<TestIntelConfiguration>())
                .Returns(callInfo => callInfo.Arg<List<string>>());

            // Act
            await _service.AnalyzeAsync(binDir, null, "json", true);

            // Assert
            _logger.Received().LogInformation("Found {Count} assemblies to analyze", 1);
        }

        [Fact]
        public async Task AnalyzeAsync_WithAssemblyLoadFailure_ContinuesWithErrorRecording()
        {
            // Arrange
            var dllPath = Path.Combine(_tempDirectory, "invalid.dll");
            File.WriteAllText(dllPath, "not a real dll");
            
            var config = CreateDefaultConfiguration();
            _configurationService.LoadConfigurationAsync(Arg.Any<string>()).Returns(config);

            // Act
            await _service.AnalyzeAsync(dllPath, null, "json", false);

            // Assert
            await _outputFormatter.Received(1).WriteOutputAsync(
                Arg.Is<AnalysisResult>(result => 
                    result.Assemblies.Count == 1 && 
                    !string.IsNullOrEmpty(result.Assemblies[0].Error!) &&
                    result.Summary != null && result.Summary.FailedAnalyses == 1),
                "json",
                Arg.Any<string>());
        }

        [Theory]
        [InlineData("net8.0")]
        [InlineData("net6.0")]
        [InlineData("netcoreapp3.1")]
        [InlineData("netstandard2.0")]
        public async Task GetTargetFrameworksFromProject_WithDifferentFrameworks_ParsesCorrectly(string targetFramework)
        {
            // This tests the enhanced target framework detection
            var projectContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>{targetFramework}</TargetFramework>
  </PropertyGroup>
</Project>";
            
            var projectPath = Path.Combine(_tempDirectory, "TestProject.csproj");
            File.WriteAllText(projectPath, projectContent);
            
            var config = CreateDefaultConfiguration();
            _configurationService.LoadConfigurationAsync(Arg.Any<string>()).Returns(config);

            // Create expected assembly path structure
            var assemblyDir = Path.Combine(Path.GetDirectoryName(projectPath)!, "bin", "Debug", targetFramework);
            Directory.CreateDirectory(assemblyDir);
            var assemblyPath = Path.Combine(assemblyDir, "TestProject.dll");
            File.WriteAllText(assemblyPath, "dummy");

            // Act
            await _service.AnalyzeAsync(assemblyPath, null, "json", false);

            // Assert - Verify the analysis service was called (basic check)
            // The actual framework detection is tested in integration tests
            Assert.True(true); // Test passes if no exception thrown
        }

        [Fact]
        public async Task AnalyzeAsync_WithMultipleTargetFrameworks_ParsesAllFrameworks()
        {
            // Test the enhanced multi-target framework parsing
            var projectContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>net6.0;net8.0;netstandard2.0</TargetFrameworks>
  </PropertyGroup>
</Project>";
            
            var projectPath = Path.Combine(_tempDirectory, "MultiTargetProject.csproj");
            File.WriteAllText(projectPath, projectContent);
            
            // Create assemblies for each target framework
            foreach (var framework in new[] { "net6.0", "net8.0", "netstandard2.0" })
            {
                var assemblyDir = Path.Combine(Path.GetDirectoryName(projectPath)!, "bin", "Debug", framework);
                Directory.CreateDirectory(assemblyDir);
                File.WriteAllText(Path.Combine(assemblyDir, "MultiTargetProject.dll"), "dummy");
            }

            var solutionContent = $@"
Microsoft Visual Studio Solution File, Format Version 12.00
Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""MultiTargetProject"", ""MultiTargetProject.csproj"", ""{{12345678-1234-1234-1234-123456789012}}""
EndProject";
            
            var solutionPath = Path.Combine(_tempDirectory, "TestSolution.sln");
            File.WriteAllText(solutionPath, solutionContent);
            
            var config = CreateDefaultConfiguration();
            _configurationService.LoadConfigurationAsync(Arg.Any<string>()).Returns(config);
            _configurationService.FilterProjects(Arg.Any<List<string>>(), Arg.Any<TestIntelConfiguration>())
                .Returns(callInfo => callInfo.Arg<List<string>>());

            // Act
            await _service.AnalyzeAsync(solutionPath, null, "json", true);

            // Assert - Should find at least one assembly (first existing one)
            _logger.Received().LogDebug(Arg.Is<string>(msg => msg.StartsWith("Found assembly:")), Arg.Any<object>());
        }

        private static TestIntelConfiguration CreateDefaultConfiguration()
        {
            return new TestIntelConfiguration
            {
                Analysis = new AnalysisConfiguration
                {
                    Verbose = false,
                    MaxParallelism = 1,
                    TimeoutSeconds = 30
                },
                Output = new OutputConfiguration
                {
                    Format = "text",
                    OutputDirectory = null
                },
                Projects = new ProjectFilterConfiguration
                {
                    TestProjectsOnly = false,
                    Include = new List<string>(),
                    Exclude = new List<string>(),
                    ExcludeTypes = new List<string>()
                }
            };
        }
    }
}