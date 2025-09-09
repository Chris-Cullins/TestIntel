using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TestIntelligence.TestComparison.Services;

/// <summary>
/// Service for validating test method identifiers and providing early feedback
/// to avoid expensive operations on invalid test methods.
/// </summary>
public interface ITestValidationService
{
    /// <summary>
    /// Validates a single test method identifier exists and is a valid test method.
    /// </summary>
    /// <param name="testMethodId">Full identifier of the test method</param>
    /// <param name="solutionPath">Path to solution file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with details about the test method</returns>
    Task<TestValidationResult> ValidateTestAsync(
        string testMethodId,
        string solutionPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates multiple test method identifiers in a batch operation.
    /// </summary>
    /// <param name="testMethodIds">Collection of test method identifiers to validate</param>
    /// <param name="solutionPath">Path to solution file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Batch validation results</returns>
    Task<BatchTestValidationResult> ValidateTestsAsync(
        IEnumerable<string> testMethodIds,
        string solutionPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers all available test methods in a solution for fuzzy matching suggestions.
    /// Uses cached results when available to avoid expensive re-discovery.
    /// </summary>
    /// <param name="solutionPath">Path to solution file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of available test method identifiers</returns>
    Task<IReadOnlyList<string>> DiscoverAvailableTestsAsync(
        string solutionPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Suggests similar test method names using fuzzy matching when validation fails.
    /// </summary>
    /// <param name="invalidTestMethodId">The invalid test method identifier</param>
    /// <param name="solutionPath">Path to solution file</param>
    /// <param name="maxSuggestions">Maximum number of suggestions to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of suggested test method identifiers</returns>
    Task<IReadOnlyList<string>> SuggestSimilarTestsAsync(
        string invalidTestMethodId,
        string solutionPath,
        int maxSuggestions = 5,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of validating a single test method.
/// </summary>
public record TestValidationResult
{
    /// <summary>
    /// The test method identifier that was validated.
    /// </summary>
    public required string TestMethodId { get; init; }

    /// <summary>
    /// Whether the test method is valid and exists.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Error message if validation failed, null if valid.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Additional metadata about the test method if found.
    /// </summary>
    public TestMethodMetadata? Metadata { get; init; }

    /// <summary>
    /// Suggested alternatives if the test method was not found.
    /// </summary>
    public IReadOnlyList<string>? Suggestions { get; init; }

    /// <summary>
    /// Time taken to validate this test method.
    /// </summary>
    public TimeSpan ValidationDuration { get; init; }
}

/// <summary>
/// Result of validating multiple test methods in a batch.
/// </summary>
public record BatchTestValidationResult
{
    /// <summary>
    /// Individual validation results for each test method.
    /// </summary>
    public required IReadOnlyList<TestValidationResult> Results { get; init; }

    /// <summary>
    /// Test methods that passed validation.
    /// </summary>
    public IReadOnlyList<string> ValidTests => 
        Results.Where(r => r.IsValid).Select(r => r.TestMethodId).ToList();

    /// <summary>
    /// Test methods that failed validation.
    /// </summary>
    public IReadOnlyList<TestValidationResult> InvalidTests => 
        Results.Where(r => !r.IsValid).ToList();

    /// <summary>
    /// Whether all test methods in the batch are valid.
    /// </summary>
    public bool AllValid => Results.All(r => r.IsValid);

    /// <summary>
    /// Total time taken for batch validation.
    /// </summary>
    public TimeSpan TotalValidationDuration { get; init; }
}

/// <summary>
/// Metadata information about a validated test method.
/// </summary>
public record TestMethodMetadata
{
    /// <summary>
    /// Full type name containing the test method.
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    /// Method name without the type prefix.
    /// </summary>
    public required string MethodName { get; init; }

    /// <summary>
    /// Assembly where the test method is located.
    /// </summary>
    public required string AssemblyName { get; init; }

    /// <summary>
    /// Test framework used (NUnit, xUnit, MSTest, etc.).
    /// </summary>
    public string? TestFramework { get; init; }

    /// <summary>
    /// Test categories or traits associated with the method.
    /// </summary>
    public IReadOnlyList<string>? Categories { get; init; }

    /// <summary>
    /// Whether this is a parameterized test (TestCase, Theory, etc.).
    /// </summary>
    public bool IsParameterized { get; init; }
}