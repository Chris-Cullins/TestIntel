using System.Collections.Generic;

namespace TestIntelligence.TestComparison.Models;

/// <summary>
/// Represents similarity metrics based on test metadata and characteristics.
/// </summary>
public class MetadataSimilarity
{
    /// <summary>
    /// Gets or sets the overall metadata similarity score (0.0 to 1.0).
    /// </summary>
    public double OverallScore { get; init; }

    /// <summary>
    /// Gets or sets the category alignment score (0.0 to 1.0).
    /// Higher scores indicate tests belong to similar categories.
    /// </summary>
    public double CategoryAlignmentScore { get; init; }

    /// <summary>
    /// Gets or sets the naming pattern similarity score (0.0 to 1.0).
    /// Based on common prefixes, suffixes, and naming conventions.
    /// </summary>
    public double NamingPatternScore { get; init; }

    /// <summary>
    /// Gets or sets the tag overlap score (0.0 to 1.0).
    /// Based on shared tags between the test methods.
    /// </summary>
    public double TagOverlapScore { get; init; }

    /// <summary>
    /// Gets or sets tags that are common between both tests.
    /// </summary>
    public required IReadOnlyList<string> SharedTags { get; init; }

    /// <summary>
    /// Gets or sets tags unique to the first test.
    /// </summary>
    public required IReadOnlyList<string> UniqueToTest1 { get; init; }

    /// <summary>
    /// Gets or sets tags unique to the second test.
    /// </summary>
    public required IReadOnlyList<string> UniqueToTest2 { get; init; }

    /// <summary>
    /// Gets or sets the execution time similarity score (0.0 to 1.0).
    /// Higher scores indicate tests have similar execution times.
    /// </summary>
    public double ExecutionTimeSimilarity { get; init; }

    /// <summary>
    /// Gets or sets additional metadata factors that contributed to the similarity calculation.
    /// </summary>
    public IReadOnlyDictionary<string, double>? AdditionalFactors { get; init; }
}