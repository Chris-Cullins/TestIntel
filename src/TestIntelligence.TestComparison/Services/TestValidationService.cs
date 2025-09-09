using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Discovery;
using TestIntelligence.Core.Interfaces;
using TestIntelligence.Core.Models;

namespace TestIntelligence.TestComparison.Services;

/// <summary>
/// Service implementation for validating test method identifiers and providing early feedback.
/// Optimized for fast validation to avoid expensive operations on invalid test methods.
/// </summary>
public class TestValidationService : ITestValidationService
{
    private readonly ITestDiscovery _testDiscovery;
    private readonly ILogger<TestValidationService> _logger;
    
    // Cache for discovered tests per solution to avoid re-discovery
    private readonly ConcurrentDictionary<string, CachedTestDiscovery> _discoveryCache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    // Cache entry with expiration
    private record CachedTestDiscovery(
        IReadOnlyList<string> TestIds,
        IReadOnlyDictionary<string, TestMethodMetadata> Metadata,
        DateTime CreatedAt)
    {
        public bool IsExpired => DateTime.UtcNow - CreatedAt > TimeSpan.FromMinutes(5);
    }

    public TestValidationService(
        ITestDiscovery testDiscovery,
        ILogger<TestValidationService> logger)
    {
        _testDiscovery = testDiscovery ?? throw new ArgumentNullException(nameof(testDiscovery));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<TestValidationResult> ValidateTestAsync(
        string testMethodId,
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(testMethodId))
            throw new ArgumentException("Test method ID cannot be null or empty", nameof(testMethodId));
        if (string.IsNullOrEmpty(solutionPath))
            throw new ArgumentException("Solution path cannot be null or empty", nameof(solutionPath));

        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Get or discover available tests
            var cachedDiscovery = await GetOrDiscoverTestsAsync(solutionPath, cancellationToken);
            
            // Check if the test exists
            var isValid = cachedDiscovery.TestIds.Contains(testMethodId, StringComparer.OrdinalIgnoreCase);
            
            if (isValid)
            {
                var metadata = cachedDiscovery.Metadata.GetValueOrDefault(testMethodId);
                
                return new TestValidationResult
                {
                    TestMethodId = testMethodId,
                    IsValid = true,
                    Metadata = metadata,
                    ValidationDuration = stopwatch.Elapsed
                };
            }
            else
            {
                // Generate suggestions for invalid test
                var suggestions = GenerateSuggestions(testMethodId, cachedDiscovery.TestIds);
                
                return new TestValidationResult
                {
                    TestMethodId = testMethodId,
                    IsValid = false,
                    ErrorMessage = $"Test method '{testMethodId}' not found in solution",
                    Suggestions = suggestions,
                    ValidationDuration = stopwatch.Elapsed
                };
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Test validation cancelled for {TestMethodId}", testMethodId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate test method {TestMethodId}", testMethodId);
            
            return new TestValidationResult
            {
                TestMethodId = testMethodId,
                IsValid = false,
                ErrorMessage = $"Validation failed: {ex.Message}",
                ValidationDuration = stopwatch.Elapsed
            };
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    /// <inheritdoc />
    public async Task<BatchTestValidationResult> ValidateTestsAsync(
        IEnumerable<string> testMethodIds,
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        if (testMethodIds == null)
            throw new ArgumentNullException(nameof(testMethodIds));

        var ids = testMethodIds.ToList();
        if (ids.Count == 0)
            throw new ArgumentException("At least one test method ID must be provided", nameof(testMethodIds));

        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Validate all tests concurrently but limit concurrency to avoid overwhelming the system
            var semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
            var validationTasks = ids.Select(async testId =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await ValidateTestAsync(testId, solutionPath, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(validationTasks);

            return new BatchTestValidationResult
            {
                Results = results,
                TotalValidationDuration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Batch test validation cancelled for {TestCount} tests", ids.Count);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate batch of {TestCount} tests", ids.Count);
            throw;
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> DiscoverAvailableTestsAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var cachedDiscovery = await GetOrDiscoverTestsAsync(solutionPath, cancellationToken);
        return cachedDiscovery.TestIds;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> SuggestSimilarTestsAsync(
        string invalidTestMethodId,
        string solutionPath,
        int maxSuggestions = 5,
        CancellationToken cancellationToken = default)
    {
        var availableTests = await DiscoverAvailableTestsAsync(solutionPath, cancellationToken);
        return GenerateSuggestions(invalidTestMethodId, availableTests, maxSuggestions);
    }

    private async Task<CachedTestDiscovery> GetOrDiscoverTestsAsync(
        string solutionPath,
        CancellationToken cancellationToken)
    {
        // Check cache first
        if (_discoveryCache.TryGetValue(solutionPath, out var cached) && !cached.IsExpired)
        {
            _logger.LogDebug("Using cached test discovery for solution: {SolutionPath}", solutionPath);
            return cached;
        }

        // Need to discover tests - use lock to avoid duplicate discovery
        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_discoveryCache.TryGetValue(solutionPath, out cached) && !cached.IsExpired)
            {
                return cached;
            }

            _logger.LogInformation("Discovering tests for validation in solution: {SolutionPath}", solutionPath);
            var stopwatch = Stopwatch.StartNew();

            // Perform fast test discovery
            var discoveryResult = await DiscoverTestsForValidationAsync(solutionPath, cancellationToken);
            
            var newCached = new CachedTestDiscovery(
                discoveryResult.TestIds,
                discoveryResult.Metadata,
                DateTime.UtcNow);

            _discoveryCache[solutionPath] = newCached;

            _logger.LogInformation("Discovered {TestCount} tests for validation in {Duration:F1}s",
                discoveryResult.TestIds.Count, stopwatch.Elapsed.TotalSeconds);

            return newCached;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private Task<(IReadOnlyList<string> TestIds, IReadOnlyDictionary<string, TestMethodMetadata> Metadata)> 
        DiscoverTestsForValidationAsync(string solutionPath, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Starting test discovery for validation in solution: {SolutionPath}", solutionPath);
            
            // TODO: Implement proper test discovery by loading assemblies
            // For now, we need to integrate with the assembly loading infrastructure
            // This is a simplified implementation that provides graceful degradation
            
            _logger.LogWarning("Test discovery for validation is not yet fully implemented - returning empty results");
            _logger.LogInformation("Validation will be skipped, allowing analysis to proceed with its own discovery");
            
            var testIds = new List<string>();
            var metadata = new Dictionary<string, TestMethodMetadata>();

            return Task.FromResult<(IReadOnlyList<string> TestIds, IReadOnlyDictionary<string, TestMethodMetadata> Metadata)>(
                (testIds.AsReadOnly(), (IReadOnlyDictionary<string, TestMethodMetadata>)metadata));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover tests for validation in solution: {SolutionPath}", solutionPath);
            return Task.FromResult<(IReadOnlyList<string> TestIds, IReadOnlyDictionary<string, TestMethodMetadata> Metadata)>(
                (Array.Empty<string>(), new Dictionary<string, TestMethodMetadata>()));
        }
    }

    private static string? DetermineTestFramework(TestMethod testMethod)
    {
        // Use the existing test method properties to determine framework
        if (testMethod.IsTest || testMethod.IsTestCase)
        {
            // Check attributes to determine framework
            var attributeNames = testMethod.TestAttributes.Select(a => a.GetType().Name).ToList();
            
            if (attributeNames.Any(name => name.Contains("NUnit", StringComparison.OrdinalIgnoreCase)))
                return "NUnit";
            if (attributeNames.Any(name => name.Contains("Fact", StringComparison.OrdinalIgnoreCase) || 
                                         name.Contains("Theory", StringComparison.OrdinalIgnoreCase)))
                return "xUnit";
            if (attributeNames.Any(name => name.Contains("TestMethod", StringComparison.OrdinalIgnoreCase)))
                return "MSTest";
        }

        return null;
    }

    private static IReadOnlyList<string>? ExtractCategories(TestMethod testMethod)
    {
        // Use existing test method method to get categories
        var categories = testMethod.GetCategories().ToList();
        return categories.Count > 0 ? categories : null;
    }

    private static IReadOnlyList<string> GenerateSuggestions(
        string invalidTestId, 
        IReadOnlyList<string> availableTests, 
        int maxSuggestions = 5)
    {
        if (availableTests.Count == 0)
            return Array.Empty<string>();

        // Use simple Levenshtein distance for fuzzy matching
        var suggestions = availableTests
            .Select(test => new { Test = test, Distance = LevenshteinDistance(invalidTestId, test) })
            .Where(item => item.Distance <= Math.Max(3, invalidTestId.Length / 3)) // Allow some tolerance
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Test.Length) // Prefer shorter matches
            .Take(maxSuggestions)
            .Select(item => item.Test)
            .ToList();

        return suggestions;
    }

    private static int LevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
            return string.IsNullOrEmpty(target) ? 0 : target.Length;
        if (string.IsNullOrEmpty(target))
            return source.Length;

        var sourceLength = source.Length;
        var targetLength = target.Length;
        var matrix = new int[sourceLength + 1, targetLength + 1];

        // Initialize first column and row
        for (var i = 0; i <= sourceLength; matrix[i, 0] = i++) { }
        for (var j = 0; j <= targetLength; matrix[0, j] = j++) { }

        // Fill the matrix
        for (var i = 1; i <= sourceLength; i++)
        {
            for (var j = 1; j <= targetLength; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[sourceLength, targetLength];
    }
}