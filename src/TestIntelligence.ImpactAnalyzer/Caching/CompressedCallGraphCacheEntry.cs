using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TestIntelligence.ImpactAnalyzer.Caching
{
    /// <summary>
    /// Represents a cached call graph analysis result with compression support.
    /// </summary>
    public class CompressedCallGraphCacheEntry
    {
        public string ProjectPath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string DependenciesHash { get; set; } = string.Empty;
        public string CompilerVersion { get; set; } = string.Empty;
        public Dictionary<string, HashSet<string>> CallGraph { get; set; } = new();
        public Dictionary<string, HashSet<string>> ReverseCallGraph { get; set; } = new();
        public long UncompressedSize { get; set; }
        public long CompressedSize { get; set; }
        public TimeSpan BuildTime { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
        
        /// <summary>
        /// Alternative property name for dependency hashes (for test compatibility).
        /// </summary>
        public Dictionary<string, string> DependencyHashes { get; set; } = new();

        /// <summary>
        /// Generates a cache key based on project characteristics.
        /// </summary>
        /// <param name="projectPath">Path to the project.</param>
        /// <param name="dependencyHashes">Hashes of referenced assemblies.</param>
        /// <param name="compilerVersion">Compiler version used.</param>
        /// <param name="additionalFactors">Additional factors to include in the key.</param>
        /// <returns>A unique cache key.</returns>
        public static string GenerateCacheKey(string projectPath, IEnumerable<string> dependencyHashes, string compilerVersion, params string[] additionalFactors)
        {
            var keyComponents = new List<string>
            {
                projectPath,
                compilerVersion
            };
            keyComponents.AddRange(dependencyHashes);
            keyComponents.AddRange(additionalFactors);

            var combinedKey = string.Join("|", keyComponents);
            return ComputeHash(combinedKey);
        }

        /// <summary>
        /// Validates that the cached entry is still valid for the given context.
        /// </summary>
        /// <param name="projectPath">Current project path.</param>
        /// <param name="dependencyHashes">Current dependency hashes.</param>
        /// <param name="compilerVersion">Current compiler version.</param>
        /// <returns>True if the cache entry is valid.</returns>
        public bool IsValidForContext(string projectPath, IEnumerable<string> dependencyHashes, string compilerVersion)
        {
            if (ProjectPath != projectPath)
                return false;

            if (CompilerVersion != compilerVersion)
                return false;

            var currentDependenciesHash = ComputeHash(string.Join("|", dependencyHashes));
            return DependenciesHash == currentDependenciesHash;
        }

        /// <summary>
        /// Gets statistics about the call graph size and complexity.
        /// </summary>
        public CallGraphStatistics GetStatistics()
        {
            var totalMethods = CallGraph.Keys.Count;
            var totalEdges = CallGraph.Values.Sum(callees => callees.Count);
            var averageFanOut = totalMethods > 0 ? (double)totalEdges / totalMethods : 0;
            
            var maxFanOut = CallGraph.Values.Max(callees => callees?.Count ?? 0);
            var maxFanIn = ReverseCallGraph.Values.Max(callers => callers?.Count ?? 0);

            return new CallGraphStatistics
            {
                TotalMethods = totalMethods,
                TotalEdges = totalEdges,
                AverageFanOut = averageFanOut,
                MaxFanOut = maxFanOut,
                MaxFanIn = maxFanIn,
                GraphDensity = totalMethods > 1 ? (double)totalEdges / (totalMethods * (totalMethods - 1)) : 0
            };
        }

        /// <summary>
        /// Validates the integrity of the call graph data.
        /// </summary>
        public CallGraphValidationResult ValidateIntegrity()
        {
            var result = new CallGraphValidationResult();
            
            // Basic null checks
            if (CallGraph == null)
            {
                result.Issues.Add("CallGraph is null");
                result.IsValid = false;
                return result;
            }
            
            if (ReverseCallGraph == null)
            {
                result.Issues.Add("ReverseCallGraph is null");
                result.IsValid = false;
                return result;
            }

            // Check basic consistency - allow for some flexibility in edge cases
            var criticalErrors = 0;
            var warningCount = 0;
            const int maxWarnings = 10; // Allow some inconsistencies

            // Check that reverse graph is consistent with forward graph
            foreach (var kvp in CallGraph)
            {
                var caller = kvp.Key ?? "";
                var callees = kvp.Value ?? new HashSet<string>();
                
                if (string.IsNullOrEmpty(caller))
                {
                    criticalErrors++;
                    result.Issues.Add("Empty or null caller found in CallGraph");
                    continue;
                }
                
                foreach (var callee in callees.Where(c => !string.IsNullOrEmpty(c)))
                {
                    if (!ReverseCallGraph.ContainsKey(callee))
                    {
                        warningCount++;
                        if (warningCount <= maxWarnings)
                            result.Issues.Add($"Method {callee} is called by {caller} but not present in reverse graph");
                        continue;
                    }

                    if (!ReverseCallGraph[callee]?.Contains(caller) == true)
                    {
                        warningCount++;
                        if (warningCount <= maxWarnings)
                            result.Issues.Add($"Reverse graph missing edge: {caller} -> {callee}");
                    }
                }
            }

            // Check reverse consistency - be more tolerant of inconsistencies
            foreach (var kvp in ReverseCallGraph)
            {
                var callee = kvp.Key ?? "";
                var callers = kvp.Value ?? new HashSet<string>();
                
                if (string.IsNullOrEmpty(callee))
                {
                    criticalErrors++;
                    result.Issues.Add("Empty or null callee found in ReverseCallGraph");
                    continue;
                }
                
                foreach (var caller in callers.Where(c => !string.IsNullOrEmpty(c)))
                {
                    if (!CallGraph.ContainsKey(caller) || !CallGraph[caller]?.Contains(callee) == true)
                    {
                        warningCount++;
                        if (warningCount <= maxWarnings)
                            result.Issues.Add($"Forward graph missing edge: {caller} -> {callee}");
                    }
                }
            }

            // Mark as invalid for critical errors or significant inconsistencies
            result.IsValid = criticalErrors == 0 && warningCount == 0;
            
            if (warningCount > maxWarnings)
            {
                result.Issues.Add($"... and {warningCount - maxWarnings} more consistency issues (truncated)");
            }
            
            return result;
        }

        private static string ComputeHash(string input)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
    }

    /// <summary>
    /// Statistics about call graph structure and complexity.
    /// </summary>
    public class CallGraphStatistics
    {
        public int TotalMethods { get; set; }
        public int TotalEdges { get; set; }
        public double AverageFanOut { get; set; }
        public int MaxFanOut { get; set; }
        public int MaxFanIn { get; set; }
        public double GraphDensity { get; set; }
    }

    /// <summary>
    /// Result of call graph data validation.
    /// </summary>
    public class CallGraphValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Issues { get; set; } = new();
    }
}