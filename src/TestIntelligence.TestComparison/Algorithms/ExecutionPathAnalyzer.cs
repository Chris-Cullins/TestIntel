using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Models;
using TestIntelligence.TestComparison.Models;

namespace TestIntelligence.TestComparison.Algorithms;

/// <summary>
/// Analyzes execution path similarity between test methods using graph-based algorithms.
/// </summary>
public class ExecutionPathAnalyzer
{
    private readonly ILogger<ExecutionPathAnalyzer> _logger;

    public ExecutionPathAnalyzer(ILogger<ExecutionPathAnalyzer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Calculates comprehensive execution path similarity between two traces.
    /// </summary>
    public ExecutionPathSimilarity CalculateExecutionPathSimilarity(
        ExecutionTrace trace1,
        ExecutionTrace trace2,
        PathComparisonOptions? options = null)
    {
        if (trace1 == null) throw new ArgumentNullException(nameof(trace1));
        if (trace2 == null) throw new ArgumentNullException(nameof(trace2));

        options ??= new PathComparisonOptions();

        _logger.LogDebug("Calculating execution path similarity between {Test1} and {Test2}", 
            trace1.TestMethodId, trace2.TestMethodId);

        var graph1 = BuildExecutionGraph(trace1, options);
        var graph2 = BuildExecutionGraph(trace2, options);

        var structuralSimilarity = options.IncludeStructuralSimilarity 
            ? CalculateStructuralSimilarity(graph1, graph2, options) 
            : 0.0;

        var sequentialSimilarity = options.IncludeSequentialSimilarity 
            ? CalculateSequentialSimilarity(trace1, trace2, options) 
            : 0.0;

        var jaccardSimilarity = CalculateJaccardSimilarity(graph1.Nodes, graph2.Nodes);
        var cosineSimilarity = CalculateCosineSimilarity(graph1, graph2);
        var divergencePoints = FindDivergencePoints(trace1, trace2, options);

        return new ExecutionPathSimilarity
        {
            JaccardSimilarity = jaccardSimilarity,
            CosineSimilarity = cosineSimilarity,
            SharedExecutionNodes = graph1.Nodes.Intersect(graph2.Nodes).Count(),
            TotalUniqueNodes = graph1.Nodes.Union(graph2.Nodes).Count(),
            DivergencePoints = divergencePoints,
            StructuralSimilarity = structuralSimilarity,
            SequentialSimilarity = sequentialSimilarity
        };
    }

    /// <summary>
    /// Builds an execution graph from an execution trace.
    /// </summary>
    public ExecutionGraph BuildExecutionGraph(ExecutionTrace trace, PathComparisonOptions options)
    {
        var relevantMethods = GetRelevantMethods(trace, options);
        var nodes = new HashSet<string>(relevantMethods.Select(m => m.MethodId));
        var edges = new HashSet<ExecutionEdge>();
        var nodeDepths = new Dictionary<string, int>();
        var callFrequencies = new Dictionary<string, int>();

        // Build call frequency map
        foreach (var method in relevantMethods)
        {
            callFrequencies[method.MethodId] = callFrequencies.GetValueOrDefault(method.MethodId, 0) + 1;
            nodeDepths[method.MethodId] = Math.Min(nodeDepths.GetValueOrDefault(method.MethodId, int.MaxValue), method.CallDepth);
        }

        // Build edges from call path relationships
        for (int i = 0; i < relevantMethods.Count - 1; i++)
        {
            var currentMethod = relevantMethods[i];
            var nextMethod = relevantMethods[i + 1];

            // Create edge if next method is called from current method (depth increases)
            if (nextMethod.CallDepth > currentMethod.CallDepth || 
                (nextMethod.CallDepth == currentMethod.CallDepth && IsSequentialCall(currentMethod, nextMethod)))
            {
                var weight = CalculateEdgeWeight(currentMethod, nextMethod, callFrequencies);
                edges.Add(new ExecutionEdge(currentMethod.MethodId, nextMethod.MethodId, nextMethod.CallDepth, weight));
            }
        }

        return new ExecutionGraph
        {
            Nodes = nodes,
            Edges = edges,
            NodeDepths = nodeDepths,
            CallFrequencies = callFrequencies
        };
    }

    /// <summary>
    /// Calculates structural similarity based on graph topology.
    /// </summary>
    public double CalculateStructuralSimilarity(
        ExecutionGraph graph1,
        ExecutionGraph graph2,
        PathComparisonOptions options)
    {
        // Node overlap (Jaccard on method sets)
        var nodeOverlap = CalculateJaccardSimilarity(graph1.Nodes, graph2.Nodes);

        // Edge overlap (Jaccard on call relationships)
        var edgeOverlap = CalculateEdgeOverlap(graph1.Edges, graph2.Edges);

        // Graph structure metrics
        var depthSimilarity = CalculateDepthSimilarity(graph1, graph2);
        var branchingSimilarity = CalculateBranchingSimilarity(graph1, graph2);

        // Combine structural metrics with weights
        return (nodeOverlap * 0.4 + edgeOverlap * 0.3 + depthSimilarity * 0.2 + branchingSimilarity * 0.1);
    }

    /// <summary>
    /// Calculates sequential similarity based on method call order.
    /// </summary>
    public double CalculateSequentialSimilarity(
        ExecutionTrace trace1,
        ExecutionTrace trace2,
        PathComparisonOptions options)
    {
        var methods1 = GetRelevantMethods(trace1, options).Select(m => m.MethodId).ToList();
        var methods2 = GetRelevantMethods(trace2, options).Select(m => m.MethodId).ToList();

        // Calculate Longest Common Subsequence (LCS) similarity
        var lcsSimilarity = CalculateLcsSimilarity(methods1, methods2);

        // Calculate edit distance similarity
        var editDistanceSimilarity = CalculateEditDistanceSimilarity(methods1, methods2);

        // Calculate n-gram similarity for call patterns
        var ngramSimilarity = CalculateNgramSimilarity(methods1, methods2, 3);

        // Combine sequential metrics
        return (lcsSimilarity * 0.5 + editDistanceSimilarity * 0.3 + ngramSimilarity * 0.2);
    }

    /// <summary>
    /// Identifies points where execution paths diverge.
    /// </summary>
    public IReadOnlyList<PathDivergencePoint> FindDivergencePoints(
        ExecutionTrace trace1,
        ExecutionTrace trace2,
        PathComparisonOptions options)
    {
        var divergencePoints = new List<PathDivergencePoint>();
        var methods1 = GetRelevantMethods(trace1, options);
        var methods2 = GetRelevantMethods(trace2, options);

        var commonPrefix = FindCommonPrefix(
            methods1.Select(m => m.MethodId).ToList(),
            methods2.Select(m => m.MethodId).ToList());

        if (commonPrefix.Length < Math.Min(methods1.Count, methods2.Count))
        {
            var divergenceMethod = commonPrefix.Length < methods1.Count 
                ? methods1[commonPrefix.Length].MethodId 
                : "EndOfExecution";

            divergencePoints.Add(new PathDivergencePoint
            {
                Method = divergenceMethod,
                Branch = "PathDivergence",
                DivergenceType = "sequential",
                Depth = commonPrefix.Length,
                Description = $"Execution paths diverge after {commonPrefix.Length} common method calls"
            });
        }

        // Find conditional and loop divergences
        FindConditionalDivergences(methods1, methods2, divergencePoints, options);

        return divergencePoints.Take(options.MaxDivergencePoints).ToList();
    }

    #region Private Helper Methods

    private List<ExecutedMethod> GetRelevantMethods(ExecutionTrace trace, PathComparisonOptions options)
    {
        var methods = trace.ExecutedMethods.ToList();

        if (options.IgnoreFrameworkCalls)
        {
            methods = methods.Where(m => m.Category != MethodCategory.Framework && 
                                        m.Category != MethodCategory.ThirdParty).ToList();
        }

        if (options.MaxAnalysisDepth > 0)
        {
            methods = methods.Where(m => m.CallDepth <= options.MaxAnalysisDepth).ToList();
        }

        return methods;
    }

    private bool IsSequentialCall(ExecutedMethod current, ExecutedMethod next)
    {
        // Simple heuristic: next method is likely called from current if they have similar call paths
        return next.CallPath.Length > current.CallPath.Length && 
               current.CallPath.All(path => next.CallPath.Contains(path));
    }

    private double CalculateEdgeWeight(ExecutedMethod from, ExecutedMethod to, Dictionary<string, int> frequencies)
    {
        var fromFreq = frequencies.GetValueOrDefault(from.MethodId, 1);
        var toFreq = frequencies.GetValueOrDefault(to.MethodId, 1);
        return Math.Log(fromFreq + toFreq + 1); // Logarithmic weighting to avoid extreme values
    }

    private double CalculateJaccardSimilarity(IReadOnlySet<string> set1, IReadOnlySet<string> set2)
    {
        if (!set1.Any() && !set2.Any()) return 1.0;
        if (!set1.Any() || !set2.Any()) return 0.0;

        var intersection = set1.Intersect(set2).Count();
        var union = set1.Union(set2).Count();
        return (double)intersection / union;
    }

    private double CalculateCosineSimilarity(ExecutionGraph graph1, ExecutionGraph graph2)
    {
        var allNodes = graph1.Nodes.Union(graph2.Nodes).ToList();
        if (!allNodes.Any()) return 1.0;

        var vector1 = allNodes.Select(node => graph1.CallFrequencies.GetValueOrDefault(node, 0)).ToArray();
        var vector2 = allNodes.Select(node => graph2.CallFrequencies.GetValueOrDefault(node, 0)).ToArray();

        var dotProduct = vector1.Zip(vector2, (a, b) => a * b).Sum();
        var magnitude1 = Math.Sqrt(vector1.Sum(x => x * x));
        var magnitude2 = Math.Sqrt(vector2.Sum(x => x * x));

        if (magnitude1 == 0 || magnitude2 == 0) return 0.0;
        return dotProduct / (magnitude1 * magnitude2);
    }

    private double CalculateEdgeOverlap(IReadOnlySet<ExecutionEdge> edges1, IReadOnlySet<ExecutionEdge> edges2)
    {
        if (!edges1.Any() && !edges2.Any()) return 1.0;
        if (!edges1.Any() || !edges2.Any()) return 0.0;

        // Compare edges by from-to relationship, ignoring weights and depths
        var normalizedEdges1 = edges1.Select(e => (e.FromMethod, e.ToMethod)).ToHashSet();
        var normalizedEdges2 = edges2.Select(e => (e.FromMethod, e.ToMethod)).ToHashSet();

        var intersection = normalizedEdges1.Intersect(normalizedEdges2).Count();
        var union = normalizedEdges1.Union(normalizedEdges2).Count();
        return (double)intersection / union;
    }

    private double CalculateDepthSimilarity(ExecutionGraph graph1, ExecutionGraph graph2)
    {
        var avgDepth1 = graph1.CalculateAverageDepth();
        var avgDepth2 = graph2.CalculateAverageDepth();
        var maxDepth1 = graph1.GetMaxDepth();
        var maxDepth2 = graph2.GetMaxDepth();

        if (avgDepth1 == 0 && avgDepth2 == 0) return 1.0;

        var avgSimilarity = 1.0 - Math.Abs(avgDepth1 - avgDepth2) / Math.Max(avgDepth1, avgDepth2);
        var maxSimilarity = 1.0 - Math.Abs(maxDepth1 - maxDepth2) / Math.Max(maxDepth1, maxDepth2);

        return (avgSimilarity + maxSimilarity) / 2.0;
    }

    private double CalculateBranchingSimilarity(ExecutionGraph graph1, ExecutionGraph graph2)
    {
        var branching1 = graph1.CalculateBranchingFactor();
        var branching2 = graph2.CalculateBranchingFactor();

        if (branching1 == 0 && branching2 == 0) return 1.0;
        if (branching1 == 0 || branching2 == 0) return 0.0;

        return 1.0 - Math.Abs(branching1 - branching2) / Math.Max(branching1, branching2);
    }

    private double CalculateLcsSimilarity(List<string> sequence1, List<string> sequence2)
    {
        var lcsLength = LongestCommonSubsequence(sequence1, sequence2);
        var maxLength = Math.Max(sequence1.Count, sequence2.Count);
        return maxLength > 0 ? (double)lcsLength / maxLength : 1.0;
    }

    private int LongestCommonSubsequence(List<string> seq1, List<string> seq2)
    {
        if (!seq1.Any() || !seq2.Any()) return 0;

        var dp = new int[seq1.Count + 1, seq2.Count + 1];

        for (int i = 1; i <= seq1.Count; i++)
        {
            for (int j = 1; j <= seq2.Count; j++)
            {
                if (seq1[i - 1] == seq2[j - 1])
                {
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                }
                else
                {
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }
            }
        }

        return dp[seq1.Count, seq2.Count];
    }

    private double CalculateEditDistanceSimilarity(List<string> sequence1, List<string> sequence2)
    {
        var editDistance = LevenshteinDistance(sequence1, sequence2);
        var maxLength = Math.Max(sequence1.Count, sequence2.Count);
        return maxLength > 0 ? 1.0 - (double)editDistance / maxLength : 1.0;
    }

    private int LevenshteinDistance(List<string> seq1, List<string> seq2)
    {
        if (!seq1.Any()) return seq2.Count;
        if (!seq2.Any()) return seq1.Count;

        var dp = new int[seq1.Count + 1, seq2.Count + 1];

        for (int i = 0; i <= seq1.Count; i++) dp[i, 0] = i;
        for (int j = 0; j <= seq2.Count; j++) dp[0, j] = j;

        for (int i = 1; i <= seq1.Count; i++)
        {
            for (int j = 1; j <= seq2.Count; j++)
            {
                var cost = seq1[i - 1] == seq2[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + cost);
            }
        }

        return dp[seq1.Count, seq2.Count];
    }

    private double CalculateNgramSimilarity(List<string> sequence1, List<string> sequence2, int n)
    {
        var ngrams1 = GetNgrams(sequence1, n).ToHashSet();
        var ngrams2 = GetNgrams(sequence2, n).ToHashSet();

        return CalculateJaccardSimilarity(ngrams1, ngrams2);
    }

    private IEnumerable<string> GetNgrams(List<string> sequence, int n)
    {
        for (int i = 0; i <= sequence.Count - n; i++)
        {
            yield return string.Join("->", sequence.Skip(i).Take(n));
        }
    }

    private string[] FindCommonPrefix(List<string> sequence1, List<string> sequence2)
    {
        var commonPrefix = new List<string>();
        var minLength = Math.Min(sequence1.Count, sequence2.Count);

        for (int i = 0; i < minLength; i++)
        {
            if (sequence1[i] == sequence2[i])
            {
                commonPrefix.Add(sequence1[i]);
            }
            else
            {
                break;
            }
        }

        return commonPrefix.ToArray();
    }

    private void FindConditionalDivergences(
        List<ExecutedMethod> methods1,
        List<ExecutedMethod> methods2,
        List<PathDivergencePoint> divergencePoints,
        PathComparisonOptions options)
    {
        // Simple heuristic: look for depth changes that might indicate conditional branches
        var depthChanges1 = GetDepthChanges(methods1);
        var depthChanges2 = GetDepthChanges(methods2);

        foreach (var change1 in depthChanges1)
        {
            var corresponding = depthChanges2.FirstOrDefault(c => c.Method == change1.Method);
            if (corresponding.Method != null && corresponding.DepthChange != change1.DepthChange)
            {
                divergencePoints.Add(new PathDivergencePoint
                {
                    Method = change1.Method,
                    Branch = $"DepthChange_{change1.DepthChange}_vs_{corresponding.DepthChange}",
                    DivergenceType = "conditional",
                    Depth = change1.Position,
                    Description = $"Different execution depth changes detected at {change1.Method}"
                });
            }
        }
    }

    private IEnumerable<(string Method, int DepthChange, int Position)> GetDepthChanges(List<ExecutedMethod> methods)
    {
        for (int i = 1; i < methods.Count; i++)
        {
            var depthChange = methods[i].CallDepth - methods[i - 1].CallDepth;
            if (Math.Abs(depthChange) > 1) // Significant depth change
            {
                yield return (methods[i].MethodId, depthChange, i);
            }
        }
    }

    #endregion
}