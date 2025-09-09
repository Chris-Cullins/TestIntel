using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.Core.Discovery;
using TestIntelligence.Core.Services;
using TestIntelligence.TestComparison.Algorithms;
using TestIntelligence.TestComparison.Services;

namespace TestIntelligence.TestComparison.Tests;

/// <summary>
/// Base test class providing common infrastructure for test comparison tests.
/// Provides service provider creation and mock setup helpers.
/// </summary>
public abstract class TestBase
{
    /// <summary>
    /// Creates a service provider configured with all necessary dependencies for testing.
    /// </summary>
    /// <returns>Configured service provider</returns>
    protected IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Configures the service collection with dependencies needed for testing.
    /// Override this method in derived classes to customize service configuration.
    /// </summary>
    /// <param name="services">Service collection to configure</param>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
        // Logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

        // Mock core services
        services.AddSingleton(CreateMockTestCoverageMapBuilder());
        services.AddSingleton(CreateMockTestCoverageQuery());
        services.AddSingleton(CreateMockTestDiscovery());

        // Real implementations for testing
        services.AddScoped<ISimilarityCalculator, SimilarityCalculator>();
        services.AddScoped<TestCoverageComparisonService>();
        services.AddScoped<OptimizationRecommendationEngine>();
        services.AddScoped<ITestComparisonService, TestComparisonService>();
    }

    /// <summary>
    /// Creates a mock ITestCoverageMapBuilder for testing.
    /// Override this method to customize mock behavior.
    /// </summary>
    protected virtual ITestCoverageMapBuilder CreateMockTestCoverageMapBuilder()
    {
        var mock = Substitute.For<ITestCoverageMapBuilder>();
        
        // Default behavior: return an empty coverage map
        var emptyCoverageMap = new Core.Models.TestCoverageMap(
            new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Core.Models.TestCoverageInfo>>(),
            DateTime.UtcNow,
            "test-solution.sln");
        
        mock.BuildTestCoverageMapAsync(Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(System.Threading.Tasks.Task.FromResult(emptyCoverageMap));

        return mock;
    }

    /// <summary>
    /// Creates a mock ITestCoverageQuery for testing.
    /// Override this method to customize mock behavior.
    /// </summary>
    protected virtual ITestCoverageQuery CreateMockTestCoverageQuery()
    {
        var mock = Substitute.For<ITestCoverageQuery>();
        
        // Default behavior: return empty test coverage lists
        var emptyCoverageList = new System.Collections.Generic.List<Core.Models.TestCoverageInfo>().AsReadOnly();
        var emptyCoverageDict = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<Core.Models.TestCoverageInfo>>();

        mock.FindTestsExercisingMethodAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(emptyCoverageList);

        mock.FindTestsExercisingMethodsAsync(Arg.Any<System.Collections.Generic.IEnumerable<string>>(), Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(emptyCoverageDict as System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyList<Core.Models.TestCoverageInfo>>);

        return mock;
    }

    /// <summary>
    /// Creates a mock ITestDiscovery for testing.
    /// Override this method to customize mock behavior.
    /// </summary>
    protected virtual ITestDiscovery CreateMockTestDiscovery()
    {
        var mock = Substitute.For<ITestDiscovery>();
        
        // Default behavior: return empty discovery results
        var emptyDiscoveryResult = new TestDiscoveryResult(
            "test-assembly.dll",
            Core.Assembly.FrameworkVersion.Net5Plus,
            new System.Collections.Generic.List<Core.Models.TestFixture>().AsReadOnly(),
            new System.Collections.Generic.List<string>().AsReadOnly());
        
        mock.DiscoverTestsAsync(Arg.Any<Core.Assembly.ITestAssembly>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(System.Threading.Tasks.Task.FromResult(emptyDiscoveryResult));

        return mock;
    }

    /// <summary>
    /// Creates a sample test coverage map with specified test methods and their coverage.
    /// Useful for setting up test scenarios.
    /// </summary>
    /// <param name="testCoverageData">Dictionary mapping production methods to lists of test coverage info</param>
    /// <param name="solutionPath">Solution path for the coverage map</param>
    /// <returns>Test coverage map configured with the provided data</returns>
    protected Core.Models.TestCoverageMap CreateTestCoverageMap(
        System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Core.Models.TestCoverageInfo>> testCoverageData,
        string solutionPath = "test-solution.sln")
    {
        return new Core.Models.TestCoverageMap(testCoverageData, DateTime.UtcNow, solutionPath);
    }

    /// <summary>
    /// Creates a sample TestCoverageInfo object for testing.
    /// </summary>
    /// <param name="testMethodId">Test method identifier</param>
    /// <param name="testMethodName">Test method name</param>
    /// <param name="testClassName">Test class name</param>
    /// <param name="testAssembly">Test assembly name</param>
    /// <param name="callPath">Call path from test to production method</param>
    /// <param name="confidence">Confidence score (default 0.9)</param>
    /// <param name="testType">Test type (default Unit)</param>
    /// <returns>Configured TestCoverageInfo object</returns>
    protected Core.Models.TestCoverageInfo CreateTestCoverageInfo(
        string testMethodId,
        string testMethodName,
        string testClassName = "TestClass",
        string testAssembly = "TestAssembly",
        string[]? callPath = null,
        double confidence = 0.9,
        Core.Models.TestType testType = Core.Models.TestType.Unit)
    {
        callPath ??= new[] { testMethodId, "ProductionMethod" };
        
        return new Core.Models.TestCoverageInfo(
            testMethodId,
            testMethodName,
            testClassName,
            testAssembly,
            callPath,
            confidence,
            testType);
    }

    /// <summary>
    /// Creates a sample TestInfo object for testing.
    /// </summary>
    /// <param name="testMethodId">Test method identifier</param>
    /// <param name="category">Test category</param>
    /// <param name="executionTime">Average execution time</param>
    /// <param name="selectionScore">Selection score</param>
    /// <param name="tags">Tags for the test</param>
    /// <returns>Configured TestInfo object</returns>
    protected SelectionEngine.Models.TestInfo CreateTestInfo(
        string testMethodId,
        Core.Models.TestCategory category = Core.Models.TestCategory.Unit,
        TimeSpan? executionTime = null,
        double selectionScore = 0.5,
        string[]? tags = null)
    {
        executionTime ??= TimeSpan.FromMilliseconds(100);
        tags ??= Array.Empty<string>();

        // Create a mock MethodInfo since TestMethod constructor requires it
        // Use a method that definitely exists and is public
        var mockMethodInfo = typeof(TestBase).GetMethod(nameof(CreateServiceProvider), Type.EmptyTypes);
        if (mockMethodInfo == null)
        {
            // Fallback to any public method
            mockMethodInfo = typeof(TestBase).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .FirstOrDefault(m => !m.IsSpecialName);
        }
        
        if (mockMethodInfo == null)
        {
            throw new InvalidOperationException("Could not find a suitable method for creating test mock");
        }
        
        var mockDeclaringType = typeof(TestBase);
        
        var testMethod = new Core.Models.TestMethod(
            mockMethodInfo,
            mockDeclaringType,
            "TestAssembly.dll",
            Core.Assembly.FrameworkVersion.Net5Plus);

        var testInfo = new SelectionEngine.Models.TestInfo(
            testMethod,
            category,
            executionTime.Value,
            selectionScore);

        // Add tags
        foreach (var tag in tags)
        {
            testInfo.Tags.Add(tag);
        }

        return testInfo;
    }

    /// <summary>
    /// Creates a service provider with custom mock configurations.
    /// </summary>
    /// <param name="configureServices">Action to customize service configuration</param>
    /// <returns>Configured service provider</returns>
    protected IServiceProvider CreateServiceProvider(Action<IServiceCollection> configureServices)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        configureServices(services);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Gets a service from the default service provider.
    /// </summary>
    /// <typeparam name="T">Type of service to retrieve</typeparam>
    /// <returns>Service instance</returns>
    protected T GetService<T>() where T : notnull
    {
        var serviceProvider = CreateServiceProvider();
        return serviceProvider.GetRequiredService<T>();
    }

    /// <summary>
    /// Gets a service from a custom service provider.
    /// </summary>
    /// <typeparam name="T">Type of service to retrieve</typeparam>
    /// <param name="serviceProvider">Service provider to use</param>
    /// <returns>Service instance</returns>
    protected T GetService<T>(IServiceProvider serviceProvider) where T : notnull
    {
        return serviceProvider.GetRequiredService<T>();
    }
}