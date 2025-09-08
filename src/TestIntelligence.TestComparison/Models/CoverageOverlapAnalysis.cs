using System.Collections.Generic;

namespace TestIntelligence.TestComparison.Models;

/// <summary>
/// Analysis of production method coverage overlap between two tests.
/// </summary>
public class CoverageOverlapAnalysis
{
    /// <summary>
    /// Gets or sets the number of production methods covered by both tests.
    /// </summary>
    public int SharedProductionMethods { get; init; }

    /// <summary>
    /// Gets or sets the number of production methods covered only by the first test.
    /// </summary>
    public int UniqueToTest1 { get; init; }

    /// <summary>
    /// Gets or sets the number of production methods covered only by the second test.
    /// </summary>
    public int UniqueToTest2 { get; init; }

    /// <summary>
    /// Gets or sets the overlap percentage using Jaccard similarity (0.0 to 100.0).
    /// Calculated as: shared / (shared + unique1 + unique2) * 100
    /// </summary>
    public double OverlapPercentage { get; init; }

    /// <summary>
    /// Gets or sets detailed information about methods covered by both tests.
    /// </summary>
    public required IReadOnlyList<SharedMethodInfo> SharedMethods { get; init; }

    /// <summary>
    /// Gets or sets methods covered only by the first test.
    /// </summary>
    public required IReadOnlyList<string> UniqueMethodsTest1 { get; init; }

    /// <summary>
    /// Gets or sets methods covered only by the second test.
    /// </summary>
    public required IReadOnlyList<string> UniqueMethodsTest2 { get; init; }

    /// <summary>
    /// Gets the total number of unique methods covered by either test.
    /// </summary>
    public int TotalUniqueMethods => SharedProductionMethods + UniqueToTest1 + UniqueToTest2;
}

/// <summary>
/// Represents information about a production method that is covered by both tests.
/// </summary>
public class SharedMethodInfo
{
    /// <summary>
    /// Gets or sets the full name of the shared method.
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// Gets or sets the confidence score for this shared coverage (0.0 to 1.0).
    /// Higher values indicate more reliable coverage detection.
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Gets or sets the call depth at which this method is reached from the test methods.
    /// Lower depths indicate more direct calls.
    /// </summary>
    public int CallDepth { get; init; }

    /// <summary>
    /// Gets or sets the weight assigned to this method in similarity calculations.
    /// Accounts for factors like call depth, complexity, and method importance.
    /// </summary>
    public double Weight { get; init; }

    /// <summary>
    /// Gets or sets whether this method is considered production code (vs framework/library code).
    /// </summary>
    public bool IsProductionCode { get; init; } = true;

    /// <summary>
    /// Gets or sets the namespace or assembly containing this method, if available.
    /// </summary>
    public string? ContainerName { get; init; }
}