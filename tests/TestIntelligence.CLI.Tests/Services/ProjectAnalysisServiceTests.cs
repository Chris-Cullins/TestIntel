using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.CLI.Services;
using Xunit;

namespace TestIntelligence.CLI.Tests.Services;

/// <summary>
/// Tests for ProjectAnalysisService functionality.
/// </summary>
public class ProjectAnalysisServiceTests : IDisposable
{
    private readonly ILogger<ProjectAnalysisService> _mockLogger;
    private readonly ProjectAnalysisService _service;
    private readonly string _tempDirectory;

    public ProjectAnalysisServiceTests()
    {
        _mockLogger = Substitute.For<ILogger<ProjectAnalysisService>>();
        _service = new ProjectAnalysisService(_mockLogger);
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ProjectAnalysisService(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FindAllProjectsInSolutionAsync_WithInvalidSolutionPath_ThrowsArgumentException(string? solutionPath)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _service.FindAllProjectsInSolutionAsync(solutionPath!));
    }

    [Fact]
    public async Task FindAllProjectsInSolutionAsync_WithNonExistentSolutionFile_ReturnsEmptyList()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDirectory, "nonexistent.sln");

        // Act
        var result = await _service.FindAllProjectsInSolutionAsync(nonExistentPath);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAllProjectsInSolutionAsync_WithValidSolutionFile_ReturnsProjects()
    {
        // Arrange
        var solutionPath = Path.Combine(_tempDirectory, "test.sln");
        var projectPath1 = Path.Combine(_tempDirectory, "Project1", "Project1.csproj");
        var projectPath2 = Path.Combine(_tempDirectory, "Project2", "Project2.csproj");

        // Create directories and project files
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath1)!);
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath2)!);
        File.WriteAllText(projectPath1, CreateSampleProjectContent());
        File.WriteAllText(projectPath2, CreateSampleProjectContent());

        var solutionContent = $@"
Microsoft Visual Studio Solution File, Format Version 12.00
Project(""{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}"") = ""Project1"", ""Project1\Project1.csproj"", ""{{GUID1}}""
EndProject
Project(""{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}"") = ""Project2"", ""Project2\Project2.csproj"", ""{{GUID2}}""
EndProject
";
        File.WriteAllText(solutionPath, solutionContent);

        // Act
        var result = await _service.FindAllProjectsInSolutionAsync(solutionPath);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(projectPath1, result);
        Assert.Contains(projectPath2, result);
    }

    [Fact]
    public async Task FindAllProjectsInSolutionAsync_WithCorruptSolutionFile_HandlesExceptionGracefully()
    {
        // Arrange
        var solutionPath = Path.Combine(_tempDirectory, "corrupt.sln");
        File.WriteAllText(solutionPath, "This is not a valid solution file");

        // Act
        var result = await _service.FindAllProjectsInSolutionAsync(solutionPath);

        // Assert
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FindTestProjectsInSolutionAsync_WithInvalidSolutionPath_ThrowsArgumentException(string? solutionPath)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _service.FindTestProjectsInSolutionAsync(solutionPath!));
    }

    [Fact]
    public async Task FindTestProjectsInSolutionAsync_FiltersOnlyTestProjects()
    {
        // Arrange
        var solutionPath = Path.Combine(_tempDirectory, "test.sln");
        var testProjectPath = Path.Combine(_tempDirectory, "TestProject", "TestProject.csproj");
        var regularProjectPath = Path.Combine(_tempDirectory, "RegularProject", "RegularProject.csproj");

        // Create directories and project files
        Directory.CreateDirectory(Path.GetDirectoryName(testProjectPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(regularProjectPath)!);
        File.WriteAllText(testProjectPath, CreateTestProjectContent());
        File.WriteAllText(regularProjectPath, CreateSampleProjectContent());

        var solutionContent = $@"
Microsoft Visual Studio Solution File, Format Version 12.00
Project(""{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}"") = ""TestProject"", ""TestProject\TestProject.csproj"", ""{{GUID1}}""
EndProject
Project(""{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}"") = ""RegularProject"", ""RegularProject\RegularProject.csproj"", ""{{GUID2}}""
EndProject
";
        File.WriteAllText(solutionPath, solutionContent);

        // Act
        var result = await _service.FindTestProjectsInSolutionAsync(solutionPath);

        // Assert
        Assert.Single(result);
        Assert.Contains(testProjectPath, result);
        Assert.DoesNotContain(regularProjectPath, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IsTestProjectAsync_WithInvalidProjectPath_ThrowsArgumentException(string? projectPath)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _service.IsTestProjectAsync(projectPath!));
    }

    [Theory]
    [InlineData("ProjectTest.csproj")]
    [InlineData("TestProject.csproj")]
    [InlineData("Project.Tests.csproj")]
    [InlineData("MyProject.Spec.csproj")]
    [InlineData("myproject.test.csproj")]
    public async Task IsTestProjectAsync_WithTestProjectName_ReturnsTrue(string projectFileName)
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, projectFileName);
        File.WriteAllText(projectPath, CreateSampleProjectContent());

        // Act
        var result = await _service.IsTestProjectAsync(projectPath);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsTestProjectAsync_WithTestFrameworkReferences_ReturnsTrue()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "RegularProject.csproj");
        File.WriteAllText(projectPath, CreateTestProjectContent());

        // Act
        var result = await _service.IsTestProjectAsync(projectPath);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsTestProjectAsync_WithRegularProject_ReturnsFalse()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "RegularProject.csproj");
        File.WriteAllText(projectPath, CreateSampleProjectContent());

        // Act
        var result = await _service.IsTestProjectAsync(projectPath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsTestProjectAsync_WithNonExistentProject_ReturnsFalse()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDirectory, "NonExistent.csproj");

        // Act
        var result = await _service.IsTestProjectAsync(nonExistentPath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsTestProjectAsync_WithCorruptProjectFile_ReturnsFalse()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "Corrupt.csproj");
        File.WriteAllText(projectPath, "This is not a valid project file");

        // Act
        var result = await _service.IsTestProjectAsync(projectPath);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetTargetFrameworksAsync_WithInvalidProjectPath_ThrowsArgumentException(string? projectPath)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetTargetFrameworksAsync(projectPath!));
    }

    [Fact]
    public async Task GetTargetFrameworksAsync_WithNonExistentProject_ReturnsEmptyList()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDirectory, "NonExistent.csproj");

        // Act
        var result = await _service.GetTargetFrameworksAsync(nonExistentPath);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTargetFrameworksAsync_WithSingleTargetFramework_ReturnsFramework()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "SingleFramework.csproj");
        var projectContent = @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>";
        File.WriteAllText(projectPath, projectContent);

        // Act
        var result = await _service.GetTargetFrameworksAsync(projectPath);

        // Assert
        Assert.Single(result);
        Assert.Contains("net8.0", result);
    }

    [Fact]
    public async Task GetTargetFrameworksAsync_WithMultipleTargetFrameworks_ReturnsAllFrameworks()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "MultiFramework.csproj");
        var projectContent = @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>net6.0;net7.0;net8.0</TargetFrameworks>
  </PropertyGroup>
</Project>";
        File.WriteAllText(projectPath, projectContent);

        // Act
        var result = await _service.GetTargetFrameworksAsync(projectPath);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains("net6.0", result);
        Assert.Contains("net7.0", result);
        Assert.Contains("net8.0", result);
    }

    [Fact]
    public async Task GetTargetFrameworksAsync_WithBothSingleAndMultiple_ReturnsDistinctFrameworks()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "MixedFramework.csproj");
        var projectContent = @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <TargetFrameworks>net6.0;net7.0;net8.0</TargetFrameworks>
  </PropertyGroup>
</Project>";
        File.WriteAllText(projectPath, projectContent);

        // Act
        var result = await _service.GetTargetFrameworksAsync(projectPath);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains("net6.0", result);
        Assert.Contains("net7.0", result);
        Assert.Contains("net8.0", result);
    }

    [Fact]
    public async Task GetTargetFrameworksAsync_WithCorruptProjectFile_HandlesExceptionGracefully()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "Corrupt.csproj");
        File.WriteAllText(projectPath, "This is not a valid project file");

        // Act
        var result = await _service.GetTargetFrameworksAsync(projectPath);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTargetFrameworksAsync_WithNoTargetFramework_ReturnsEmptyList()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "NoFramework.csproj");
        var projectContent = @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
</Project>";
        File.WriteAllText(projectPath, projectContent);

        // Act
        var result = await _service.GetTargetFrameworksAsync(projectPath);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAllProjectsInSolutionAsync_WithMixedCaseExtensions_FindsAllProjects()
    {
        // Arrange
        var solutionPath = Path.Combine(_tempDirectory, "test.sln");
        var projectPath1 = Path.Combine(_tempDirectory, "Project1", "Project1.csproj");
        var projectPath2 = Path.Combine(_tempDirectory, "Project2", "Project2.CSPROJ");

        // Create directories and project files
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath1)!);
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath2)!);
        File.WriteAllText(projectPath1, CreateSampleProjectContent());
        File.WriteAllText(projectPath2, CreateSampleProjectContent());

        var solutionContent = $@"
Microsoft Visual Studio Solution File, Format Version 12.00
Project(""{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}"") = ""Project1"", ""Project1\Project1.csproj"", ""{{GUID1}}""
EndProject
Project(""{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}"") = ""Project2"", ""Project2\Project2.CSPROJ"", ""{{GUID2}}""
EndProject
";
        File.WriteAllText(solutionPath, solutionContent);

        // Act
        var result = await _service.FindAllProjectsInSolutionAsync(solutionPath);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(projectPath1, result);
        Assert.Contains(projectPath2, result);
    }

    [Theory]
    [InlineData("xunit")]
    [InlineData("nunit")]
    [InlineData("mstest")]
    [InlineData("Microsoft.NET.Test.Sdk")]
    [InlineData("FluentAssertions")]
    [InlineData("Moq")]
    [InlineData("NSubstitute")]
    [InlineData("FakeItEasy")]
    [InlineData("Shouldly")]
    public async Task IsTestProjectAsync_WithSpecificTestFramework_ReturnsTrue(string testFramework)
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "TestProject.csproj");
        var projectContent = $@"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""{testFramework}"" Version=""1.0.0"" />
  </ItemGroup>
</Project>";
        File.WriteAllText(projectPath, projectContent);

        // Act
        var result = await _service.IsTestProjectAsync(projectPath);

        // Assert
        Assert.True(result);
    }

    private static string CreateSampleProjectContent()
    {
        return @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Microsoft.Extensions.Logging"" Version=""8.0.0"" />
  </ItemGroup>
</Project>";
    }

    private static string CreateTestProjectContent()
    {
        return @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.0.0"" />
    <PackageReference Include=""xunit"" Version=""2.4.2"" />
    <PackageReference Include=""xunit.runner.visualstudio"" Version=""2.4.3"" />
    <PackageReference Include=""Moq"" Version=""4.18.4"" />
  </ItemGroup>
</Project>";
    }
}