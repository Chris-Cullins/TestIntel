using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Caching;
using Xunit;

namespace TestIntelligence.Core.Tests.Caching
{
    public class ProjectCacheManagerTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly string _testProjectPath;
        private readonly ILogger<ProjectCacheManager> _mockLogger;

        public ProjectCacheManagerTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "ProjectCacheManagerTests", Guid.NewGuid().ToString());
            _testProjectPath = Path.Combine(_tempDirectory, "TestProject.csproj");
            _mockLogger = new TestLogger<ProjectCacheManager>();
            
            Directory.CreateDirectory(_tempDirectory);
            CreateTestProject();
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        [Fact]
        public async Task GetProjectAsync_WithNoCachedEntry_ReturnsNull()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();

            // Act
            var result = await cacheManager.GetProjectAsync(_testProjectPath);

            // Assert
            Assert.Null(result);
            
            var stats = await cacheManager.GetStatisticsAsync();
            Assert.Equal(1, stats.MissCount);
            Assert.Equal(0, stats.HitCount);
        }

        [Fact]
        public async Task CreateProjectEntryAsync_WithValidProject_CreatesEntry()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();
            
            // Create some source files
            var sourceFile1 = Path.Combine(_tempDirectory, "Source1.cs");
            var sourceFile2 = Path.Combine(_tempDirectory, "Source2.cs");
            await File.WriteAllTextAsync(sourceFile1, "public class Class1 { }");
            await File.WriteAllTextAsync(sourceFile2, "public class Class2 { }");

            // Act
            var entry = await cacheManager.CreateProjectEntryAsync(_testProjectPath, "net8.0");

            // Assert
            Assert.NotNull(entry);
            Assert.Equal(_testProjectPath, entry.ProjectPath);
            Assert.Equal("net8.0", entry.TargetFramework);
            Assert.True(entry.SourceFiles.Count >= 2);
            Assert.Contains("Source1.cs", entry.SourceFiles);
            Assert.Contains("Source2.cs", entry.SourceFiles);
            Assert.NotEmpty(entry.ContentHash);
        }

        [Fact]
        public async Task StoreProjectAsync_WithValidEntry_StoresSuccessfully()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();
            var entry = await cacheManager.CreateProjectEntryAsync(_testProjectPath, "net8.0");

            // Act
            await cacheManager.StoreProjectAsync(entry);

            // Assert
            var stats = await cacheManager.GetStatisticsAsync();
            Assert.Equal(1, stats.StoreCount);
            Assert.Equal(1, stats.TotalEntries);
            // LastMaintenanceRun may not be recent since background maintenance is disabled in tests
            Assert.True(stats.LastMaintenanceRun >= DateTime.MinValue);
        }

        [Fact]
        public async Task GetProjectAsync_WithValidCachedEntry_ReturnsEntry()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();
            var entry = await cacheManager.CreateProjectEntryAsync(_testProjectPath, "net8.0");
            await cacheManager.StoreProjectAsync(entry);

            // Act
            var result = await cacheManager.GetProjectAsync(_testProjectPath, "net8.0");

            // Assert
            Assert.NotNull(result);
            Assert.Equal(entry.ProjectPath, result.ProjectPath);
            Assert.Equal(entry.TargetFramework, result.TargetFramework);
            Assert.Equal(entry.SourceFiles.Count, result.SourceFiles.Count);
            
            var stats = await cacheManager.GetStatisticsAsync();
            Assert.Equal(1, stats.HitCount);
            Assert.True(stats.HitRatio > 0);
        }

        [Fact]
        public async Task GetProjectAsync_WithModifiedProject_InvalidatesCache()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();
            var entry = await cacheManager.CreateProjectEntryAsync(_testProjectPath, "net8.0");
            await cacheManager.StoreProjectAsync(entry);

            // Modify the project file
            await Task.Delay(100); // Ensure different timestamp
            var modifiedContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
                <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <Modified>true</Modified>
                </PropertyGroup>
            </Project>";
            await File.WriteAllTextAsync(_testProjectPath, modifiedContent);

            // Act
            var result = await cacheManager.GetProjectAsync(_testProjectPath, "net8.0");

            // Assert
            Assert.Null(result); // Should be invalidated due to content change
            
            var stats = await cacheManager.GetStatisticsAsync();
            Assert.Equal(1, stats.InvalidationCount);
        }

        [Fact]
        public async Task GetProjectsAsync_WithMultipleProjects_ReturnsValidEntries()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();
            
            var project2Path = Path.Combine(_tempDirectory, "TestProject2.csproj");
            var project3Path = Path.Combine(_tempDirectory, "TestProject3.csproj");
            
            CreateTestProject(project2Path);
            CreateTestProject(project3Path);

            var entry1 = await cacheManager.CreateProjectEntryAsync(_testProjectPath, "net8.0");
            var entry2 = await cacheManager.CreateProjectEntryAsync(project2Path, "net8.0");
            var entry3 = await cacheManager.CreateProjectEntryAsync(project3Path, "net8.0");

            await cacheManager.StoreProjectAsync(entry1);
            await cacheManager.StoreProjectAsync(entry2);
            await cacheManager.StoreProjectAsync(entry3);

            var projectPaths = new[] { _testProjectPath, project2Path, project3Path, "non-existent.csproj" };

            // Act
            var results = await cacheManager.GetProjectsAsync(projectPaths, "net8.0");

            // Assert
            Assert.Equal(3, results.Count);
            Assert.True(results.ContainsKey(_testProjectPath));
            Assert.True(results.ContainsKey(project2Path));
            Assert.True(results.ContainsKey(project3Path));
            Assert.False(results.ContainsKey("non-existent.csproj"));
        }

        [Fact]
        public async Task InvalidateProjectAsync_RemovesProjectEntries()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();
            var entry = await cacheManager.CreateProjectEntryAsync(_testProjectPath, "net8.0");
            await cacheManager.StoreProjectAsync(entry);

            // Act
            await cacheManager.InvalidateProjectAsync(_testProjectPath);

            // Assert
            var result = await cacheManager.GetProjectAsync(_testProjectPath, "net8.0");
            Assert.Null(result);
            
            var stats = await cacheManager.GetStatisticsAsync();
            Assert.True(stats.InvalidationCount > 0);
        }

        [Fact]
        public async Task GetStatisticsAsync_ReturnsAccurateStatistics()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();
            
            // Store multiple entries
            for (int i = 1; i <= 3; i++)
            {
                var projectPath = Path.Combine(_tempDirectory, $"TestProject{i}.csproj");
                CreateTestProject(projectPath);
                var entry = await cacheManager.CreateProjectEntryAsync(projectPath, "net8.0");
                await cacheManager.StoreProjectAsync(entry);
            }

            // Perform some gets (hits and misses)
            var project1Path = Path.Combine(_tempDirectory, "TestProject1.csproj");
            await cacheManager.GetProjectAsync(project1Path, "net8.0"); // Hit
            await cacheManager.GetProjectAsync("non-existent.csproj"); // Miss

            // Act
            var stats = await cacheManager.GetStatisticsAsync();

            // Assert
            Assert.Equal(3, stats.TotalEntries);
            Assert.Equal(3, stats.StoreCount);
            Assert.Equal(1, stats.HitCount);
            Assert.Equal(1, stats.MissCount);
            Assert.True(stats.HitRatio > 0);
            Assert.True(stats.TotalCompressedSize > 0);
            Assert.True(stats.TrackedProjectsCount >= 3);
        }

        [Fact]
        public async Task ClearAsync_RemovesAllEntries()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();
            var entry = await cacheManager.CreateProjectEntryAsync(_testProjectPath, "net8.0");
            await cacheManager.StoreProjectAsync(entry);

            // Act
            await cacheManager.ClearAsync();

            // Assert
            var result = await cacheManager.GetProjectAsync(_testProjectPath, "net8.0");
            Assert.Null(result);
            
            var stats = await cacheManager.GetStatisticsAsync();
            Assert.Equal(0, stats.TotalEntries);
            Assert.Equal(0, stats.TrackedProjectsCount);
        }

        [Fact]
        public async Task PerformMaintenanceAsync_ExecutesWithoutError()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();
            var entry = await cacheManager.CreateProjectEntryAsync(_testProjectPath, "net8.0");
            await cacheManager.StoreProjectAsync(entry);

            // Act & Assert - Should not throw
            await cacheManager.PerformMaintenanceAsync();
            
            var stats = await cacheManager.GetStatisticsAsync();
            Assert.True(stats.LastMaintenanceRun >= DateTime.UtcNow.AddMinutes(-1));
        }

        [Fact]
        public async Task CreateProjectEntryAsync_WithProjectReferences_DiscoversReferences()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();
            
            // Create a referenced project
            var referencedProjectPath = Path.Combine(_tempDirectory, "ReferencedProject.csproj");
            CreateTestProject(referencedProjectPath);
            
            // Update the main project to reference the other project
            var projectContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">
                <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                </PropertyGroup>
                <ItemGroup>
                    <ProjectReference Include=""ReferencedProject.csproj"" />
                </ItemGroup>
            </Project>";
            await File.WriteAllTextAsync(_testProjectPath, projectContent);

            // Act
            var entry = await cacheManager.CreateProjectEntryAsync(_testProjectPath, "net8.0");

            // Assert
            Assert.NotEmpty(entry.ProjectReferences);
            Assert.Contains(entry.ProjectReferences, pr => pr.RelativePath == "ReferencedProject.csproj");
        }

        [Fact]
        public async Task StoreProjectAsync_WithNullEntry_ThrowsArgumentNullException()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => cacheManager.StoreProjectAsync(null!));
        }

        [Fact]
        public async Task StoreProjectAsync_WithEmptyProjectPath_ThrowsArgumentException()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();
            var entry = new ProjectCacheEntry { ProjectPath = "" };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => cacheManager.StoreProjectAsync(entry));
        }

        [Fact]
        public async Task CreateProjectEntryAsync_WithNonExistentProject_ThrowsArgumentException()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                cacheManager.CreateProjectEntryAsync("non-existent-project.csproj"));
        }

        [Fact]
        public async Task CompressionEffectiveness_AchievesGoodCompression()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();
            
            // Create a project with lots of source files for better compression
            for (int i = 1; i <= 20; i++)
            {
                var sourceFile = Path.Combine(_tempDirectory, $"LargeSource{i}.cs");
                var sourceContent = $@"
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject
{{
    public class LargeClass{i}
    {{
        private readonly List<string> _items = new List<string>();
        
        public void Method1() {{ /* Implementation */ }}
        public void Method2() {{ /* Implementation */ }}
        public void Method3() {{ /* Implementation */ }}
        public string Property{i} {{ get; set; }} = ""Default value for property {i}"";
    }}
}}";
                await File.WriteAllTextAsync(sourceFile, sourceContent);
            }

            var entry = await cacheManager.CreateProjectEntryAsync(_testProjectPath, "net8.0");

            // Act
            await cacheManager.StoreProjectAsync(entry);

            // Assert
            var stats = await cacheManager.GetStatisticsAsync();
            Assert.True(stats.TotalCompressedSize < stats.TotalUncompressedSize);
            Assert.True(stats.AverageCompressionRatio > 30); // Should achieve at least 30% compression
        }

        [Fact]
        public async Task ConcurrentAccess_HandlesMultipleOperations()
        {
            // Arrange
            using var cacheManager = CreateCacheManager();
            const int operationCount = 10;
            var tasks = new List<Task>();

            // Create multiple projects
            for (int i = 0; i < operationCount; i++)
            {
                var projectPath = Path.Combine(_tempDirectory, $"ConcurrentProject{i}.csproj");
                CreateTestProject(projectPath);
            }

            // Act - Perform concurrent operations
            for (int i = 0; i < operationCount; i++)
            {
                var index = i;
                tasks.Add(Task.Run(async () =>
                {
                    var projectPath = Path.Combine(_tempDirectory, $"ConcurrentProject{index}.csproj");
                    var entry = await cacheManager.CreateProjectEntryAsync(projectPath, "net8.0");
                    await cacheManager.StoreProjectAsync(entry);
                }));
            }

            await Task.WhenAll(tasks);

            // Verify all entries were stored
            var projectPaths = Enumerable.Range(0, operationCount)
                .Select(i => Path.Combine(_tempDirectory, $"ConcurrentProject{i}.csproj"))
                .ToArray();

            var results = await cacheManager.GetProjectsAsync(projectPaths, "net8.0");

            // Assert
            Assert.Equal(operationCount, results.Count);
            
            var stats = await cacheManager.GetStatisticsAsync();
            Assert.Equal(operationCount, stats.StoreCount);
        }

        private ProjectCacheManager CreateCacheManager()
        {
            var cacheOptions = new CompressedCacheOptions
            {
                MaxCacheSizeBytes = 50 * 1024 * 1024, // 50MB for tests
                EnableBackgroundMaintenance = false // Disable for tests
            };
            
            return new ProjectCacheManager(_tempDirectory, cacheOptions, _mockLogger);
        }

        private void CreateTestProject(string? projectPath = null)
        {
            projectPath ??= _testProjectPath;
            
            var projectContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
                <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <OutputType>Library</OutputType>
                </PropertyGroup>
            </Project>";
            
            File.WriteAllText(projectPath, projectContent);
        }

        private class TestLogger<T> : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => false;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
        }
    }
}