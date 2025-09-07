using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Models;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Discovery;
using TestIntelligence.Core.Services;
using TestIntelligence.SelectionEngine.Models;

namespace TestIntelligence.CLI.Services;

/// <summary>
/// Implementation of analysis service for CLI operations.
/// </summary>
public class AnalysisService : IAnalysisService
{
    private readonly ILogger<AnalysisService> _logger;
    private readonly IOutputFormatter _outputFormatter;
    private readonly IConfigurationService _configurationService;
    private readonly IAssemblyPathResolver _assemblyPathResolver;

    public AnalysisService(
        ILogger<AnalysisService> logger, 
        IOutputFormatter outputFormatter,
        IConfigurationService configurationService,
        IAssemblyPathResolver assemblyPathResolver)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _outputFormatter = outputFormatter ?? throw new ArgumentNullException(nameof(outputFormatter));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _assemblyPathResolver = assemblyPathResolver ?? throw new ArgumentNullException(nameof(assemblyPathResolver));
    }

    public async Task AnalyzeAsync(string path, string? outputPath, string format, bool verbose)
    {
        try
        {
            _logger.LogInformation("Starting analysis of: {Path}", path);

            // Load configuration
            var configuration = await _configurationService.LoadConfigurationAsync(path).ConfigureAwait(false);
            
            // Override verbose setting if specified in configuration and not overridden by command line
            var effectiveVerbose = verbose || configuration.Analysis.Verbose;
            
            // Override format if not explicitly specified and configuration has a default
            var effectiveFormat = string.IsNullOrEmpty(format) || format == "text" 
                ? configuration.Output.Format 
                : format;

            if (effectiveVerbose)
            {
                _logger.LogInformation("Verbose mode enabled");
            }

            // Validate input path
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new FileNotFoundException($"Path not found: {path}");
            }

            var analysisResult = await PerformAnalysisAsync(path, effectiveVerbose, configuration).ConfigureAwait(false);

            // Use configured output directory if not specified
            var effectiveOutputPath = outputPath ?? 
                (configuration.Output.OutputDirectory != null 
                    ? Path.Combine(configuration.Output.OutputDirectory, $"analysis_{DateTime.Now:yyyyMMdd_HHmmss}.{(effectiveFormat == "json" ? "json" : "txt")}")
                    : null);

            await _outputFormatter.WriteOutputAsync(analysisResult, effectiveFormat, effectiveOutputPath).ConfigureAwait(false);

            _logger.LogInformation("Analysis completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during analysis");
            throw;
        }
    }

    private async Task<AnalysisResult> PerformAnalysisAsync(string path, bool verbose, TestIntelConfiguration configuration)
    {
        var result = new AnalysisResult
        {
            AnalyzedPath = path,
            Timestamp = DateTimeOffset.UtcNow,
            Assemblies = new List<AssemblyAnalysis>()
        };

        // Discover assemblies to analyze
        var allAssemblyPaths = await DiscoverAssembliesAsync(path, configuration).ConfigureAwait(false);
        
        // Apply configuration filtering
        var assemblyPaths = allAssemblyPaths;
        if (allAssemblyPaths.Count != assemblyPaths.Count)
        {
            _logger.LogInformation("Configuration filtering: {Original} → {Filtered} assemblies", 
                allAssemblyPaths.Count, assemblyPaths.Count);
        }
        
        _logger.LogInformation("Found {Count} assemblies to analyze", assemblyPaths.Count);

        // Use a single shared loader for the entire analysis to avoid assembly resolution conflicts
        using var sharedLoader = new CrossFrameworkAssemblyLoader();

        foreach (var assemblyPath in assemblyPaths)
        {
            try
            {
                _logger.LogDebug("Starting analysis of assembly: {AssemblyPath}", assemblyPath);
                var assemblyAnalysis = await AnalyzeAssemblyAsync(assemblyPath, verbose, sharedLoader).ConfigureAwait(false);
                _logger.LogDebug("Assembly {AssemblyName} analysis completed: {TestCount} tests found", 
                    Path.GetFileName(assemblyPath), assemblyAnalysis.TestMethods.Count);
                result.Assemblies.Add(assemblyAnalysis);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze assembly: {Assembly}", assemblyPath);
                
                result.Assemblies.Add(new AssemblyAnalysis
                {
                    AssemblyPath = assemblyPath,
                    Error = ex.Message,
                    TestMethods = new List<TestMethodAnalysis>()
                });
            }
        }

        // Calculate summary statistics
        result.Summary = new AnalysisSummary
        {
            TotalAssemblies = result.Assemblies.Count,
            TotalTestMethods = result.Assemblies.Sum(a => a.TestMethods.Count),
            SuccessfullyAnalyzed = result.Assemblies.Count(a => string.IsNullOrEmpty(a.Error)),
            FailedAnalyses = result.Assemblies.Count(a => !string.IsNullOrEmpty(a.Error)),
            CategoryBreakdown = CalculateCategoryBreakdown(result.Assemblies)
        };

        return result;
    }

    private async Task<List<string>> DiscoverAssembliesAsync(string path, TestIntelConfiguration configuration)
    {
        var assemblies = new List<string>();

        if (File.Exists(path))
        {
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                assemblies.Add(path);
            }
            else if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                // Parse solution file to find projects
                var solutionDir = Path.GetDirectoryName(path)!;
                var allProjectPaths = configuration.Projects.TestProjectsOnly 
                    ? await FindTestProjectsInSolutionAsync(path)
                    : await FindAllProjectsInSolutionAsync(path).ConfigureAwait(false);
                
                if (allProjectPaths == null)
                {
                    _logger.LogWarning("Project discovery returned null for solution: {SolutionPath}", path);
                    allProjectPaths = new List<string>();
                }
                
                // Apply configuration-based project filtering
                var filteredProjectPaths = _configurationService.FilterProjects(allProjectPaths, configuration) ?? new List<string>();
                
                foreach (var projectPath in filteredProjectPaths)
                {
                    var assemblyPath = _assemblyPathResolver.ResolveAssemblyPath(projectPath);
                    if (File.Exists(assemblyPath))
                    {
                        _logger.LogDebug("Found assembly: {AssemblyPath}", assemblyPath);
                        assemblies.Add(assemblyPath);
                    }
                    else
                    {
                        _logger.LogWarning("Assembly not found for project {ProjectPath}, expected at {AssemblyPath}", projectPath, assemblyPath);
                    }
                }
            }
        }
        else if (Directory.Exists(path))
        {
            // Find all assemblies in directory based on configuration
            var dllFiles = Directory.GetFiles(path, "*.dll", SearchOption.AllDirectories)
                .Where(f => !f.Contains("obj", StringComparison.OrdinalIgnoreCase));
            
            // Filter by test assemblies if configured
            if (configuration.Projects.TestProjectsOnly)
            {
                dllFiles = dllFiles.Where(f => 
                    f.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("spec", StringComparison.OrdinalIgnoreCase));
            }
            
            assemblies.AddRange(dllFiles);
        }

        return assemblies.Distinct().ToList();
    }

    private async Task<AssemblyAnalysis> AnalyzeAssemblyAsync(string assemblyPath, bool verbose, CrossFrameworkAssemblyLoader? sharedLoader = null)
    {
        _logger.LogDebug("Analyzing assembly: {Assembly}", assemblyPath);

        var analysis = new AssemblyAnalysis
        {
            AssemblyPath = assemblyPath,
            TestMethods = new List<TestMethodAnalysis>()
        };

        try
        {
            // Load assembly and discover tests
            var loader = sharedLoader ?? new CrossFrameworkAssemblyLoader();
            var shouldDisposeLoader = sharedLoader == null;
            var loadResult = await loader.LoadAssemblyAsync(assemblyPath).ConfigureAwait(false);
            
            if (!loadResult.IsSuccess || loadResult.Assembly == null)
            {
                throw new InvalidOperationException($"Failed to load assembly: {string.Join(", ", loadResult.Errors)}");
            }
            
            var discovery = TestDiscoveryFactory.CreateNUnitTestDiscovery();
            var discoveryResult = await discovery.DiscoverTestsAsync(loadResult.Assembly).ConfigureAwait(false);
            
            if (discoveryResult.Errors.Any())
            {
                // Check if this looks like a dependency resolution issue
                var hasDependencyIssues = discoveryResult.Errors.Any(e => 
                    e.Contains("Could not load file or assembly") || 
                    e.Contains("Could not resolve type"));
                
                if (hasDependencyIssues)
                {
                    _logger.LogWarning("Assembly {AssemblyName} has dependency resolution issues: {ErrorCount} errors", 
                        Path.GetFileName(assemblyPath), discoveryResult.Errors.Count);
                }
                else
                {
                    _logger.LogWarning("Assembly {AssemblyName} discovery errors: {Errors}", 
                        Path.GetFileName(assemblyPath), string.Join(", ", discoveryResult.Errors.Take(3)));
                }
            }
            
            var testMethods = discoveryResult.GetAllTestMethods();

            foreach (var testMethod in testMethods)
            {
                var methodAnalysis = new TestMethodAnalysis
                {
                    MethodName = testMethod.GetDisplayName(),
                    Category = await CategorizeTestMethodAsync(testMethod),
                    EstimatedDuration = TimeSpan.FromMilliseconds(100), // Default estimate
                    Tags = ExtractTags(testMethod),
                    Dependencies = new List<string>()
                };

                if (verbose)
                {
                    methodAnalysis.Dependencies = await ExtractDependenciesAsync(testMethod).ConfigureAwait(false);
                }

                analysis.TestMethods.Add(methodAnalysis);
            }

            analysis.Framework = loadResult.Assembly.FrameworkVersion.ToString();
        }
        catch (Exception ex)
        {
            analysis.Error = ex.Message;
        }

        return analysis;
    }

    private Task<TestCategory> CategorizeTestMethodAsync(Core.Models.TestMethod testMethod)
    {
        // Simplified categorization logic for CLI
        var methodName = testMethod.MethodInfo.Name.ToLower();
        
        if (methodName.Contains("database") || methodName.Contains("db"))
            return Task.FromResult(TestCategory.Database);
        
        if (methodName.Contains("api") || methodName.Contains("http"))
            return Task.FromResult(TestCategory.API);
        
        if (methodName.Contains("integration"))
            return Task.FromResult(TestCategory.Integration);
        
        if (methodName.Contains("ui") || methodName.Contains("selenium"))
            return Task.FromResult(TestCategory.UI);
        
        return Task.FromResult(TestCategory.Unit);
    }

    private List<string> ExtractTags(Core.Models.TestMethod testMethod)
    {
        var tags = new List<string>();
        
        // Extract tags from attributes
        foreach (var attribute in testMethod.MethodInfo.GetCustomAttributes(true))
        {
            var attributeName = attribute.GetType().Name.ToLower();
            
            if (attributeName.Contains("category"))
                tags.Add("category");
            if (attributeName.Contains("slow"))
                tags.Add("slow");
            if (attributeName.Contains("integration"))
                tags.Add("integration");
        }

        return tags;
    }

    private Task<List<string>> ExtractDependenciesAsync(Core.Models.TestMethod testMethod)
    {
        var dependencies = new List<string>();
        
        try
        {
            // Extract external dependencies, not test infrastructure
            var assembly = testMethod.MethodInfo.DeclaringType?.Assembly;
            if (assembly != null)
            {
                // Get referenced assemblies that aren't test frameworks or system assemblies
                var referencedAssemblies = assembly.GetReferencedAssemblies()
                    .Where(an => !IsTestFrameworkAssembly(an.Name) && !IsSystemAssembly(an.Name))
                    .Select(an => an.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Cast<string>()
                    .Distinct()
                    .OrderBy(name => name)
                    .ToList();
                
                dependencies.AddRange(referencedAssemblies);
            }
            
            // If no external dependencies found, this is likely a pure unit test
            if (!dependencies.Any())
            {
                // Return empty list for isolated unit tests
                return Task.FromResult(new List<string>());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract dependencies for test method: {Method}", testMethod.GetDisplayName());
        }
        
        return Task.FromResult(dependencies);
    }

    private async Task<List<string>> FindTestProjectsInSolutionAsync(string solutionPath)
    {
        var allProjects = await FindAllProjectsInSolutionAsync(solutionPath).ConfigureAwait(false);
        var testProjects = new List<string>();
        
        foreach (var projectPath in allProjects)
        {
            if (await IsTestProjectAsync(projectPath))
            {
                _logger.LogDebug("Found test project: {ProjectPath}", projectPath);
                testProjects.Add(projectPath);
            }
        }

        _logger.LogInformation("Found {Count} test projects in solution", testProjects.Count);
        return testProjects;
    }

    private async Task<List<string>> FindAllProjectsInSolutionAsync(string solutionPath)
    {
        var projects = new List<string>();
        
        if (!File.Exists(solutionPath))
            return projects;

        _logger.LogDebug("Parsing solution file: {SolutionPath}", solutionPath);
        var solutionContent = await File.ReadAllTextAsync(solutionPath).ConfigureAwait(false);
        var lines = solutionContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var solutionDir = Path.GetDirectoryName(solutionPath)!;
        
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("Project(") && trimmedLine.Contains(".csproj"))
            {
                // Parse: Project("{GUID}") = "ProjectName", "relative\path\Project.csproj", "{GUID}"
                var parts = trimmedLine.Split(',');
                if (parts.Length >= 2)
                {
                    var projectRelativePath = parts[1].Trim().Trim('"');
                    var fullProjectPath = Path.Combine(solutionDir, projectRelativePath.Replace('\\', Path.DirectorySeparatorChar));
                    
                    if (File.Exists(fullProjectPath))
                    {
                        _logger.LogDebug("Found project: {ProjectPath}", fullProjectPath);
                        projects.Add(fullProjectPath);
                    }
                }
            }
        }

        _logger.LogInformation("Found {Count} total projects in solution", projects.Count);
        return projects;
    }

    private Dictionary<TestCategory, int> CalculateCategoryBreakdown(List<AssemblyAnalysis> assemblies)
    {
        var breakdown = new Dictionary<TestCategory, int>();

        foreach (var assembly in assemblies)
        {
            foreach (var testMethod in assembly.TestMethods)
            {
                if (breakdown.ContainsKey(testMethod.Category))
                {
                    breakdown[testMethod.Category]++;
                }
                else
                {
                    breakdown[testMethod.Category] = 1;
                }
            }
        }

        return breakdown;
    }

    /// <summary>
    /// Determines if a project file represents a test project by examining its content and references.
    /// </summary>
    private async Task<bool> IsTestProjectAsync(string projectPath)
    {
        try
        {
            var projectContent = await File.ReadAllTextAsync(projectPath).ConfigureAwait(false);
            
            // Check for test indicators in project name/path
            var projectName = Path.GetFileNameWithoutExtension(projectPath).ToLowerInvariant();
            if (projectName.Contains("test") || projectName.Contains("spec"))
            {
                return true;
            }
            
            // Check for common test framework package references
            var testIndicators = new[]
            {
                "Microsoft.NET.Test.Sdk",
                "xunit", "nunit", "mstest",
                "FluentAssertions", "Shouldly",
                "Moq", "NSubstitute", "FakeItEasy"
            };
            
            return testIndicators.Any(indicator => 
                projectContent.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to analyze project file: {ProjectPath}", projectPath);
            return false;
        }
    }

    private bool IsTestFrameworkAssembly(string? assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return false;

        var testFrameworkPrefixes = new[]
        {
            "nunit", "xunit", "mstest", "Microsoft.VisualStudio.TestPlatform",
            "Microsoft.TestPlatform", "FluentAssertions", "Moq", "NSubstitute",
            "AutoFixture", "Shouldly", "Machine.Specifications"
        };

        return testFrameworkPrefixes.Any(prefix =>
            assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsSystemAssembly(string? assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return false;

        var systemPrefixes = new[]
        {
            "System", "Microsoft.Extensions", "Microsoft.AspNetCore", 
            "Microsoft.EntityFrameworkCore", "Newtonsoft", "mscorlib",
            "netstandard", "Microsoft.CSharp", "Microsoft.Win32"
        };

        return systemPrefixes.Any(prefix =>
            assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}