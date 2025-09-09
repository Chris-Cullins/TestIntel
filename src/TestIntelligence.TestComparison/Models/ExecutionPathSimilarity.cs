using System.Collections.Generic;

namespace TestIntelligence.TestComparison.Models;

/// <summary>
/// Results of execution path similarity analysis between two tests.
/// </summary>
public class ExecutionPathSimilarity
{
    /// <summary>
    /// Jaccard similarity coefficient between the sets of executed methods.
    /// </summary>
    public double JaccardSimilarity { get; init; }

    /// <summary>
    /// Cosine similarity of execution path vectors.
    /// </summary>
    public double CosineSimilarity { get; init; }

    /// <summary>
    /// Number of methods executed by both tests.
    /// </summary>
    public int SharedExecutionNodes { get; init; }

    /// <summary>
    /// Total number of unique methods executed across both tests.
    /// </summary>
    public int TotalUniqueNodes { get; init; }

    /// <summary>
    /// Points where the execution paths diverge between the two tests.
    /// </summary>
    public required IReadOnlyList<PathDivergencePoint> DivergencePoints { get; init; }

    /// <summary>
    /// Similarity based on graph structure topology (node relationships).
    /// </summary>
    public double StructuralSimilarity { get; init; }

    /// <summary>
    /// Similarity based on sequential order of method calls.
    /// </summary>
    public double SequentialSimilarity { get; init; }

    /// <summary>
    /// Combined overall execution path similarity score.
    /// </summary>
    public double OverallPathSimilarity => (StructuralSimilarity + SequentialSimilarity) / 2.0;
}

/// <summary>
/// Represents a point where two execution paths diverge.
/// </summary>
public class PathDivergencePoint
{
    /// <summary>
    /// Method name where the divergence occurs.
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// Branch or path taken that differs between tests.
    /// </summary>
    public required string Branch { get; init; }

    /// <summary>
    /// Type of divergence (conditional, loop, exception, etc.).
    /// </summary>
    public required string DivergenceType { get; init; } // "conditional", "loop", "exception"

    /// <summary>
    /// Call depth at which the divergence occurs.
    /// </summary>
    public int Depth { get; init; }

    /// <summary>
    /// Description of the divergence for reporting purposes.
    /// </summary>
    public string Description { get; init; } = string.Empty;
}