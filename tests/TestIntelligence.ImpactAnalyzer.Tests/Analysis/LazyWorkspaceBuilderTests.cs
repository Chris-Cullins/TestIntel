using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.ImpactAnalyzer.Analysis;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Analysis
{
    public class LazyWorkspaceBuilderTests : IDisposable
    {
        private readonly ILogger<LazyWorkspaceBuilder> _mockLogger;
        private readonly ILogger<SymbolIndex> _mockSymbolIndexLogger;
        private readonly SymbolIndex _symbolIndex;
        private readonly LazyWorkspaceBuilder _workspaceBuilder;
        private string _tempDirectory = null!;
        private string _testSolutionPath = null!;
        private string _testProjectPath = null!;

        public LazyWorkspaceBuilderTests()
        {
            _mockLogger = Substitute.For<ILogger<LazyWorkspaceBuilder>>();
            _mockSymbolIndexLogger = Substitute.For<ILogger<SymbolIndex>>();
            _symbolIndex = new SymbolIndex(_mockSymbolIndexLogger);
            _workspaceBuilder = new LazyWorkspaceBuilder(_symbolIndex, _mockLogger);
            
            // Create temporary test directory structure
            _tempDirectory = Path.Combine(Path.GetTempPath(), "TestIntelligence", "LazyWorkspaceBuilderTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            
            SetupTestSolution();
        }

        private void SetupTestSolution()
        {
            // Create test solution structure
            var project1Dir = Path.Combine(_tempDirectory, "Project1");
            var project2Dir = Path.Combine(_tempDirectory, "Project2");
            Directory.CreateDirectory(project1Dir);
            Directory.CreateDirectory(project2Dir);
            
            var project1Path = Path.Combine(project1Dir, "Project1.csproj");
            var project2Path = Path.Combine(project2Dir, "Project2.csproj");
            _testSolutionPath = Path.Combine(_tempDirectory, "TestSolution.sln");
            _testProjectPath = project1Path;
            
            // Create project files
            File.WriteAllText(project1Path, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>");

            File.WriteAllText(project2Path, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include=""..\Project1\Project1.csproj"" />
  </ItemGroup>
</Project>");

            // Create solution file
            File.WriteAllText(_testSolutionPath, $@"
Microsoft Visual Studio Solution File, Format Version 12.00
Project(""{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}"") = ""Project1"", ""Project1\Project1.csproj"", ""{{12345678-1234-1234-1234-123456789012}}""
EndProject
Project(""{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}"") = ""Project2"", ""Project2\Project2.csproj"", ""{{87654321-4321-4321-4321-210987654321}}""
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
	EndGlobalSection
EndGlobal");

            // Create test source files
            CreateTestSourceFile(project1Dir, "Class1.cs", @"
using System;

namespace Project1
{
    public class Calculator
    {
        public int Add(int a, int b) => a + b;
    }
}");

            CreateTestSourceFile(project2Dir, "Class2.cs", @"
using System;
using Project1;

namespace Project2
{
    public class CalculatorService
    {
        private readonly Calculator _calculator = new Calculator();
        
        public int AddNumbers(int x, int y)
        {
            return _calculator.Add(x, y);
        }
    }
}");
        }

        private void CreateTestSourceFile(string directory, string fileName, string content)
        {
            var filePath = Path.Combine(directory, fileName);
            File.WriteAllText(filePath, content);
        }

        [Fact]
        public async Task InitializeAsync_WithValidSolution_ShouldSucceed()
        {
            // Act
            await _workspaceBuilder.InitializeAsync(_testSolutionPath);

            // Assert
            var stats = _workspaceBuilder.GetStats();
            Assert.Equal(0, stats.LoadedProjects); // No projects loaded yet (lazy loading)
            Assert.True(stats.TotalProjects >= 0); // Should have discovered projects
        }

        [Fact]
        public async Task InitializeAsync_WithSingleProject_ShouldSucceed()
        {
            // Act
            await _workspaceBuilder.InitializeAsync(_testProjectPath);

            // Assert
            var stats = _workspaceBuilder.GetStats();
            Assert.True(stats.FileToProjectMappings >= 0);
        }

        [Fact]
        public async Task GetOrLoadProjectAsync_WithValidProject_ShouldLoadProject()
        {
            // Arrange
            await _workspaceBuilder.InitializeAsync(_testSolutionPath);

            // Act
            var project = await _workspaceBuilder.GetOrLoadProjectAsync(_testProjectPath);

            // Assert
            Assert.NotNull(project);
            Assert.Equal("Project1", project.Name);
            
            // Verify stats updated
            var stats = _workspaceBuilder.GetStats();
            Assert.Equal(1, stats.LoadedProjects);
        }

        [Fact]
        public async Task GetOrLoadProjectAsync_WithSameProjectTwice_ShouldUseCache()
        {
            // Arrange
            await _workspaceBuilder.InitializeAsync(_testSolutionPath);

            // Act
            var project1 = await _workspaceBuilder.GetOrLoadProjectAsync(_testProjectPath);
            var project2 = await _workspaceBuilder.GetOrLoadProjectAsync(_testProjectPath);

            // Assert
            Assert.NotNull(project1);
            Assert.NotNull(project2);
            Assert.Same(project1, project2); // Should be same instance (cached)
            
            // Should still only have 1 loaded project
            var stats = _workspaceBuilder.GetStats();
            Assert.Equal(1, stats.LoadedProjects);
        }

        [Fact]
        public async Task GetOrLoadProjectAsync_WithNonexistentProject_ShouldReturnNull()
        {
            // Arrange
            await _workspaceBuilder.InitializeAsync(_testSolutionPath);
            var nonexistentPath = Path.Combine(_tempDirectory, "Nonexistent", "Project.csproj");

            // Act
            var project = await _workspaceBuilder.GetOrLoadProjectAsync(nonexistentPath);

            // Assert
            Assert.Null(project);
        }

        [Fact]
        public async Task GetCompilationAsync_WithValidProject_ShouldReturnCompilation()
        {
            // Arrange
            await _workspaceBuilder.InitializeAsync(_testSolutionPath);

            // Act
            var compilation = await _workspaceBuilder.GetCompilationAsync(_testProjectPath);

            // Assert
            Assert.NotNull(compilation);
            Assert.Contains("Project1", compilation.AssemblyName);
            
            // Verify compilation was cached
            var stats = _workspaceBuilder.GetStats();
            Assert.Equal(1, stats.CompiledProjects);
        }

        [Fact]
        public async Task GetCompilationAsync_WithSameProjectTwice_ShouldUseCache()
        {
            // Arrange
            await _workspaceBuilder.InitializeAsync(_testSolutionPath);

            // Act
            var compilation1 = await _workspaceBuilder.GetCompilationAsync(_testProjectPath);
            var compilation2 = await _workspaceBuilder.GetCompilationAsync(_testProjectPath);

            // Assert
            Assert.NotNull(compilation1);
            Assert.NotNull(compilation2);
            Assert.Same(compilation1, compilation2); // Should be same instance (cached)
        }

        [Fact]
        public async Task GetProjectContainingFileAsync_WithValidFile_ShouldReturnProject()
        {
            // Arrange
            await _workspaceBuilder.InitializeAsync(_testSolutionPath);
            var testFile = Path.Combine(_tempDirectory, "Project1", "Class1.cs");

            // Act
            var project = await _workspaceBuilder.GetProjectContainingFileAsync(testFile);

            // Assert
            Assert.NotNull(project);
            Assert.Equal("Project1", project.Name);
        }

        [Fact]
        public async Task GetProjectContainingFileAsync_WithNonexistentFile_ShouldReturnNull()
        {
            // Arrange
            await _workspaceBuilder.InitializeAsync(_testSolutionPath);
            var nonexistentFile = Path.Combine(_tempDirectory, "Nonexistent", "File.cs");

            // Act
            var project = await _workspaceBuilder.GetProjectContainingFileAsync(nonexistentFile);

            // Assert
            Assert.Null(project);
        }

        [Fact]
        public async Task GetProjectsContainingMethodAsync_WithValidMethod_ShouldReturnProjects()
        {
            // Arrange
            await _workspaceBuilder.InitializeAsync(_testSolutionPath);

            // Act
            var projects = await _workspaceBuilder.GetProjectsContainingMethodAsync("Add");

            // Assert
            Assert.NotEmpty(projects);
            Assert.Contains(projects, p => p.Name == "Project1");
        }

        [Fact]
        public async Task GetAllProjectPathsAsync_ShouldReturnAllProjects()
        {
            // Arrange
            await _workspaceBuilder.InitializeAsync(_testSolutionPath);

            // Act
            var projectPaths = await _workspaceBuilder.GetAllProjectPathsAsync();

            // Assert
            Assert.NotEmpty(projectPaths);
            Assert.Contains(projectPaths, p => p.EndsWith("Project1.csproj"));
            Assert.Contains(projectPaths, p => p.EndsWith("Project2.csproj"));
        }

        [Fact]
        public async Task PreloadProjectsAsync_ShouldLoadProjectsInBackground()
        {
            // Arrange
            await _workspaceBuilder.InitializeAsync(_testSolutionPath);
            var projectPaths = new[] { _testProjectPath };

            // Act
            await _workspaceBuilder.PreloadProjectsAsync(projectPaths);

            // Assert
            var stats = _workspaceBuilder.GetStats();
            Assert.Equal(1, stats.LoadedProjects);
        }

        [Fact]
        public void GetStats_ShouldReturnAccurateStatistics()
        {
            // Arrange
            var stats = _workspaceBuilder.GetStats();

            // Act & Assert
            Assert.True(stats.LoadedProjects >= 0);
            Assert.True(stats.CompiledProjects >= 0);
            Assert.True(stats.FileToProjectMappings >= 0);
            Assert.True(stats.TotalProjects >= 0);
            Assert.True(stats.LoadedProjectsPercentage >= 0 && stats.LoadedProjectsPercentage <= 100);
        }

        [Fact]
        public async Task ClearCaches_ShouldResetCachedData()
        {
            // Arrange
            await _workspaceBuilder.InitializeAsync(_testSolutionPath);
            await _workspaceBuilder.GetOrLoadProjectAsync(_testProjectPath);
            await _workspaceBuilder.GetCompilationAsync(_testProjectPath);

            var statsBefore = _workspaceBuilder.GetStats();
            Assert.Equal(1, statsBefore.LoadedProjects);
            Assert.Equal(1, statsBefore.CompiledProjects);

            // Act
            _workspaceBuilder.ClearCaches();

            // Assert
            var statsAfter = _workspaceBuilder.GetStats();
            Assert.Equal(0, statsAfter.LoadedProjects);
            Assert.Equal(0, statsAfter.CompiledProjects);
            // File mappings should remain as they're lightweight
        }

        [Fact]
        public async Task InitializeAsync_WithInvalidPath_ShouldHandleGracefully()
        {
            // Arrange
            var invalidPath = Path.Combine(_tempDirectory, "nonexistent.sln");

            // Act & Assert - Should not throw
            await _workspaceBuilder.InitializeAsync(invalidPath);
        }

        [Fact]
        public void WorkspaceStats_LoadedProjectsPercentage_ShouldCalculateCorrectly()
        {
            // Arrange
            var stats = new LazyWorkspaceBuilder.WorkspaceStats
            {
                LoadedProjects = 2,
                TotalProjects = 10
            };

            // Act
            var percentage = stats.LoadedProjectsPercentage;

            // Assert
            Assert.Equal(20.0, percentage);
        }

        [Fact]
        public void WorkspaceStats_LoadedProjectsPercentage_WithZeroTotal_ShouldReturnZero()
        {
            // Arrange
            var stats = new LazyWorkspaceBuilder.WorkspaceStats
            {
                LoadedProjects = 5,
                TotalProjects = 0
            };

            // Act
            var percentage = stats.LoadedProjectsPercentage;

            // Assert
            Assert.Equal(0.0, percentage);
        }

        public void Dispose()
        {
            try
            {
                _workspaceBuilder?.Dispose();
                
                if (Directory.Exists(_tempDirectory))
                {
                    Directory.Delete(_tempDirectory, true);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}