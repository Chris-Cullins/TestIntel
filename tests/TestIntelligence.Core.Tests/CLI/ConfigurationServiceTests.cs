using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using TestIntelligence.CLI.Models;
using TestIntelligence.CLI.Services;

namespace TestIntelligence.Core.Tests.CLI
{
    public class ConfigurationServiceTests : IDisposable
    {
        private readonly ConfigurationService _service;
        private readonly ILogger<ConfigurationService> _mockLogger;
        private readonly string _testDirectory;

        public ConfigurationServiceTests()
        {
            _mockLogger = Substitute.For<ILogger<ConfigurationService>>();
            _service = new ConfigurationService(_mockLogger);
            _testDirectory = Path.Combine(Path.GetTempPath(), "TestIntelConfigTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }

        [Fact]
        public async Task LoadConfigurationAsync_WithNonExistentDirectory_ReturnsDefaultConfiguration()
        {
            // Arrange
            var nonExistentPath = "/path/that/does/not/exist";

            // Act
            var config = await _service.LoadConfigurationAsync(nonExistentPath);

            // Assert
            Assert.NotNull(config);
            Assert.True(config.Projects.TestProjectsOnly);
            Assert.False(config.Analysis.Verbose);
            Assert.Equal("text", config.Output.Format);
        }

        [Fact]
        public async Task LoadConfigurationAsync_WithSolutionFile_LoadsFromSolutionDirectory()
        {
            // Arrange
            var solutionPath = Path.Combine(_testDirectory, "test.sln");
            var configPath = Path.Combine(_testDirectory, "testintel.config");
            
            File.WriteAllText(solutionPath, "Microsoft Visual Studio Solution File");
            await _service.CreateDefaultConfigurationAsync(configPath);

            // Act
            var config = await _service.LoadConfigurationAsync(solutionPath);

            // Assert
            Assert.NotNull(config);
            Assert.True(config.Projects.TestProjectsOnly);
        }

        [Fact]
        public async Task CreateDefaultConfigurationAsync_CreatesValidJsonFile()
        {
            // Arrange
            var configPath = Path.Combine(_testDirectory, "testintel.config");

            // Act
            var result = await _service.CreateDefaultConfigurationAsync(configPath);

            // Assert
            Assert.True(result);
            Assert.True(File.Exists(configPath));
            
            var content = await File.ReadAllTextAsync(configPath);
            Assert.NotEmpty(content);
            Assert.Contains("TestIntelligence Configuration", content);
        }

        [Fact]
        public async Task LoadConfigurationFromFileAsync_WithValidJson_LoadsCorrectly()
        {
            // Arrange
            var configPath = Path.Combine(_testDirectory, "custom.config");
            var configContent = @"{
                ""projects"": {
                    ""include"": [""*Core*""],
                    ""exclude"": [""*Test*""],
                    ""testProjectsOnly"": false
                },
                ""analysis"": {
                    ""verbose"": true,
                    ""maxParallelism"": 4
                },
                ""output"": {
                    ""format"": ""json""
                }
            }";
            
            await File.WriteAllTextAsync(configPath, configContent);

            // Act
            var config = await _service.LoadConfigurationFromFileAsync(configPath);

            // Assert
            Assert.Single(config.Projects.Include);
            Assert.Equal("*Core*", config.Projects.Include[0]);
            Assert.Single(config.Projects.Exclude);
            Assert.Equal("*Test*", config.Projects.Exclude[0]);
            Assert.False(config.Projects.TestProjectsOnly);
            Assert.True(config.Analysis.Verbose);
            Assert.Equal(4, config.Analysis.MaxParallelism);
            Assert.Equal("json", config.Output.Format);
        }

        [Fact]
        public void FilterProjects_WithEmptyList_ReturnsEmpty()
        {
            // Arrange
            var projects = new List<string>();
            var config = new TestIntelConfiguration();

            // Act
            var filtered = _service.FilterProjects(projects, config);

            // Assert
            Assert.Empty(filtered);
        }

        [Fact]
        public void FilterProjects_WithExcludePatterns_FiltersCorrectly()
        {
            // Arrange
            var projects = new List<string>
            {
                "/path/to/Core.Tests/Core.Tests.csproj",
                "/path/to/Integration.Tests/Integration.Tests.csproj",
                "/path/to/Unit.Tests/Unit.Tests.csproj",
                "/path/to/ORM.Project/ORM.Project.csproj"
            };
            
            var config = new TestIntelConfiguration
            {
                Projects = new ProjectFilterConfiguration
                {
                    Exclude = new List<string> { "*Integration*", "*ORM*" }
                }
            };

            // Act
            var filtered = _service.FilterProjects(projects, config);

            // Assert
            Assert.Equal(2, filtered.Count);
            Assert.Contains("/path/to/Core.Tests/Core.Tests.csproj", filtered);
            Assert.Contains("/path/to/Unit.Tests/Unit.Tests.csproj", filtered);
            Assert.DoesNotContain("/path/to/Integration.Tests/Integration.Tests.csproj", filtered);
            Assert.DoesNotContain("/path/to/ORM.Project/ORM.Project.csproj", filtered);
        }

        [Fact]
        public void FilterProjects_WithIncludePatterns_FiltersCorrectly()
        {
            // Arrange
            var projects = new List<string>
            {
                "/path/to/Core.Tests/Core.Tests.csproj",
                "/path/to/Integration.Tests/Integration.Tests.csproj",
                "/path/to/Unit.Tests/Unit.Tests.csproj"
            };
            
            var config = new TestIntelConfiguration
            {
                Projects = new ProjectFilterConfiguration
                {
                    Include = new List<string> { "*Core*" }
                }
            };

            // Act
            var filtered = _service.FilterProjects(projects, config);

            // Assert
            Assert.Single(filtered);
            Assert.Contains("/path/to/Core.Tests/Core.Tests.csproj", filtered);
        }

        [Fact]
        public void FilterProjects_WithExcludeTypes_FiltersCorrectly()
        {
            // Arrange
            var projects = new List<string>
            {
                "/path/to/Core.Tests.csproj",
                "/path/to/Database.Tests.csproj",
                "/path/to/ORM.Tests.csproj",
                "/path/to/Unit.Tests.csproj"
            };
            
            var config = new TestIntelConfiguration
            {
                Projects = new ProjectFilterConfiguration
                {
                    ExcludeTypes = new List<string> { "database", "orm" }
                }
            };

            // Act
            var filtered = _service.FilterProjects(projects, config);

            // Assert
            Assert.Equal(2, filtered.Count);
            Assert.Contains("/path/to/Core.Tests.csproj", filtered);
            Assert.Contains("/path/to/Unit.Tests.csproj", filtered);
            Assert.DoesNotContain("/path/to/Database.Tests.csproj", filtered);
            Assert.DoesNotContain("/path/to/ORM.Tests.csproj", filtered);
        }

        [Fact]
        public void FilterProjects_WithBothIncludeAndExclude_ExcludeTakesPrecedence()
        {
            // Arrange
            var projects = new List<string>
            {
                "/path/to/Core.Tests.csproj",
                "/path/to/Core.Database.csproj"
            };
            
            var config = new TestIntelConfiguration
            {
                Projects = new ProjectFilterConfiguration
                {
                    Include = new List<string> { "*Core*" },
                    Exclude = new List<string> { "*Database*" }
                }
            };

            // Act
            var filtered = _service.FilterProjects(projects, config);

            // Assert
            Assert.Single(filtered);
            Assert.Contains("/path/to/Core.Tests.csproj", filtered);
            Assert.DoesNotContain("/path/to/Core.Database.csproj", filtered);
        }
    }
}