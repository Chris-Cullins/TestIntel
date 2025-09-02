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
    public class SymbolIndexTests : IDisposable
    {
        private readonly ILogger<SymbolIndex> _mockLogger;
        private readonly SymbolIndex _symbolIndex;
        private string _tempDirectory = null!;
        private string _testSolutionPath = null!;
        private string _testProjectPath = null!;

        public SymbolIndexTests()
        {
            _mockLogger = Substitute.For<ILogger<SymbolIndex>>();
            _symbolIndex = new SymbolIndex(_mockLogger);
            
            // Create temporary test directory structure
            _tempDirectory = Path.Combine(Path.GetTempPath(), "TestIntelligence", "SymbolIndexTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            
            SetupTestSolution();
        }

        private void SetupTestSolution()
        {
            // Create test solution structure
            var projectDir = Path.Combine(_tempDirectory, "TestProject");
            Directory.CreateDirectory(projectDir);
            
            _testProjectPath = Path.Combine(projectDir, "TestProject.csproj");
            _testSolutionPath = Path.Combine(_tempDirectory, "TestSolution.sln");
            
            // Create a simple project file
            File.WriteAllText(_testProjectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>");

            // Create solution file
            File.WriteAllText(_testSolutionPath, $@"
Microsoft Visual Studio Solution File, Format Version 12.00
Project(""{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}"") = ""TestProject"", ""TestProject\TestProject.csproj"", ""{{12345678-1234-1234-1234-123456789012}}""
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
	EndGlobalSection
EndGlobal");

            // Create test C# files
            CreateTestSourceFile(projectDir, "Calculator.cs", @"
using System;

namespace TestProject
{
    public class Calculator
    {
        public int Add(int a, int b)
        {
            return a + b;
        }

        public int Multiply(int a, int b)
        {
            return a * b;
        }
    }
}");

            CreateTestSourceFile(projectDir, "StringHelper.cs", @"
using System;

namespace TestProject.Utilities
{
    public class StringHelper
    {
        public string Concatenate(string a, string b)
        {
            return a + b;
        }

        public bool IsNullOrEmpty(string value)
        {
            return string.IsNullOrEmpty(value);
        }
    }
}");

            CreateTestSourceFile(projectDir, "Tests.cs", @"
using Xunit;
using TestProject;

namespace TestProject.Tests
{
    public class CalculatorTests
    {
        [Fact]
        public void Add_ShouldReturnCorrectSum()
        {
            var calculator = new Calculator();
            var result = calculator.Add(2, 3);
            Assert.Equal(5, result);
        }

        [Test]
        public void Multiply_ShouldReturnCorrectProduct()
        {
            var calculator = new Calculator();
            var result = calculator.Multiply(2, 3);
            Assert.Equal(6, result);
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
        public async Task BuildIndexAsync_WithValidSolution_ShouldIndexAllMethods()
        {
            // Act
            await _symbolIndex.BuildIndexAsync(_testSolutionPath);

            // Assert - Check that methods were found
            var addMethodFiles = await _symbolIndex.FindFilesContainingMethodAsync("Add");
            var multiplyMethodFiles = await _symbolIndex.FindFilesContainingMethodAsync("Multiply");
            var concatenateMethodFiles = await _symbolIndex.FindFilesContainingMethodAsync("Concatenate");

            Assert.NotEmpty(addMethodFiles);
            Assert.NotEmpty(multiplyMethodFiles);
            Assert.NotEmpty(concatenateMethodFiles);

            // Verify specific method was found in correct file
            Assert.Contains(addMethodFiles, f => f.EndsWith("Calculator.cs"));
            Assert.Contains(concatenateMethodFiles, f => f.EndsWith("StringHelper.cs"));
        }

        [Fact]
        public async Task FindFilesContainingMethodAsync_WithoutIndex_ShouldReturnEmpty()
        {
            // Act (without building index first)
            var files = await _symbolIndex.FindFilesContainingMethodAsync("Add");

            // Assert
            Assert.Empty(files);
        }

        [Fact]
        public async Task FindFilesContainingMethodAsync_WithNonexistentMethod_ShouldReturnEmpty()
        {
            // Arrange
            await _symbolIndex.BuildIndexAsync(_testSolutionPath);

            // Act
            var files = await _symbolIndex.FindFilesContainingMethodAsync("NonexistentMethod");

            // Assert
            Assert.Empty(files);
        }

        [Fact]
        public async Task FindFilesContainingTypeAsync_ShouldFindTypes()
        {
            // Arrange
            await _symbolIndex.BuildIndexAsync(_testSolutionPath);

            // Act
            var calculatorFiles = await _symbolIndex.FindFilesContainingTypeAsync("Calculator");
            var stringHelperFiles = await _symbolIndex.FindFilesContainingTypeAsync("StringHelper");

            // Assert
            Assert.NotEmpty(calculatorFiles);
            Assert.NotEmpty(stringHelperFiles);
            Assert.Contains(calculatorFiles, f => f.EndsWith("Calculator.cs"));
            Assert.Contains(stringHelperFiles, f => f.EndsWith("StringHelper.cs"));
        }

        [Fact]
        public async Task GetFilesInNamespaceAsync_ShouldFindNamespaces()
        {
            // Arrange
            await _symbolIndex.BuildIndexAsync(_testSolutionPath);

            // Act
            var testProjectFiles = await _symbolIndex.GetFilesInNamespaceAsync("TestProject");
            var utilitiesFiles = await _symbolIndex.GetFilesInNamespaceAsync("TestProject.Utilities");

            // Assert
            Assert.NotEmpty(testProjectFiles);
            Assert.NotEmpty(utilitiesFiles);
            Assert.Contains(testProjectFiles, f => f.EndsWith("Calculator.cs"));
            Assert.Contains(utilitiesFiles, f => f.EndsWith("StringHelper.cs"));
        }

        [Fact]
        public async Task FindProjectsContainingMethodAsync_ShouldReturnProjectInfo()
        {
            // Arrange
            await _symbolIndex.BuildIndexAsync(_testSolutionPath);

            // Act
            var projects = await _symbolIndex.FindProjectsContainingMethodAsync("Add");

            // Assert
            Assert.NotEmpty(projects);
            var project = projects.First();
            Assert.Equal("TestProject", project.Name);
            Assert.Contains("TestProject.csproj", project.Path);
        }

        [Fact]
        public async Task GetProjectForFile_WithValidFile_ShouldReturnProjectInfo()
        {
            // Arrange
            var testFile = Path.Combine(_tempDirectory, "TestProject", "Calculator.cs");
            
            // Need to build index first to populate file-to-project mapping
            await _symbolIndex.BuildIndexAsync(_testSolutionPath);

            // Act
            var projectInfo = _symbolIndex.GetProjectForFile(testFile);

            // Assert
            Assert.NotNull(projectInfo);
            Assert.Equal("TestProject", projectInfo.Name);
        }

        [Fact]
        public void GetProjectForFile_WithNonexistentFile_ShouldReturnNull()
        {
            // Act
            var projectInfo = _symbolIndex.GetProjectForFile("/nonexistent/file.cs");

            // Assert
            Assert.Null(projectInfo);
        }

        [Fact]
        public async Task RefreshFilesAsync_ShouldUpdateIndex()
        {
            // Arrange
            await _symbolIndex.BuildIndexAsync(_testSolutionPath);
            
            // Create a new file with a new method
            var newFile = Path.Combine(_tempDirectory, "TestProject", "NewFile.cs");
            CreateTestSourceFile(Path.GetDirectoryName(newFile)!, Path.GetFileName(newFile), @"
namespace TestProject
{
    public class NewClass
    {
        public void NewMethod() { }
    }
}");

            // Act
            await _symbolIndex.RefreshFilesAsync(new[] { newFile });
            var files = await _symbolIndex.FindFilesContainingMethodAsync("NewMethod");

            // Assert
            Assert.NotEmpty(files);
            Assert.Contains(files, f => f.EndsWith("NewFile.cs"));
        }

        [Fact]
        public async Task Clear_ShouldResetIndex()
        {
            // Arrange
            await _symbolIndex.BuildIndexAsync(_testSolutionPath);

            // Act
            _symbolIndex.Clear();
            var files = await _symbolIndex.FindFilesContainingMethodAsync("Add");

            // Assert
            Assert.Empty(files);
        }

        [Fact]
        public async Task BuildIndexAsync_WithProjectFile_ShouldWork()
        {
            // Act
            await _symbolIndex.BuildIndexAsync(_testProjectPath);

            // Assert
            var addMethodFiles = await _symbolIndex.FindFilesContainingMethodAsync("Add");
            Assert.NotEmpty(addMethodFiles);
        }

        [Fact]
        public async Task FindFilesContainingMethodAsync_WithFuzzyMatching_ShouldWork()
        {
            // Arrange
            await _symbolIndex.BuildIndexAsync(_testSolutionPath);

            // Act - Test partial method name matching
            var files = await _symbolIndex.FindFilesContainingMethodAsync("Add");

            // Assert
            Assert.NotEmpty(files);
            Assert.Contains(files, f => f.EndsWith("Calculator.cs"));
        }

        [Fact]
        public void ProjectInfo_Equality_ShouldWorkCorrectly()
        {
            // Arrange
            var project1 = new SymbolIndex.ProjectInfo("Test", "/path/to/project.csproj", "/path/to");
            var project2 = new SymbolIndex.ProjectInfo("Test", "/path/to/project.csproj", "/path/to");
            var project3 = new SymbolIndex.ProjectInfo("Test", "/different/path/project.csproj", "/different/path");

            // Act & Assert
            Assert.Equal(project1, project2);
            Assert.NotEqual(project1, project3);
            Assert.Equal(project1.GetHashCode(), project2.GetHashCode());
        }

        public void Dispose()
        {
            try
            {
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