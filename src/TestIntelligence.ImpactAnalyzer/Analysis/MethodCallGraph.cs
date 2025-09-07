using System;
using System.Collections.Generic;
using System.Linq;
using TestIntelligence.Core.Models;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public class MethodCallGraph
    {
        private readonly Dictionary<string, HashSet<string>> _callGraph;
        private readonly Dictionary<string, MethodInfo> _methodDefinitions;
        private readonly Dictionary<string, HashSet<string>> _reverseGraph;

        public MethodCallGraph(Dictionary<string, HashSet<string>> callGraph, Dictionary<string, MethodInfo> methodDefinitions)
        {
            _callGraph = callGraph ?? throw new ArgumentNullException(nameof(callGraph));
            _methodDefinitions = methodDefinitions ?? throw new ArgumentNullException(nameof(methodDefinitions));
            _reverseGraph = BuildReverseGraph();
        }

        /// <summary>
        /// Gets the internal call graph for advanced operations
        /// </summary>
        public IReadOnlyDictionary<string, HashSet<string>> CallGraph => _callGraph;

        /// <summary>
        /// Gets the method definitions for advanced operations
        /// </summary>
        public IReadOnlyDictionary<string, MethodInfo> MethodDefinitions => _methodDefinitions;

        public IReadOnlyCollection<string> GetMethodCalls(string methodId)
        {
            return _callGraph.TryGetValue(methodId, out var calls) ? calls : new HashSet<string>();
        }

        public IReadOnlyCollection<string> GetMethodDependents(string methodId)
        {
            return _reverseGraph.TryGetValue(methodId, out var dependents) ? dependents : new HashSet<string>();
        }

        public IReadOnlyCollection<string> GetTransitiveDependents(string methodId)
        {
            var visited = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(methodId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!visited.Add(current)) continue;

                foreach (var dependent in GetMethodDependents(current))
                {
                    if (!visited.Contains(dependent))
                        queue.Enqueue(dependent);
                }
            }

            visited.Remove(methodId);
            return visited;
        }

        public MethodInfo? GetMethodInfo(string methodId)
        {
            return _methodDefinitions.TryGetValue(methodId, out var info) ? info : null;
        }

        public IReadOnlyCollection<string> GetAllMethods()
        {
            return _methodDefinitions.Keys;
        }

        public IReadOnlyCollection<string> GetTestMethodsExercisingMethod(string methodId)
        {
            var testMethods = new HashSet<string>();
            var visited = new HashSet<string>();
            var queue = new Queue<string>();

            // Start from the target method and traverse dependents
            queue.Enqueue(methodId);
            visited.Add(methodId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                
                // Check direct dependents
                foreach (var dependent in GetMethodDependents(current))
                {
                    if (visited.Add(dependent))
                    {
                        var methodInfo = GetMethodInfo(dependent);
                        if (methodInfo?.IsTestMethod == true)
                        {
                            testMethods.Add(dependent);
                        }
                        else
                        {
                            // Continue traversing non-test methods
                            queue.Enqueue(dependent);
                        }
                    }
                }
            }

            return testMethods;
        }

        public IReadOnlyCollection<TestCoverageResult> GetTestCoverageForMethod(string methodId)
        {
            var results = new List<TestCoverageResult>();
            var visited = new HashSet<string>();
            var paths = new Dictionary<string, List<string>>();

            // Debug logging for ScoreTestsAsync method lookup


            // BFS to find all test methods that can reach the target method
            var queue = new Queue<(string methodId, List<string> path)>();
            queue.Enqueue((methodId, new List<string> { methodId }));

            while (queue.Count > 0)
            {
                var (currentMethod, path) = queue.Dequeue();

                if (visited.Contains(currentMethod))
                    continue;
                
                visited.Add(currentMethod);

                foreach (var dependent in GetMethodDependents(currentMethod))
                {
                    if (!visited.Contains(dependent))
                    {
                        var newPath = new List<string>(path) { dependent };
                        
                        var methodInfo = GetMethodInfo(dependent);
                        if (methodInfo?.IsTestMethod == true)
                        {
                            // Found a test method - create coverage result
                            var confidence = CalculateConfidence(newPath);
                            results.Add(new TestCoverageResult(
                                dependent,
                                methodInfo.Name,
                                methodInfo.ContainingType,
                                methodInfo.FilePath,
                                newPath.ToArray(),
                                confidence));
                            

                        }
                        else
                        {
                            // Continue searching through this path
                            queue.Enqueue((dependent, newPath));
                        }
                    }
                }
            }



            return results;
        }

        private Dictionary<string, HashSet<string>> BuildReverseGraph()
        {
            var reverseGraph = new Dictionary<string, HashSet<string>>();

            foreach (var kvp in _callGraph)
            {
                var caller = kvp.Key;
                foreach (var callee in kvp.Value)
                {
                    if (!reverseGraph.ContainsKey(callee))
                        reverseGraph[callee] = new HashSet<string>();
                    
                    reverseGraph[callee].Add(caller);
                }
            }

            // Debug logging for ScoreTestsAsync reverse graph entries


            return reverseGraph;
        }

        private double CalculateConfidence(List<string> path)
        {
            if (path.Count <= 1) return 1.0;
            
            // Reduce confidence based on path length
            var depth = path.Count - 1;
            if (depth == 1) return 1.0;      // Direct call
            if (depth <= 3) return 0.8;     // Short path
            if (depth <= 6) return 0.6;     // Medium path
            return 0.4;                      // Long path
        }
    }
}