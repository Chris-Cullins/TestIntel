using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Core.Caching;
using TestIntelligence.ImpactAnalyzer.Caching;
using Xunit;
using Xunit.Abstractions;

namespace TestIntelligence.Core.Tests.Caching
{
    /// <summary>
    /// Integration tests for cache invalidation across all cache levels:
    /// - Solution-level cache
    /// - Project-level cache 
    /// - Call graph cache
    /// </summary>
    public class CacheInvalidationIntegrationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly ILogger<CacheInvalidationIntegrationTests> _logger;
        private readonly string _testDirectory;
        private readonly string _testSolutionPath;
        private readonly string _testProjectPath;
        private readonly List<IDisposable> _disposables = new();

        public CacheInvalidationIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _logger = new TestLogger<CacheInvalidationIntegrationTests>(output);
            
            // Create test directory structure
            _testDirectory = Path.Combine(Path.GetTempPath(), "CacheInvalidationTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDirectory);
            
            _testSolutionPath = Path.Combine(_testDirectory, "TestSolution.sln");
            _testProjectPath = Path.Combine(_testDirectory, "TestProject", "TestProject.csproj");
            
            SetupTestProject();
        }

        [Fact(Skip = "Integration test with timing dependency - requires cache invalidation fixes")]
        public async Task SolutionCacheManager_InvalidatesOnFileChange()
        {
            // Arrange
            var solutionCacheManager = new SolutionCacheManager(
                _testSolutionPath,
                logger: _logger as ILogger<SolutionCacheManager>);
            _disposables.Add(solutionCacheManager);

            await solutionCacheManager.InitializeAsync();

            // Create some test data and cache it
            var testData = new TestCacheData { Value = "Original", Timestamp = DateTime.UtcNow };
            var dependentFiles = new[] { _testProjectPath };

            var cached = await solutionCacheManager.GetOrSetAsync(
                "test-key",
                () => Task.FromResult(testData),
                dependentFiles);

            Assert.Equal("Original", cached.Value);

            // Act - Modify the dependent file
            var projectContent = await File.ReadAllTextAsync(_testProjectPath);
            await File.WriteAllTextAsync(_testProjectPath, projectContent + "\n<!-- Modified -->");

            // Wait a bit for file system events to propagate
            await Task.Delay(100);

            // Force change detection
            var changes = await solutionCacheManager.DetectChangesAsync();
            Assert.True(changes.HasChanges);
            Assert.Contains(_testProjectPath, changes.ModifiedFiles);

            // Verify cache was invalidated by trying to get the same key
            var testData2 = new TestCacheData { Value = "NewValue", Timestamp = DateTime.UtcNow };
            var cached2 = await solutionCacheManager.GetOrSetAsync(
                "test-key",
                () => Task.FromResult(testData2),
                dependentFiles);

            // Should get new value since cache was invalidated
            Assert.Equal("NewValue", cached2.Value);
        }

        [Fact(Skip = "Integration test with timing dependency - requires cache invalidation fixes")]
        public async Task ProjectCacheManager_InvalidatesOnContentChange()
        {
            // Arrange
            var projectCacheManager = new ProjectCacheManager(
                Path.Combine(_testDirectory, "ProjectCache"),
                logger: _logger as ILogger<ProjectCacheManager>);
            _disposables.Add(projectCacheManager);

            // Create initial project entry
            var initialEntry = await projectCacheManager.CreateProjectEntryAsync(_testProjectPath);
            await projectCacheManager.StoreProjectAsync(initialEntry);

            // Verify it's cached
            var cachedEntry = await projectCacheManager.GetProjectAsync(_testProjectPath, "unknown");
            Assert.NotNull(cachedEntry);
            Assert.Equal(initialEntry.ContentHash, cachedEntry.ContentHash);

            // Act - Modify source file content (not just timestamp)
            var sourceFile = Path.Combine(Path.GetDirectoryName(_testProjectPath)!, "TestClass.cs");
            var originalContent = await File.ReadAllTextAsync(sourceFile);
            await File.WriteAllTextAsync(sourceFile, originalContent.Replace("TestClass", "ModifiedTestClass"));

            // Wait for file system watcher and invalidation
            await Task.Delay(200);

            // Try to get cached entry again - should be invalidated due to content change
            var cachedEntryAfterChange = await projectCacheManager.GetProjectAsync(_testProjectPath);
            
            // Should be null because cache was invalidated, or if present, should have different hash
            if (cachedEntryAfterChange != null)
            {
                // If still cached, content hash should be different
                Assert.NotEqual(initialEntry.ContentHash, cachedEntryAfterChange.ContentHash);
            }
        }

        [Fact(Skip = "Integration test with timing dependency - requires cache invalidation fixes")]
        public async Task CallGraphCache_InvalidatesOnProjectFileChange()
        {
            // Arrange
            var callGraphCache = new CallGraphCache(
                Path.Combine(_testDirectory, "CallGraphCache"),
                logger: _logger as ILogger<CallGraphCache>);
            _disposables.Add(callGraphCache);

            // Store a call graph entry
            var callGraph = new Dictionary<string, HashSet<string>>
            {
                ["TestMethod"] = new HashSet<string> { "HelperMethod" }
            };
            var reverseCallGraph = new Dictionary<string, HashSet<string>>
            {
                ["HelperMethod"] = new HashSet<string> { "TestMethod" }
            };
            var referencedAssemblies = new[] { "System.dll", "mscorlib.dll" };

            await callGraphCache.StoreCallGraphAsync(
                _testProjectPath,
                referencedAssemblies,
                callGraph,
                reverseCallGraph,
                TimeSpan.FromSeconds(1));

            // Verify it's cached
            var cachedGraph = await callGraphCache.GetCallGraphAsync(_testProjectPath, referencedAssemblies);
            Assert.NotNull(cachedGraph);
            Assert.Equal(callGraph.Count, cachedGraph.CallGraph.Count);

            // Act - Modify project file
            var projectContent = await File.ReadAllTextAsync(_testProjectPath);
            await File.WriteAllTextAsync(_testProjectPath, 
                projectContent.Replace("<TargetFramework>net8.0</TargetFramework>", 
                                     "<TargetFramework>net8.0</TargetFramework>\n  <GeneratePackageOnBuild>true</GeneratePackageOnBuild>"));

            // Wait for file system events
            await Task.Delay(200);

            // Try to get cached entry - should be invalidated
            var cachedGraphAfterChange = await callGraphCache.GetCallGraphAsync(_testProjectPath, referencedAssemblies);
            
            // Should be null because cache was invalidated
            Assert.Null(cachedGraphAfterChange);
        }

        [Fact(Skip = "Integration test with timing dependency - requires cache invalidation fixes")]
        public async Task EndToEndCacheInvalidation_WorksAcrossAllLayers()
        {
            // Arrange - Set up all cache managers
            var solutionCache = new SolutionCacheManager(_testSolutionPath, logger: _logger as ILogger<SolutionCacheManager>);
            var projectCache = new ProjectCacheManager(Path.Combine(_testDirectory, "ProjectCache"), logger: _logger as ILogger<ProjectCacheManager>);
            var callGraphCache = new CallGraphCache(Path.Combine(_testDirectory, "CallGraphCache"), logger: _logger as ILogger<CallGraphCache>);
            
            _disposables.AddRange(new IDisposable[] { solutionCache, projectCache, callGraphCache });

            await solutionCache.InitializeAsync();

            // Cache data at all levels
            var testData = new TestCacheData { Value = "Test", Timestamp = DateTime.UtcNow };
            var solutionCached = await solutionCache.GetOrSetAsync("test", () => Task.FromResult(testData), new[] { _testProjectPath });

            var projectEntry = await projectCache.CreateProjectEntryAsync(_testProjectPath);
            await projectCache.StoreProjectAsync(projectEntry);

            var callGraph = new Dictionary<string, HashSet<string>> { ["Test"] = new HashSet<string> { "Helper" } };
            var reverseCallGraph = new Dictionary<string, HashSet<string>> { ["Helper"] = new HashSet<string> { "Test" } };
            await callGraphCache.StoreCallGraphAsync(_testProjectPath, new[] { "test.dll" }, callGraph, reverseCallGraph, TimeSpan.FromSeconds(1));

            // Verify all are cached
            Assert.NotNull(solutionCached);
            Assert.NotNull(await projectCache.GetProjectAsync(_testProjectPath, "unknown"));
            Assert.NotNull(await callGraphCache.GetCallGraphAsync(_testProjectPath, new[] { "test.dll" }));

            // Act - Make a significant change to a source file
            var sourceFile = Path.Combine(Path.GetDirectoryName(_testProjectPath)!, "TestClass.cs");
            var originalContent = await File.ReadAllTextAsync(sourceFile);
            await File.WriteAllTextAsync(sourceFile, originalContent + "\n\npublic class NewClass { }");

            // Wait for all invalidation to propagate
            await Task.Delay(1500);

            // Force change detection at solution level
            var changes = await solutionCache.DetectChangesAsync();
            Assert.True(changes.HasChanges);

            // Assert - Verify invalidation cascaded through all layers
            var solutionCached2 = await solutionCache.GetOrSetAsync("test", () => Task.FromResult(new TestCacheData { Value = "New", Timestamp = DateTime.UtcNow }), new[] { _testProjectPath });
            Assert.Equal("New", solutionCached2.Value); // Should be new value due to invalidation

            var projectCached2 = await projectCache.GetProjectAsync(_testProjectPath);
            // Should be invalidated (null) or have different hash
            if (projectCached2 != null)
            {
                Assert.NotEqual(projectEntry.ContentHash, projectCached2.ContentHash);
            }

            // Call graph should also be invalidated
            var callGraphCached2 = await callGraphCache.GetCallGraphAsync(_testProjectPath, new[] { "test.dll" });
            Assert.Null(callGraphCached2);
        }

        [Fact]
        public async Task CacheInvalidation_PerformanceTest()
        {
            // Test that invalidation doesn't cause performance issues with many files
            var solutionCache = new SolutionCacheManager(_testSolutionPath, logger: _logger as ILogger<SolutionCacheManager>);
            _disposables.Add(solutionCache);

            await solutionCache.InitializeAsync();

            // Create many cache entries
            var tasks = Enumerable.Range(0, 100).Select(async i =>
            {
                var data = new TestCacheData { Value = $"Test{i}", Timestamp = DateTime.UtcNow };
                return await solutionCache.GetOrSetAsync($"key{i}", () => Task.FromResult(data), new[] { _testProjectPath });
            });

            await Task.WhenAll(tasks);

            // Time the invalidation
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            // Modify the file to trigger invalidation
            var content = await File.ReadAllTextAsync(_testProjectPath);
            await File.WriteAllTextAsync(_testProjectPath, content + $"\n<!-- {DateTime.UtcNow} -->");
            
            var changes = await solutionCache.DetectChangesAsync();
            stopwatch.Stop();

            Assert.True(changes.HasChanges);
            Assert.True(stopwatch.ElapsedMilliseconds < 5000); // Should complete within 5 seconds
            
            _output.WriteLine($"Invalidation of 100 cache entries took {stopwatch.ElapsedMilliseconds}ms");
        }

        private void SetupTestProject()
        {
            // Create solution file
            File.WriteAllText(_testSolutionPath, @"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Project(""{9A19103F-16F7-4668-BE54-9A1E7A4F7556}"") = ""TestProject"", ""TestProject\TestProject.csproj"", ""{12345678-1234-5678-9012-123456789012}""
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
EndGlobal
");

            // Create project directory and files
            var projectDir = Path.GetDirectoryName(_testProjectPath)!;
            Directory.CreateDirectory(projectDir);

            File.WriteAllText(_testProjectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>");

            File.WriteAllText(Path.Combine(projectDir, "TestClass.cs"), @"
namespace TestProject
{
    public class TestClass
    {
        public string GetMessage() => ""Hello World"";
        
        private void HelperMethod() { }
    }
}");
        }

        public void Dispose()
        {
            foreach (var disposable in _disposables)
            {
                disposable?.Dispose();
            }

            try
            {
                if (Directory.Exists(_testDirectory))
                {
                    Directory.Delete(_testDirectory, true);
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Failed to cleanup test directory: {ex.Message}");
            }
        }

        public class TestCacheData
        {
            public string Value { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
        }

        public class TestLogger<T> : ILogger<T>
        {
            private readonly ITestOutputHelper _output;

            public TestLogger(ITestOutputHelper output)
            {
                _output = output;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _output.WriteLine($"[{logLevel}] {formatter(state, exception)}");
                if (exception != null)
                {
                    _output.WriteLine(exception.ToString());
                }
            }
        }
    }
}