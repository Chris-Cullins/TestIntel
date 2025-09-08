using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Services;
using TestIntelligence.TestComparison.Models;

namespace TestIntelligence.TestComparison.Services;

/// <summary>
/// Service for analyzing coverage overlap between tests by integrating with the Core coverage analyzer.
/// Provides methods to get coverage sets, calculate overlap, and apply weighting based on call depth and complexity.
/// </summary>
public class TestCoverageComparisonService
{
    private readonly ITestCoverageMapBuilder _coverageMapBuilder;
    private readonly ITestCoverageQuery _coverageQuery;
    private readonly ISimilarityCalculator _similarityCalculator;
    private readonly ILogger<TestCoverageComparisonService> _logger;

    public TestCoverageComparisonService(
        ITestCoverageMapBuilder coverageMapBuilder,
        ITestCoverageQuery coverageQuery,
        ISimilarityCalculator similarityCalculator,
        ILogger<TestCoverageComparisonService> logger)
    {
        _coverageMapBuilder = coverageMapBuilder ?? throw new ArgumentNullException(nameof(coverageMapBuilder));
        _coverageQuery = coverageQuery ?? throw new ArgumentNullException(nameof(coverageQuery));
        _similarityCalculator = similarityCalculator ?? throw new ArgumentNullException(nameof(similarityCalculator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Analyzes coverage overlap between two test methods.
    /// </summary>
    /// <param name="test1Id">Full identifier of first test method</param>
    /// <param name="test2Id">Full identifier of second test method</param>
    /// <param name="solutionPath">Path to solution file</param>
    /// <param name="options">Weighting options for analysis</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detailed coverage overlap analysis</returns>
    public async Task<CoverageOverlapAnalysis> AnalyzeCoverageOverlapAsync(
        string test1Id, 
        string test2Id, 
        string solutionPath, 
        WeightingOptions options, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(test1Id)) throw new ArgumentException("Test 1 ID cannot be null or empty", nameof(test1Id));
        if (string.IsNullOrEmpty(test2Id)) throw new ArgumentException("Test 2 ID cannot be null or empty", nameof(test2Id));
        if (string.IsNullOrEmpty(solutionPath)) throw new ArgumentException("Solution path cannot be null or empty", nameof(solutionPath));

        _logger.LogDebug("Analyzing coverage overlap between {Test1Id} and {Test2Id}", test1Id, test2Id);

        try
        {
            // Build or retrieve the coverage map
            var coverageMap = await _coverageMapBuilder.BuildTestCoverageMapAsync(solutionPath, cancellationToken);

            // Get coverage for both tests
            var test1Coverage = await GetTestCoverageAsync(test1Id, coverageMap, cancellationToken);
            var test2Coverage = await GetTestCoverageAsync(test2Id, coverageMap, cancellationToken);

            _logger.LogDebug("Test {Test1Id} covers {Coverage1Count} methods, " +
                "Test {Test2Id} covers {Coverage2Count} methods",
                test1Id, test1Coverage.Count, test2Id, test2Coverage.Count);

            // Calculate overlap analysis
            return CalculateOverlapAnalysis(test1Coverage, test2Coverage, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze coverage overlap between {Test1Id} and {Test2Id}", test1Id, test2Id);
            throw;
        }
    }

    /// <summary>
    /// Gets the set of production methods covered by a specific test.
    /// This method reverses the typical coverage relationship to find which production methods
    /// are covered by the given test method.
    /// </summary>
    /// <param name="testId">Full identifier of the test method</param>
    /// <param name="coverageMap">Pre-built coverage map for the solution</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Set of production method names covered by the test</returns>
    private Task<HashSet<string>> GetTestCoverageAsync(
        string testId,
        Core.Models.TestCoverageMap coverageMap,
        CancellationToken cancellationToken = default)
    {
        var coveredMethods = new HashSet<string>();

        // Since the coverage map is structured as method -> tests,
        // we need to reverse lookup to find all methods covered by our test
        foreach (var methodToTestsPair in coverageMap.MethodToTests)
        {
            var productionMethodId = methodToTestsPair.Key;
            var testsCoveringMethod = methodToTestsPair.Value;

            // Check if our test is among the tests covering this production method
            var testCoverage = testsCoveringMethod.FirstOrDefault(tc => 
                string.Equals(tc.TestMethodId, testId, StringComparison.OrdinalIgnoreCase));

            if (testCoverage != null)
            {
                coveredMethods.Add(productionMethodId);
            }
        }

        _logger.LogDebug("Test {TestId} covers {CoveredMethodsCount} production methods", 
            testId, coveredMethods.Count);

        return Task.FromResult(coveredMethods);
    }

    /// <summary>
    /// Calculates detailed overlap analysis between two coverage sets.
    /// </summary>
    private CoverageOverlapAnalysis CalculateOverlapAnalysis(
        HashSet<string> test1Coverage,
        HashSet<string> test2Coverage,
        WeightingOptions options)
    {
        // Find shared and unique methods
        var sharedMethods = test1Coverage.Intersect(test2Coverage).ToList();
        var uniqueToTest1 = test1Coverage.Except(test2Coverage).ToList();
        var uniqueToTest2 = test2Coverage.Except(test1Coverage).ToList();

        // Calculate overlap percentage using Jaccard similarity
        var overlapPercentage = _similarityCalculator.CalculateCoverageOverlap(
            test1Coverage, test2Coverage, options) * 100.0;

        // Create shared method info with weighting details
        var sharedMethodInfos = sharedMethods.Select(method => CreateSharedMethodInfo(method, options)).ToList();

        var analysis = new CoverageOverlapAnalysis
        {
            SharedProductionMethods = sharedMethods.Count,
            UniqueToTest1 = uniqueToTest1.Count,
            UniqueToTest2 = uniqueToTest2.Count,
            OverlapPercentage = overlapPercentage,
            SharedMethods = sharedMethodInfos.AsReadOnly(),
            UniqueMethodsTest1 = uniqueToTest1.AsReadOnly(),
            UniqueMethodsTest2 = uniqueToTest2.AsReadOnly()
        };

        _logger.LogDebug("Coverage analysis complete: {SharedCount} shared, {Unique1Count} unique to test 1, " +
            "{Unique2Count} unique to test 2, {OverlapPercentage:F1}% overlap",
            analysis.SharedProductionMethods, analysis.UniqueToTest1, analysis.UniqueToTest2, analysis.OverlapPercentage);

        return analysis;
    }

    /// <summary>
    /// Creates detailed information about a shared method with weighting data.
    /// </summary>
    private SharedMethodInfo CreateSharedMethodInfo(string method, WeightingOptions options)
    {
        // Calculate weight based on method characteristics
        var weight = CalculateMethodWeight(method, options);
        
        // Assume high confidence for now - in a real implementation,
        // this would be based on the coverage analysis confidence
        var confidence = 0.9;

        // For now, assume call depth of 1 - in a real implementation,
        // this would come from the coverage analysis data
        var callDepth = 1;

        return new SharedMethodInfo
        {
            Method = method,
            Confidence = confidence,
            CallDepth = callDepth,
            Weight = weight,
            IsProductionCode = !IsFrameworkMethod(method),
            ContainerName = ExtractContainerName(method)
        };
    }

    /// <summary>
    /// Calculates the weight of a method based on the weighting options.
    /// </summary>
    private double CalculateMethodWeight(string methodName, WeightingOptions options)
    {
        var weight = 1.0;

        // Apply production vs framework code weighting
        if (IsFrameworkMethod(methodName))
        {
            weight *= options.FrameworkCodeWeight;
        }
        else
        {
            weight *= options.ProductionCodeWeight;
        }

        // Apply call depth decay (using default depth of 1 for now)
        // In a real implementation, this would use actual call depth data
        weight *= Math.Pow(options.CallDepthDecayFactor, 0); // 0 decay for depth 1

        // Apply complexity weighting if enabled
        if (options.UseComplexityWeighting)
        {
            weight *= CalculateComplexityWeight(methodName);
        }

        return Math.Max(0.0, weight);
    }

    /// <summary>
    /// Determines if a method is from framework/library code based on its name.
    /// </summary>
    private bool IsFrameworkMethod(string methodName)
    {
        var frameworkPatterns = new[]
        {
            "System.",
            "Microsoft.",
            "NUnit.",
            "Xunit.",
            "Moq.",
            "AutoFixture.",
            "FluentAssertions.",
            "Newtonsoft.",
            "Castle."
        };

        return frameworkPatterns.Any(pattern => methodName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Calculates a complexity weight for a method based on naming heuristics.
    /// </summary>
    private double CalculateComplexityWeight(string methodName)
    {
        var weight = 1.0;

        // Generic methods might be more complex
        if (methodName.Contains("<") && methodName.Contains(">"))
        {
            weight *= 1.2;
        }

        // Constructor methods are typically important
        if (methodName.Contains(".ctor") || methodName.Contains(".cctor"))
        {
            weight *= 1.1;
        }

        // Property accessors are typically simpler
        if (methodName.Contains("get_") || methodName.Contains("set_"))
        {
            weight *= 0.7;
        }

        // Async methods might be more complex
        if (methodName.Contains("Async") || methodName.Contains("async"))
        {
            weight *= 1.15;
        }

        return Math.Max(0.1, Math.Min(2.0, weight));
    }

    /// <summary>
    /// Extracts the container name (namespace or class) from a method name.
    /// </summary>
    private string? ExtractContainerName(string methodName)
    {
        var lastDotIndex = methodName.LastIndexOf('.');
        if (lastDotIndex > 0)
        {
            var containerPart = methodName.Substring(0, lastDotIndex);
            
            // Try to extract namespace (everything before the last type name)
            var parts = containerPart.Split('.');
            if (parts.Length > 1)
            {
                // Return the namespace part (all but the last part which is likely the class name)
                return string.Join(".", parts.Take(parts.Length - 1));
            }
            
            return containerPart;
        }

        return null;
    }
}