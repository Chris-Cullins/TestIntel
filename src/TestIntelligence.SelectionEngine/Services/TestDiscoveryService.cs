using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Categorizer;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Discovery;
using TestIntelligence.Core.Models;
using TestIntelligence.Core.Services;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.SelectionEngine.Interfaces;
using TestIntelligence.ImpactAnalyzer.Models;

namespace TestIntelligence.SelectionEngine.Services
{
    /// <summary>
    /// Service responsible for discovering tests from various sources.
    /// </summary>
    public class TestDiscoveryService : ITestDiscoveryService
    {
        private readonly ILogger<TestDiscoveryService> _logger;
        private readonly IAssemblyPathResolver? _assemblyPathResolver;
        private readonly ITestCategorizer? _testCategorizer;

        public TestDiscoveryService(
            ILogger<TestDiscoveryService> logger,
            IAssemblyPathResolver? assemblyPathResolver = null,
            ITestCategorizer? testCategorizer = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _assemblyPathResolver = assemblyPathResolver;
            _testCategorizer = testCategorizer;
        }

        public async Task<List<TestInfo>> GetCandidateTestsAsync(
            CodeChangeSet? changes, 
            TestSelectionOptions options, 
            string? solutionPath = null,
            CancellationToken cancellationToken = default)
        {
            var candidates = new List<TestInfo>();

            try
            {
                // If we have a solution path, discover tests from it
                if (!string.IsNullOrEmpty(solutionPath))
                {
                    candidates = await DiscoverTestsFromSolutionAsync(solutionPath, cancellationToken);
                }
                // If we don't have a solution path but have changes, try to infer from the first change path
                else if (changes?.Changes.Count > 0)
                {
                    var firstChangePath = changes.Changes.First().FilePath;
                    var inferredSolutionPath = FindSolutionFile(firstChangePath);
                    if (!string.IsNullOrEmpty(inferredSolutionPath))
                    {
                        _logger.LogInformation("Inferred solution path from changes: {SolutionPath}", inferredSolutionPath);
                        candidates = await DiscoverTestsFromSolutionAsync(inferredSolutionPath, cancellationToken);
                    }
                }

                _logger.LogInformation("Discovered {CandidateCount} candidate tests", candidates.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error discovering candidate tests");
            }

            return candidates;
        }

        public async Task<List<TestInfo>> DiscoverTestsFromSolutionAsync(string solutionPath, CancellationToken cancellationToken = default)
        {
            var testInfos = new List<TestInfo>();

            try
            {
                // Find test assemblies in the solution
                var assemblyPaths = _assemblyPathResolver != null 
                    ? await _assemblyPathResolver.FindTestAssembliesInSolutionAsync(solutionPath)
                    : await FindTestAssembliesInSolution(solutionPath);
                
                _logger.LogInformation("Found {AssemblyCount} test assemblies in solution", assemblyPaths.Count);
                foreach (var path in assemblyPaths)
                {
                    _logger.LogInformation("  Test assembly: {Assembly}", path);
                }

                // Use shared loader for efficiency
                using var loader = new CrossFrameworkAssemblyLoader();
                var discovery = TestDiscoveryFactory.CreateNUnitTestDiscovery();

                foreach (var assemblyPath in assemblyPaths)
                {
                    try
                    {
                        var loadResult = await loader.LoadAssemblyAsync(assemblyPath);
                        if (!loadResult.IsSuccess || loadResult.Assembly == null)
                        {
                            _logger.LogWarning("Failed to load assembly: {Assembly} - {Errors}", 
                                assemblyPath, string.Join(", ", loadResult.Errors));
                            continue;
                        }

                        var discoveryResult = await discovery.DiscoverTestsAsync(loadResult.Assembly, cancellationToken);
                        
                        // Convert discovered tests to TestInfo objects
                        foreach (var testMethod in discoveryResult.GetAllTestMethods())
                        {
                            var testInfo = ConvertToTestInfo(testMethod);
                            testInfos.Add(testInfo);
                        }

                        _logger.LogDebug("Discovered {TestCount} tests from {Assembly}", 
                            discoveryResult.TestMethodCount, Path.GetFileName(assemblyPath));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error discovering tests from assembly: {Assembly}", assemblyPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error discovering tests from solution: {Solution}", solutionPath);
            }

            return testInfos;
        }

        public string? FindSolutionFile(string filePath)
        {
            try
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
                
                while (directory != null)
                {
                    var solutionFiles = Directory.GetFiles(directory, "*.sln");
                    if (solutionFiles.Length > 0)
                    {
                        return solutionFiles.First(); // Return the first solution file found
                    }
                    
                    directory = Path.GetDirectoryName(directory);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error finding solution file for path: {FilePath}", filePath);
            }

            return null;
        }

        private async Task<List<string>> FindTestAssembliesInSolution(string solutionPath)
        {
            var assemblies = new List<string>();

            try
            {
                var solutionDir = Path.GetDirectoryName(solutionPath)!;
                var projectPaths = await FindTestProjectsInSolution(solutionPath);
                
                _logger.LogInformation("Found {ProjectCount} test projects", projectPaths.Count);

                foreach (var projectPath in projectPaths)
                {
                    var assemblyPath = _assemblyPathResolver?.ResolveAssemblyPath(projectPath) 
                        ?? GetAssemblyPathFromProject(projectPath);
                    _logger.LogDebug("Checking assembly path: {Assembly} (exists: {Exists})", assemblyPath, File.Exists(assemblyPath));
                    if (File.Exists(assemblyPath))
                    {
                        assemblies.Add(assemblyPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error finding test assemblies in solution: {Solution}", solutionPath);
            }

            return assemblies;
        }

        private async Task<List<string>> FindTestProjectsInSolution(string solutionPath)
        {
            var testProjects = new List<string>();

            try
            {
                var solutionContent = await File.ReadAllTextAsync(solutionPath);
                var solutionDir = Path.GetDirectoryName(solutionPath)!;

                // Parse solution file properly
                // Format: Project("{GUID}") = "ProjectName", "RelativePath", "{ProjectGUID}"
                var lines = solutionContent.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("Project(") && line.Contains(".csproj"))
                    {
                        // Extract project path from solution line
                        var parts = line.Split(',');
                        if (parts.Length >= 2)
                        {
                            var relativePath = parts[1].Trim().Trim('"');
                            
                            // Only include test projects (in tests directory or with "Test" in path/name)
                            if (relativePath.Contains("test", StringComparison.OrdinalIgnoreCase) || 
                                relativePath.StartsWith("tests", StringComparison.OrdinalIgnoreCase))
                            {
                                var fullPath = Path.Combine(solutionDir, relativePath).Replace('\\', Path.DirectorySeparatorChar);
                                if (File.Exists(fullPath))
                                {
                                    testProjects.Add(fullPath);
                                    _logger.LogDebug("Found test project: {Project}", fullPath);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing solution file: {Solution}", solutionPath);
            }

            return testProjects;
        }

        private string GetAssemblyPathFromProject(string projectPath)
        {
            var projectDir = Path.GetDirectoryName(projectPath)!;
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            
            // Try common output paths
            var possiblePaths = new[]
            {
                Path.Combine(projectDir, "bin", "Debug", "net8.0", $"{projectName}.dll"),
                Path.Combine(projectDir, "bin", "Release", "net8.0", $"{projectName}.dll"),
                Path.Combine(projectDir, "bin", "Debug", $"{projectName}.dll"),
                Path.Combine(projectDir, "bin", "Release", $"{projectName}.dll")
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            // Default to Debug net8.0 path even if it doesn't exist yet
            return Path.Combine(projectDir, "bin", "Debug", "net8.0", $"{projectName}.dll");
        }

        private TestInfo ConvertToTestInfo(Core.Models.TestMethod testMethod)
        {
            var category = CategorizeTest(testMethod);
            var averageTime = TimeSpan.FromMilliseconds(100); // Default estimate
            
            var testInfo = new TestInfo(testMethod, category, averageTime);

            // Add tags to the test info
            var tags = ExtractTestTags(testMethod);
            foreach (var tag in tags)
            {
                testInfo.Tags.Add(tag);
            }

            // Extract dependencies from test method and class
            var dependencies = ExtractTestDependencies(testMethod);
            foreach (var dependency in dependencies)
            {
                testInfo.Dependencies.Add(dependency);
            }

            return testInfo;
        }

        private TestCategory CategorizeTest(Core.Models.TestMethod testMethod)
        {
            // Use injected categorizer if available, otherwise use default implementation
            var categorizer = _testCategorizer ?? new DefaultTestCategorizer();
            var testInfo = new Core.Models.TestCategorizationInfo(testMethod);
            return categorizer.CategorizeAsync(testInfo, CancellationToken.None).GetAwaiter().GetResult();
        }

        private List<string> ExtractTestTags(Core.Models.TestMethod testMethod)
        {
            var tags = new List<string>();
            
            // Add category as a tag
            var category = CategorizeTest(testMethod);
            tags.Add(category.ToString());

            // You could add more sophisticated tag extraction from attributes here
            
            return tags;
        }

        private List<string> ExtractTestDependencies(Core.Models.TestMethod testMethod)
        {
            var dependencies = new List<string>();
            
            try
            {
                var className = testMethod.MethodInfo.DeclaringType?.Name ?? "";
                var methodName = testMethod.MethodInfo.Name;
                var namespaceName = testMethod.MethodInfo.DeclaringType?.Namespace ?? "";

                // Extract dependencies based on test naming patterns
                if (className.EndsWith("Tests") || className.EndsWith("Test"))
                {
                    var baseClassName = className.Replace("Tests", "").Replace("Test", "");
                    
                    // Add direct class dependency
                    if (!string.IsNullOrEmpty(baseClassName))
                    {
                        // Handle NUnitTestDiscoveryTests -> NUnitTestDiscovery mapping
                        if (baseClassName == "NUnitTestDiscovery")
                        {
                            dependencies.Add("TestIntelligence.Core.Discovery.NUnitTestDiscovery");
                            dependencies.Add("TestIntelligence.Core.Discovery.NUnitTestDiscovery.DiscoverTestsAsync");
                            dependencies.Add("TestIntelligence.Core.Discovery.ITestDiscovery");
                        }
                        else
                        {
                            // Generic pattern for other tests
                            dependencies.Add($"{namespaceName.Replace(".Tests", "")}.{baseClassName}");
                        }
                    }
                }

                // Method-specific dependencies
                if (methodName.Contains("DiscoverTests"))
                {
                    dependencies.Add("TestIntelligence.Core.Discovery.NUnitTestDiscovery.DiscoverTestsAsync");
                    dependencies.Add("TestIntelligence.Core.Discovery.ITestDiscovery.DiscoverTestsAsync");
                }

                if (methodName.Contains("CreateNUnitTestDiscovery"))
                {
                    dependencies.Add("TestIntelligence.Core.Discovery.TestDiscoveryFactory.CreateNUnitTestDiscovery");
                    dependencies.Add("TestIntelligence.Core.Discovery.NUnitTestDiscovery");
                }

                // Assembly-based dependencies
                if (namespaceName.Contains("Core.Tests"))
                {
                    dependencies.Add("TestIntelligence.Core");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extracting dependencies for test: {TestName}", testMethod.MethodInfo.Name);
            }

            return dependencies.Distinct().ToList();
        }

        private TestInfo ConvertToTestInfoForCategorization(Core.Models.TestMethod testMethod)
        {
            // Create a minimal TestInfo for categorization purposes
            var category = TestCategory.Unit; // Default value, will be overridden by categorizer
            var averageTime = TimeSpan.FromMilliseconds(100); // Default estimate
            return new TestInfo(testMethod, category, averageTime);
        }
    }
}