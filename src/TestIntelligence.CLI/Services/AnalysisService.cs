using Microsoft.Extensions.Logging;
using System.IO;
using TestIntelligence.CLI.Models;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Discovery;
using TestIntelligence.SelectionEngine.Models;

namespace TestIntelligence.CLI.Services;

/// <summary>
/// Implementation of analysis service for CLI operations.
/// </summary>
public class AnalysisService : IAnalysisService
{
    private readonly ILogger<AnalysisService> _logger;
    private readonly IOutputFormatter _outputFormatter;

    public AnalysisService(ILogger<AnalysisService> logger, IOutputFormatter outputFormatter)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _outputFormatter = outputFormatter ?? throw new ArgumentNullException(nameof(outputFormatter));
    }

    public async Task AnalyzeAsync(string path, string? outputPath, string format, bool verbose)
    {
        try
        {
            _logger.LogInformation("Starting analysis of: {Path}", path);

            if (verbose)
            {
                _logger.LogInformation("Verbose mode enabled");
            }

            // Validate input path
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new FileNotFoundException($"Path not found: {path}");
            }

            var analysisResult = await PerformAnalysisAsync(path, verbose);

            await _outputFormatter.WriteOutputAsync(analysisResult, format, outputPath);

            _logger.LogInformation("Analysis completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during analysis");
            throw;
        }
    }

    private async Task<AnalysisResult> PerformAnalysisAsync(string path, bool verbose)
    {
        var result = new AnalysisResult
        {
            AnalyzedPath = path,
            Timestamp = DateTimeOffset.UtcNow,
            Assemblies = new List<AssemblyAnalysis>()
        };

        // Discover assemblies to analyze
        var assemblyPaths = await DiscoverAssembliesAsync(path);
        
        _logger.LogInformation("Found {Count} assemblies to analyze", assemblyPaths.Count);

        foreach (var assemblyPath in assemblyPaths)
        {
            try
            {
                var assemblyAnalysis = await AnalyzeAssemblyAsync(assemblyPath, verbose);
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

    private async Task<List<string>> DiscoverAssembliesAsync(string path)
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
                // Parse solution file to find test projects
                var solutionDir = Path.GetDirectoryName(path)!;
                var testProjectPaths = await FindTestProjectsInSolutionAsync(path);
                
                foreach (var projectPath in testProjectPaths)
                {
                    var assemblyPath = GetAssemblyPathFromProject(projectPath);
                    if (File.Exists(assemblyPath))
                    {
                        assemblies.Add(assemblyPath);
                    }
                }
            }
        }
        else if (Directory.Exists(path))
        {
            // Find all test assemblies in directory
            var dllFiles = Directory.GetFiles(path, "*.dll", SearchOption.AllDirectories)
                .Where(f => f.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                           f.Contains("spec", StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.Contains("obj", StringComparison.OrdinalIgnoreCase));
            
            assemblies.AddRange(dllFiles);
        }

        return assemblies.Distinct().ToList();
    }

    private async Task<AssemblyAnalysis> AnalyzeAssemblyAsync(string assemblyPath, bool verbose)
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
            var loader = new CrossFrameworkAssemblyLoader();
            var loadResult = await loader.LoadAssemblyAsync(assemblyPath);
            
            if (!loadResult.IsSuccess || loadResult.Assembly == null)
            {
                throw new InvalidOperationException($"Failed to load assembly: {string.Join(", ", loadResult.Errors)}");
            }
            
            var discovery = TestDiscoveryFactory.CreateNUnitTestDiscovery();
            var discoveryResult = await discovery.DiscoverTestsAsync(loadResult.Assembly);
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
                    methodAnalysis.Dependencies = await ExtractDependenciesAsync(testMethod);
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
        var projects = new List<string>();
        
        if (!File.Exists(solutionPath))
            return projects;

        _logger.LogDebug("Parsing solution file: {SolutionPath}", solutionPath);
        var solutionContent = await File.ReadAllTextAsync(solutionPath);
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
                    
                    if (File.Exists(fullProjectPath) && await IsTestProjectAsync(fullProjectPath))
                    {
                        _logger.LogDebug("Found test project: {ProjectPath}", fullProjectPath);
                        projects.Add(fullProjectPath);
                    }
                }
            }
        }

        _logger.LogInformation("Found {Count} test projects in solution", projects.Count);
        return projects;
    }

    private string GetAssemblyPathFromProject(string projectPath)
    {
        var projectDir = Path.GetDirectoryName(projectPath)!;
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        
        // Try to detect target framework from project file
        var targetFrameworks = GetTargetFrameworksFromProject(projectPath);
        
        var possiblePaths = new List<string>();
        
        // Try different configurations and frameworks
        var configurations = new[] { "Debug", "Release" };
        
        foreach (var config in configurations)
        {
            foreach (var framework in targetFrameworks)
            {
                possiblePaths.Add(Path.Combine(projectDir, "bin", config, framework, $"{projectName}.dll"));
            }
        }
        
        // Fallback to common frameworks if none detected
        if (!targetFrameworks.Any())
        {
            foreach (var config in configurations)
            {
                possiblePaths.AddRange(new[]
                {
                    Path.Combine(projectDir, "bin", config, "net8.0", $"{projectName}.dll"),
                    Path.Combine(projectDir, "bin", config, "net6.0", $"{projectName}.dll"),
                    Path.Combine(projectDir, "bin", config, "net5.0", $"{projectName}.dll"),
                    Path.Combine(projectDir, "bin", config, "netcoreapp3.1", $"{projectName}.dll"),
                    Path.Combine(projectDir, "bin", config, "netstandard2.0", $"{projectName}.dll")
                });
            }
        }

        var existingPath = possiblePaths.FirstOrDefault(File.Exists);
        if (existingPath != null)
        {
            _logger.LogDebug("Found assembly at: {AssemblyPath}", existingPath);
            return existingPath;
        }
        
        _logger.LogWarning("Assembly not found for project {ProjectPath}, using default path", projectPath);
        return possiblePaths.First();
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
            var projectContent = await File.ReadAllTextAsync(projectPath);
            
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
    
    /// <summary>
    /// Extracts target framework versions from a project file.
    /// </summary>
    private List<string> GetTargetFrameworksFromProject(string projectPath)
    {
        var frameworks = new List<string>();
        
        try
        {
            var projectContent = File.ReadAllText(projectPath);
            
            // Look for TargetFramework (single) or TargetFrameworks (multiple)
            var lines = projectContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                if (trimmed.StartsWith("<TargetFramework>", StringComparison.OrdinalIgnoreCase))
                {
                    var framework = ExtractXmlElementContent(trimmed, "TargetFramework");
                    if (!string.IsNullOrEmpty(framework))
                    {
                        frameworks.Add(framework);
                    }
                }
                else if (trimmed.StartsWith("<TargetFrameworks>", StringComparison.OrdinalIgnoreCase))
                {
                    var frameworksString = ExtractXmlElementContent(trimmed, "TargetFrameworks");
                    if (!string.IsNullOrEmpty(frameworksString))
                    {
                        frameworks.AddRange(frameworksString.Split(';', StringSplitOptions.RemoveEmptyEntries)
                            .Select(f => f.Trim()));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse target frameworks from: {ProjectPath}", projectPath);
        }
        
        return frameworks;
    }
    
    /// <summary>
    /// Extracts content from an XML element in a simple way.
    /// </summary>
    private string? ExtractXmlElementContent(string line, string elementName)
    {
        var startTag = $"<{elementName}>";
        var endTag = $"</{elementName}>";
        
        var startIndex = line.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        var endIndex = line.IndexOf(endTag, StringComparison.OrdinalIgnoreCase);
        
        if (startIndex >= 0 && endIndex > startIndex)
        {
            var contentStart = startIndex + startTag.Length;
            var contentLength = endIndex - contentStart;
            return line.Substring(contentStart, contentLength).Trim();
        }
        
        return null;
    }
    
    /// <summary>
    /// Determines if an assembly name represents a test framework.
    /// </summary>
    private bool IsTestFrameworkAssembly(string? assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return false;
            
        var testFrameworkNames = new[]
        {
            "xunit", "nunit", "mstest", "Microsoft.VisualStudio.TestPlatform",
            "FluentAssertions", "Shouldly", "Moq", "NSubstitute", "FakeItEasy",
            "Microsoft.NET.Test", "TestAdapter", "TestFramework"
        };
        
        return testFrameworkNames.Any(framework => 
            assemblyName.Contains(framework, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Determines if an assembly name represents a system/runtime assembly.
    /// </summary>
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