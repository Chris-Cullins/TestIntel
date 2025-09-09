using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Discovery;
using TestIntelligence.Core.Interfaces;
using TestIntelligence.Core.Models;
using TestIntelligence.Core.Services;

namespace TestIntelligence.TestComparison.Services;

/// <summary>
/// Service implementation for validating test method identifiers and providing early feedback.
/// Optimized for fast validation to avoid expensive operations on invalid test methods.
/// </summary>
public class TestValidationService : ITestValidationService
{
    private readonly ITestDiscovery _testDiscovery;
    private readonly ILogger<TestValidationService> _logger;
    private readonly IAssemblyPathResolver? _assemblyPathResolver;
    
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
        ILogger<TestValidationService> logger,
        IAssemblyPathResolver? assemblyPathResolver = null)
    {
        _testDiscovery = testDiscovery ?? throw new ArgumentNullException(nameof(testDiscovery));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _assemblyPathResolver = assemblyPathResolver;
    }

    /// <inheritdoc />
    public async Task<TestValidationResult> ValidateTestAsync(
        string testMethodId,
        string solutionOrDirectoryPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(testMethodId))
            throw new ArgumentException("Test method ID cannot be null or empty", nameof(testMethodId));
        if (string.IsNullOrEmpty(solutionOrDirectoryPath))
            throw new ArgumentException("Solution or directory path cannot be null or empty", nameof(solutionOrDirectoryPath));

        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Get or discover available tests
            var cachedDiscovery = await GetOrDiscoverTestsAsync(solutionOrDirectoryPath, cancellationToken);
            
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
                    ErrorMessage = $"Test method '{testMethodId}' not found in solution or directory",
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
        string solutionOrDirectoryPath,
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
                    return await ValidateTestAsync(testId, solutionOrDirectoryPath, cancellationToken);
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
        string solutionOrDirectoryPath,
        CancellationToken cancellationToken = default)
    {
        var cachedDiscovery = await GetOrDiscoverTestsAsync(solutionOrDirectoryPath, cancellationToken);
        return cachedDiscovery.TestIds;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> SuggestSimilarTestsAsync(
        string invalidTestMethodId,
        string solutionOrDirectoryPath,
        int maxSuggestions = 5,
        CancellationToken cancellationToken = default)
    {
        var availableTests = await DiscoverAvailableTestsAsync(solutionOrDirectoryPath, cancellationToken);
        return GenerateSuggestions(invalidTestMethodId, availableTests, maxSuggestions);
    }

    private async Task<CachedTestDiscovery> GetOrDiscoverTestsAsync(
        string solutionOrDirectoryPath,
        CancellationToken cancellationToken)
    {
        // Check cache first
        if (_discoveryCache.TryGetValue(solutionOrDirectoryPath, out var cached) && !cached.IsExpired)
        {
            _logger.LogDebug("Using cached test discovery for path: {Path}", solutionOrDirectoryPath);
            return cached;
        }

        // Need to discover tests - use lock to avoid duplicate discovery
        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_discoveryCache.TryGetValue(solutionOrDirectoryPath, out cached) && !cached.IsExpired)
            {
                return cached;
            }

            _logger.LogInformation("Discovering tests for validation in path: {Path}", solutionOrDirectoryPath);
            var stopwatch = Stopwatch.StartNew();

            // Perform fast test discovery
            var discoveryResult = await DiscoverTestsForValidationAsync(solutionOrDirectoryPath, cancellationToken);
            
            var newCached = new CachedTestDiscovery(
                discoveryResult.TestIds,
                discoveryResult.Metadata,
                DateTime.UtcNow);

            _discoveryCache[solutionOrDirectoryPath] = newCached;

            _logger.LogInformation("Discovered {TestCount} tests for validation in {Duration:F1}s",
                discoveryResult.TestIds.Count, stopwatch.Elapsed.TotalSeconds);

            return newCached;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task<(IReadOnlyList<string> TestIds, IReadOnlyDictionary<string, TestMethodMetadata> Metadata)> 
        DiscoverTestsForValidationAsync(string solutionOrDirectoryPath, CancellationToken cancellationToken)
    {
        var testIds = new List<string>();
        var metadata = new Dictionary<string, TestMethodMetadata>();

        try
        {
            _logger.LogDebug("Starting test discovery for validation in path: {Path}", solutionOrDirectoryPath);
            
            // Find test assemblies in the solution or directory
            var assemblyPaths = await FindTestAssembliesAsync(solutionOrDirectoryPath);
            
            if (assemblyPaths.Count == 0)
            {
                _logger.LogWarning("No test assemblies found in path: {Path}", solutionOrDirectoryPath);
                return (testIds.AsReadOnly(), metadata);
            }

            _logger.LogInformation("Found {AssemblyCount} test assemblies for validation", assemblyPaths.Count);

            // Load assemblies and discover tests
            using var loader = new CrossFrameworkAssemblyLoader();
            
            foreach (var assemblyPath in assemblyPaths)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var loadResult = await loader.LoadAssemblyAsync(assemblyPath);
                    if (!loadResult.IsSuccess)
                    {
                        _logger.LogWarning("Failed to load assembly {AssemblyPath}: {Errors}", 
                            assemblyPath, string.Join(", ", loadResult.Errors));
                        continue;
                    }

                    var discoveryResult = await _testDiscovery.DiscoverTestsAsync(loadResult.Assembly!, cancellationToken);
                    
                    if (!discoveryResult.IsSuccessful)
                    {
                        _logger.LogWarning("Test discovery failed for {AssemblyPath}: {Errors}", 
                            assemblyPath, string.Join(", ", discoveryResult.Errors));
                        continue;
                    }

                    // Extract test IDs and metadata
                    foreach (var fixture in discoveryResult.TestFixtures)
                    {
                        foreach (var testMethod in fixture.GetExecutableTests())
                        {
                            var testId = testMethod.GetUniqueId();
                            testIds.Add(testId);
                            
                            var testMetadata = new TestMethodMetadata
                            {
                                TypeName = testMethod.DeclaringType.FullName ?? testMethod.DeclaringType.Name,
                                MethodName = testMethod.MethodName,
                                AssemblyName = Path.GetFileNameWithoutExtension(assemblyPath),
                                TestFramework = DetermineTestFramework(testMethod),
                                Categories = ExtractCategories(testMethod),
                                IsParameterized = testMethod.IsTestCase
                            };
                            
                            metadata[testId] = testMetadata;
                        }
                    }
                    
                    _logger.LogDebug("Discovered {TestCount} tests in {AssemblyPath}", 
                        discoveryResult.TestMethodCount, assemblyPath);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Test discovery cancelled for {AssemblyPath}", assemblyPath);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to discover tests in assembly {AssemblyPath}", assemblyPath);
                }
            }

            _logger.LogInformation("Completed test discovery for validation: {TestCount} tests found", testIds.Count);
            return (testIds.AsReadOnly(), metadata);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Test discovery for validation was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover tests for validation in path: {Path}", solutionOrDirectoryPath);
            return (testIds.AsReadOnly(), metadata);
        }
    }

    private async Task<IReadOnlyList<string>> FindTestAssembliesAsync(string solutionOrDirectoryPath)
    {
        try
        {
            // Check if it's a solution file or directory
            var isDirectory = Directory.Exists(solutionOrDirectoryPath);
            var isSolutionFile = File.Exists(solutionOrDirectoryPath) && 
                               (solutionOrDirectoryPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase));

            if (!isDirectory && !isSolutionFile)
            {
                _logger.LogWarning("Path is neither a valid directory nor solution file: {Path}", solutionOrDirectoryPath);
                return Array.Empty<string>();
            }

            // Use assembly path resolver if available and it's a solution file
            if (_assemblyPathResolver != null && isSolutionFile)
            {
                return await _assemblyPathResolver.FindTestAssembliesInSolutionAsync(solutionOrDirectoryPath);
            }

            // Determine search directory
            string searchDir;
            if (isDirectory)
            {
                searchDir = solutionOrDirectoryPath;
                _logger.LogInformation("Searching for test assemblies in directory: {Directory}", searchDir);
            }
            else
            {
                // It's a solution file, use its directory
                searchDir = Path.GetDirectoryName(solutionOrDirectoryPath)!;
                _logger.LogInformation("Searching for test assemblies in solution directory: {Directory}", searchDir);
            }

            if (string.IsNullOrEmpty(searchDir) || !Directory.Exists(searchDir))
            {
                _logger.LogWarning("Search directory not found: {Directory}", searchDir);
                return Array.Empty<string>();
            }

            var testAssemblies = new List<string>();
            
            // First, try to find test assemblies in bin directories (built assemblies)
            var binDirectories = Directory.GetDirectories(searchDir, "bin", SearchOption.AllDirectories)
                .Where(d => !d.Contains("obj", StringComparison.OrdinalIgnoreCase));

            foreach (var binDir in binDirectories)
            {
                var dllFiles = Directory.GetFiles(binDir, "*.dll", SearchOption.AllDirectories)
                    .Where(f => f.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                               f.Contains("spec", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !Path.GetFileName(f).StartsWith("Microsoft.") &&
                               !Path.GetFileName(f).StartsWith("System.") &&
                               !Path.GetFileName(f).StartsWith("NUnit.") &&
                               !Path.GetFileName(f).StartsWith("xunit."));

                testAssemblies.AddRange(dllFiles);
            }

            // If no assemblies found in bin directories, try direct directory search for DLLs
            if (testAssemblies.Count == 0)
            {
                _logger.LogInformation("No test assemblies found in bin directories, searching directly in: {Directory}", searchDir);
                var directDllFiles = Directory.GetFiles(searchDir, "*.dll", SearchOption.AllDirectories)
                    .Where(f => f.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                               f.Contains("spec", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !f.Contains("obj", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !Path.GetFileName(f).StartsWith("Microsoft.") &&
                               !Path.GetFileName(f).StartsWith("System.") &&
                               !Path.GetFileName(f).StartsWith("NUnit.") &&
                               !Path.GetFileName(f).StartsWith("xunit."));

                testAssemblies.AddRange(directDllFiles);
            }

            _logger.LogInformation("Found {AssemblyCount} test assemblies in {Path}", testAssemblies.Count, solutionOrDirectoryPath);
            return testAssemblies.Distinct().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find test assemblies in path: {Path}", solutionOrDirectoryPath);
            return Array.Empty<string>();
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