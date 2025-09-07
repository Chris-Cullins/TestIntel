using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.Core.Services
{
    /// <summary>
    /// Centralized service for resolving assembly paths from various sources.
    /// Consolidates assembly path resolution logic that was scattered across multiple classes.
    /// </summary>
    public class AssemblyPathResolver : IAssemblyPathResolver
    {
        private readonly ILogger<AssemblyPathResolver> _logger;

        // Common test assembly naming patterns
        private static readonly string[] TestAssemblyPatterns = new[]
        {
            "*Test*.dll",
            "*.Tests.dll",
            "*.Test.dll",
            "*Tests.dll",
            "*.UnitTests.dll",
            "*.IntegrationTests.dll",
            "*.E2ETests.dll"
        };

        // Common target frameworks to try when auto-detecting
        private static readonly string[] CommonFrameworks = new[]
        {
            "net8.0",
            "net7.0",
            "net6.0",
            "netcoreapp3.1",
            "netstandard2.1",
            "netstandard2.0",
            "net48",
            "net472",
            "net471"
        };

        public AssemblyPathResolver(ILogger<AssemblyPathResolver> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<string>> FindTestAssembliesInSolutionAsync(string solutionPath)
        {
            if (string.IsNullOrWhiteSpace(solutionPath))
                throw new ArgumentException("Solution path cannot be null or empty", nameof(solutionPath));

            var assemblies = new List<string>();
            var solutionDir = Path.GetDirectoryName(solutionPath);
            
            if (string.IsNullOrEmpty(solutionDir))
                return assemblies;

            try
            {
                _logger.LogDebug("Finding test assemblies in solution: {SolutionPath}", solutionPath);

                // First, try to find through project files
                var testProjects = await FindTestProjectsInSolutionAsync(solutionPath);
                foreach (var projectPath in testProjects)
                {
                    var assemblyPath = ResolveAssemblyPath(projectPath);
                    if (File.Exists(assemblyPath))
                    {
                        assemblies.Add(assemblyPath);
                        _logger.LogDebug("Found test assembly from project: {AssemblyPath}", assemblyPath);
                    }
                }

                // Fallback: search for test assemblies by pattern
                if (assemblies.Count == 0)
                {
                    _logger.LogDebug("No assemblies found through projects, falling back to pattern search");
                    assemblies.AddRange(SearchTestAssembliesByPattern(solutionDir));
                }

                // Remove duplicates and prefer Debug over Release
                var uniqueAssemblies = assemblies
                    .GroupBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderBy(f => f.Contains("Release", StringComparison.OrdinalIgnoreCase) ? 1 : 0).First())
                    .ToList();

                _logger.LogDebug("Found {AssemblyCount} unique test assemblies", uniqueAssemblies.Count);
                return uniqueAssemblies;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding test assemblies in solution: {SolutionPath}", solutionPath);
                return assemblies;
            }
        }

        public async Task<IReadOnlyList<string>> FindAllAssembliesInSolutionAsync(string solutionPath)
        {
            if (string.IsNullOrWhiteSpace(solutionPath))
                throw new ArgumentException("Solution path cannot be null or empty", nameof(solutionPath));

            var assemblies = new List<string>();

            try
            {
                _logger.LogDebug("Finding all assemblies in solution: {SolutionPath}", solutionPath);

                var allProjects = await FindAllProjectsInSolutionAsync(solutionPath);
                foreach (var projectPath in allProjects)
                {
                    var assemblyPath = ResolveAssemblyPath(projectPath);
                    if (File.Exists(assemblyPath))
                    {
                        assemblies.Add(assemblyPath);
                    }
                }

                _logger.LogDebug("Found {AssemblyCount} assemblies in solution", assemblies.Count);
                return assemblies;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding assemblies in solution: {SolutionPath}", solutionPath);
                return assemblies;
            }
        }

        public string ResolveAssemblyPath(string projectPath, string? preferredConfiguration = null, string? preferredFramework = null)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                throw new ArgumentException("Project path cannot be null or empty", nameof(projectPath));

            var projectDir = Path.GetDirectoryName(projectPath)!;
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            
            // Get target frameworks from project file
            var targetFrameworks = GetTargetFrameworksFromProject(projectPath);
            
            // Use provided framework or detect from project
            var frameworks = !string.IsNullOrEmpty(preferredFramework) 
                ? new[] { preferredFramework }
                : targetFrameworks.Any() ? targetFrameworks.ToArray() : CommonFrameworks;
            
            // Use provided configuration or default to Debug, then Release
            var configurations = !string.IsNullOrEmpty(preferredConfiguration)
                ? new[] { preferredConfiguration }
                : new[] { "Debug", "Release" };

            var possiblePaths = new List<string>();
            
            // Try all combinations of configurations and frameworks
            foreach (var config in configurations)
            {
                foreach (var framework in frameworks)
                {
                    possiblePaths.Add(Path.Combine(projectDir, "bin", config, framework, $"{projectName}.dll"));
                }
            }

            // Return first existing path, or first possible path as fallback
            var existingPath = possiblePaths.FirstOrDefault(File.Exists);
            if (existingPath != null)
            {
                _logger.LogDebug("Resolved existing assembly path: {AssemblyPath}", existingPath);
                return existingPath;
            }

            var defaultPath = possiblePaths.First();
            _logger.LogDebug("Assembly not found, using default path: {AssemblyPath}", defaultPath);
            return defaultPath;
        }

        public IReadOnlyList<string> FindAssembliesInDirectory(string directoryPath, string searchPattern = "*.dll", bool includeTestAssemblies = true)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

            if (!Directory.Exists(directoryPath))
                return Array.Empty<string>();

            try
            {
                var assemblies = Directory.GetFiles(directoryPath, searchPattern, SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!includeTestAssemblies)
                {
                    assemblies = assemblies.Where(a => !IsTestAssembly(a)).ToList();
                }

                _logger.LogDebug("Found {AssemblyCount} assemblies in directory: {DirectoryPath}", assemblies.Count, directoryPath);
                return assemblies;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding assemblies in directory: {DirectoryPath}", directoryPath);
                return Array.Empty<string>();
            }
        }

        public bool IsTestAssembly(string assemblyPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
                return false;

            var fileName = Path.GetFileName(assemblyPath);
            
            // Check against test assembly patterns
            return TestAssemblyPatterns.Any(pattern => 
                IsPatternMatch(fileName, pattern.Replace("*.dll", "", StringComparison.OrdinalIgnoreCase)));
        }

        private async Task<List<string>> FindTestProjectsInSolutionAsync(string solutionPath)
        {
            var testProjects = new List<string>();

            try
            {
                var allProjects = await FindAllProjectsInSolutionAsync(solutionPath);
                
                foreach (var projectPath in allProjects)
                {
                    if (IsTestProject(projectPath))
                    {
                        testProjects.Add(projectPath);
                    }
                }

                _logger.LogDebug("Found {TestProjectCount} test projects out of {TotalProjectCount}", 
                    testProjects.Count, allProjects.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding test projects in solution: {SolutionPath}", solutionPath);
            }

            return testProjects;
        }

        private async Task<List<string>> FindAllProjectsInSolutionAsync(string solutionPath)
        {
            var projects = new List<string>();

            try
            {
                if (!File.Exists(solutionPath))
                    return projects;

                var solutionContent = await File.ReadAllTextAsync(solutionPath);
                var solutionDir = Path.GetDirectoryName(solutionPath)!;

                var lines = solutionContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.StartsWith("Project(", StringComparison.OrdinalIgnoreCase) && 
                        line.Contains(".csproj", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(',');
                        if (parts.Length >= 2)
                        {
                            var projectRelativePath = parts[1].Trim().Trim('"');
                            var projectPath = Path.Combine(solutionDir, projectRelativePath.Replace('\\', Path.DirectorySeparatorChar));
                            
                            if (File.Exists(projectPath))
                            {
                                projects.Add(projectPath);
                            }
                        }
                    }
                }

                _logger.LogDebug("Found {ProjectCount} projects in solution", projects.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing solution file: {SolutionPath}", solutionPath);
            }

            return projects;
        }

        private bool IsTestProject(string projectPath)
        {
            try
            {
                // Check by naming convention first
                var projectName = Path.GetFileNameWithoutExtension(projectPath);
                if (IsPatternMatch(projectName, "Test") || IsPatternMatch(projectName, "Tests"))
                {
                    return true;
                }

                // Check project file for test framework references
                var projectContent = File.ReadAllText(projectPath);
                var testFrameworkIndicators = new[]
                {
                    "Microsoft.NET.Test.Sdk",
                    "NUnit",
                    "xunit",
                    "MSTest",
                    "NUnit3TestAdapter"
                };

                return testFrameworkIndicators.Any(indicator => 
                    projectContent.Contains(indicator, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error checking if project is test project: {ProjectPath}", projectPath);
                return false;
            }
        }

        private List<string> SearchTestAssembliesByPattern(string solutionDir)
        {
            var assemblies = new List<string>();

            try
            {
                foreach (var pattern in TestAssemblyPatterns)
                {
                    var files = Directory.GetFiles(solutionDir, pattern, SearchOption.AllDirectories)
                        .Where(f => (f.Contains("bin", StringComparison.OrdinalIgnoreCase)) && 
                                   (f.Contains("Debug", StringComparison.OrdinalIgnoreCase) || 
                                    f.Contains("Release", StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                    
                    assemblies.AddRange(files);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching for test assemblies by pattern in: {SolutionDir}", solutionDir);
            }

            return assemblies;
        }

        private List<string> GetTargetFrameworksFromProject(string projectPath)
        {
            var frameworks = new List<string>();

            try
            {
                if (!File.Exists(projectPath))
                    return frameworks;

                var doc = XDocument.Load(projectPath);
                
                // Look for TargetFramework (single) or TargetFrameworks (multiple)
                var targetFrameworkElements = doc.Descendants("TargetFramework");
                var targetFrameworksElements = doc.Descendants("TargetFrameworks");

                foreach (var element in targetFrameworkElements)
                {
                    var framework = element.Value?.Trim();
                    if (!string.IsNullOrEmpty(framework))
                    {
                        frameworks.Add(framework);
                    }
                }

                foreach (var element in targetFrameworksElements)
                {
                    var frameworksValue = element.Value?.Trim();
                    if (!string.IsNullOrEmpty(frameworksValue))
                    {
                        var multipleFrameworks = frameworksValue.Split(';', StringSplitOptions.RemoveEmptyEntries);
                        frameworks.AddRange(multipleFrameworks.Select(f => f.Trim()));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error reading target frameworks from project: {ProjectPath}", projectPath);
            }

            return frameworks.Distinct().ToList();
        }

        private static bool IsPatternMatch(string text, string pattern)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern))
                return false;

            return text.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}