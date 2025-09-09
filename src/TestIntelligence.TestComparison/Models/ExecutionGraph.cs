using System;
using System.Collections.Generic;
using System.Linq;

namespace TestIntelligence.TestComparison.Models;

/// <summary>
/// Represents an execution path as a graph structure for similarity analysis.
/// </summary>
public class ExecutionGraph
{
    /// <summary>
    /// Set of unique method nodes in the execution graph.
    /// </summary>
    public IReadOnlySet<string> Nodes { get; init; } = new HashSet<string>();

    /// <summary>
    /// Set of execution edges representing method call relationships.
    /// </summary>
    public IReadOnlySet<ExecutionEdge> Edges { get; init; } = new HashSet<ExecutionEdge>();

    /// <summary>
    /// Mapping of method nodes to their call depths.
    /// </summary>
    public IReadOnlyDictionary<string, int> NodeDepths { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Frequency of calls for each method node.
    /// </summary>
    public IReadOnlyDictionary<string, int> CallFrequencies { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Calculates the average call depth across all nodes.
    /// </summary>
    /// <returns>Average depth value</returns>
    public double CalculateAverageDepth() => NodeDepths.Values.DefaultIfEmpty(0).Average();

    /// <summary>
    /// Calculates the branching factor of the execution graph.
    /// </summary>
    /// <returns>Branching factor (edges per node ratio)</returns>
    public double CalculateBranchingFactor() => Edges.Count / (double)Math.Max(1, Nodes.Count - 1);

    /// <summary>
    /// Gets the maximum call depth in the execution graph.
    /// </summary>
    /// <returns>Maximum depth value</returns>
    public int GetMaxDepth() => NodeDepths.Values.DefaultIfEmpty(0).Max();

    /// <summary>
    /// Gets all nodes at a specific call depth.
    /// </summary>
    /// <param name="depth">The call depth to filter by</param>
    /// <returns>Collection of method names at the specified depth</returns>
    public IEnumerable<string> GetNodesAtDepth(int depth)
    {
        return NodeDepths.Where(kvp => kvp.Value == depth).Select(kvp => kvp.Key);
    }

    /// <summary>
    /// Gets all outgoing edges from a specific method node.
    /// </summary>
    /// <param name="methodName">Source method name</param>
    /// <returns>Collection of outgoing edges</returns>
    public IEnumerable<ExecutionEdge> GetOutgoingEdges(string methodName)
    {
        return Edges.Where(edge => edge.FromMethod == methodName);
    }

    /// <summary>
    /// Gets all incoming edges to a specific method node.
    /// </summary>
    /// <param name="methodName">Target method name</param>
    /// <returns>Collection of incoming edges</returns>
    public IEnumerable<ExecutionEdge> GetIncomingEdges(string methodName)
    {
        return Edges.Where(edge => edge.ToMethod == methodName);
    }
}

/// <summary>
/// Represents a directed edge in the execution graph showing a method call relationship.
/// </summary>
public record ExecutionEdge(
    string FromMethod, 
    string ToMethod, 
    int CallDepth, 
    double Weight)
{
    /// <summary>
    /// Creates an execution edge with default weight.
    /// </summary>
    /// <param name="fromMethod">Source method</param>
    /// <param name="toMethod">Target method</param>
    /// <param name="callDepth">Depth of the call</param>
    public ExecutionEdge(string fromMethod, string toMethod, int callDepth) 
        : this(fromMethod, toMethod, callDepth, 1.0)
    {
    }
}