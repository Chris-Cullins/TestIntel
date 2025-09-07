using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Models;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Discovery;
using TestIntelligence.Core.Utilities;
using TestIntelligence.SelectionEngine.Models;

namespace TestIntelligence.CLI.Services;

/// <summary>
/// Service responsible for analyzing individual assemblies and extracting test method information.
/// </summary>
public interface ITestAnalysisService
{
    /// <summary>
    /// Analyzes a single assembly and extracts test method information.
    /// </summary>
    Task<AssemblyAnalysis> AnalyzeAssemblyAsync(string assemblyPath, bool verbose, CrossFrameworkAssemblyLoader? sharedLoader = null);

    /// <summary>
    /// Categorizes a test method based on its characteristics.
    /// </summary>
    Task<TestCategory> CategorizeTestMethodAsync(Core.Models.TestMethod testMethod);

    /// <summary>
    /// Extracts tags/attributes from a test method.
    /// </summary>
    List<string> ExtractTags(Core.Models.TestMethod testMethod);

    /// <summary>
    /// Extracts dependencies for a test method (for verbose analysis).
    /// </summary>
    Task<List<string>> ExtractDependenciesAsync(Core.Models.TestMethod testMethod);
}

/// <summary>
/// Implementation of test analysis that handles assembly loading and test method analysis.
/// </summary>
public class TestAnalysisService : ITestAnalysisService
{
    private readonly ILogger<TestAnalysisService> _logger;

    // Test framework and system assembly filters
    private static readonly string[] TestFrameworkNames = new[]
    {
        "xunit", "nunit", "mstest", "Microsoft.VisualStudio.TestPlatform",
        "FluentAssertions", "Shouldly", "Moq", "NSubstitute", "FakeItEasy",
        "Microsoft.NET.Test", "TestAdapter", "TestFramework"
    };

    private static readonly string[] SystemPrefixes = new[]
    {
        "System", "Microsoft.Extensions", "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore", "Newtonsoft", "mscorlib",
        "netstandard", "Microsoft.CSharp", "Microsoft.Win32"
    };

    public TestAnalysisService(ILogger<TestAnalysisService> logger)
    {
        _logger = ExceptionHelper.ThrowIfNull(logger, nameof(logger));
    }

    public async Task<AssemblyAnalysis> AnalyzeAssemblyAsync(string assemblyPath, bool verbose, CrossFrameworkAssemblyLoader? sharedLoader = null)
    {
        ExceptionHelper.ThrowIfNullOrWhiteSpace(assemblyPath, nameof(assemblyPath));

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
            var loadResult = await loader.LoadAssemblyAsync(assemblyPath);

            if (!loadResult.IsSuccess || loadResult.Assembly == null)
            {
                throw new InvalidOperationException($"Failed to load assembly: {string.Join(", ", loadResult.Errors)}");
            }

            var discovery = TestDiscoveryFactory.CreateNUnitTestDiscovery();
            var discoveryResult = await discovery.DiscoverTestsAsync(loadResult.Assembly);

            ProcessDiscoveryErrors(discoveryResult, assemblyPath);

            var testMethods = discoveryResult.GetAllTestMethods();
            await ProcessTestMethods(testMethods, analysis, verbose);

            analysis.Framework = loadResult.Assembly.FrameworkVersion.ToString();
        }
        catch (Exception ex)
        {
            analysis.Error = ex.Message;
            ExceptionHelper.LogException(_logger, ex, "analyzing assembly", new { assemblyPath });
        }

        return analysis;
    }

    public Task<TestCategory> CategorizeTestMethodAsync(Core.Models.TestMethod testMethod)
    {
        ExceptionHelper.ThrowIfNull(testMethod, nameof(testMethod));

        var methodName = testMethod.MethodInfo.Name.ToLowerInvariant();

        // Simple categorization based on method name patterns
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

    public List<string> ExtractTags(Core.Models.TestMethod testMethod)
    {
        ExceptionHelper.ThrowIfNull(testMethod, nameof(testMethod));

        var tags = new List<string>();

        try
        {
            // Extract tags from method attributes
            foreach (var attribute in testMethod.MethodInfo.GetCustomAttributes(true))
            {
                var attributeName = attribute.GetType().Name.ToLowerInvariant();

                if (attributeName.Contains("category"))
                    tags.Add("category");
                if (attributeName.Contains("slow"))
                    tags.Add("slow");
                if (attributeName.Contains("integration"))
                    tags.Add("integration");
            }
        }
        catch (Exception ex)
        {
            ExceptionHelper.LogException(_logger, ex, "extracting tags", new { testMethod = testMethod.GetDisplayName() });
        }

        return tags.Distinct().ToList();
    }

    public Task<List<string>> ExtractDependenciesAsync(Core.Models.TestMethod testMethod)
    {
        ExceptionHelper.ThrowIfNull(testMethod, nameof(testMethod));

        return Task.FromResult(ExceptionHelper.ExecuteSafely(() =>
        {
            var assembly = testMethod.MethodInfo.DeclaringType?.Assembly;
            if (assembly == null)
                return new List<string>();

            // Get non-test, non-system referenced assemblies
            var referencedAssemblies = assembly.GetReferencedAssemblies()
                .Where(an => !IsTestFrameworkAssembly(an.Name) && !IsSystemAssembly(an.Name))
                .Select(an => an.Name)
                .Where(name => !string.IsNullOrEmpty(name))
                .Cast<string>()
                .Distinct()
                .OrderBy(name => name)
                .ToList();

            return referencedAssemblies;
        }, new List<string>(), _logger, $"extracting dependencies for {testMethod.GetDisplayName()}"));
    }

    private void ProcessDiscoveryErrors(TestDiscoveryResult discoveryResult, string assemblyPath)
    {
        if (!discoveryResult.Errors.Any())
            return;

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

    private async Task ProcessTestMethods(IEnumerable<Core.Models.TestMethod> testMethods, AssemblyAnalysis analysis, bool verbose)
    {
        foreach (var testMethod in testMethods)
        {
            try
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
            catch (Exception ex)
            {
                ExceptionHelper.LogException(_logger, ex, "processing test method", new { testMethod = testMethod.GetDisplayName() });
            }
        }
    }

    private bool IsTestFrameworkAssembly(string? assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return false;

        return TestFrameworkNames.Any(framework =>
            assemblyName.Contains(framework, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsSystemAssembly(string? assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return false;

        return SystemPrefixes.Any(prefix =>
            assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}