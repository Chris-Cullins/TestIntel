using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Interfaces;
using TestIntelligence.Core.Models;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Classification;

namespace TestIntelligence.ImpactAnalyzer.Services
{
    /// <summary>
    /// Implementation of test execution tracing that discovers all production code exercised by test methods.
    /// This provides the forward direction of coverage analysis (test → production code).
    /// </summary>
    public class TestExecutionTracer : ITestExecutionTracer
    {
        private readonly IRoslynAnalyzer _roslynAnalyzer;
        private readonly TestMethodClassifier _testClassifier;
        private readonly ILogger<TestExecutionTracer> _logger;
        
        // Cache to avoid rebuilding call graphs for the same solution
        private MethodCallGraph? _cachedCallGraph;
        private string? _cachedSolutionPath;
        
        // Cache execution trace calculations to avoid redundant traversals
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ExecutionTrace?> _traceCache = new();
        
        // Cache size management to prevent memory bloat
        private const int MaxCacheSize = 5000;
        private const int MaxCallDepth = 20;
        private const int MaxVisitedNodes = 2000;

        public TestExecutionTracer(
            IRoslynAnalyzer roslynAnalyzer,
            ILogger<TestExecutionTracer> logger)
        {
            _roslynAnalyzer = roslynAnalyzer ?? throw new ArgumentNullException(nameof(roslynAnalyzer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _testClassifier = new TestMethodClassifier();
        }

        public async Task<ExecutionTrace> TraceTestExecutionAsync(
            string testMethodId, 
            string solutionPath, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(testMethodId))
                throw new ArgumentException("Test method ID cannot be null or empty", nameof(testMethodId));
            
            if (string.IsNullOrWhiteSpace(solutionPath))
                throw new ArgumentException("Solution path cannot be null or empty", nameof(solutionPath));

            _logger.LogInformation("Tracing execution for test method: {TestMethodId}", testMethodId);

            // Check cache first
            if (_traceCache.TryGetValue(testMethodId, out var cachedTrace) && cachedTrace != null)
            {
                _logger.LogInformation("Using cached execution trace for test method: {TestMethodId}", testMethodId);
                return cachedTrace;
            }

            var callGraph = await GetOrBuildCallGraphAsync(solutionPath, cancellationToken);
            
            // Verify the method exists and is actually a test method
            var testMethodInfo = callGraph.GetMethodInfo(testMethodId);
            if (testMethodInfo == null)
            {
                throw new ArgumentException($"Test method not found: {testMethodId}", nameof(testMethodId));
            }

            if (!_testClassifier.IsTestMethod(testMethodInfo))
            {
                throw new ArgumentException($"Method is not a test method: {testMethodId}", nameof(testMethodId));
            }

            var executionTrace = TraceTestExecution(testMethodInfo, callGraph);
            
            // Cache the result (with size management)
            ManageTraceCache();
            _traceCache.TryAdd(testMethodId, executionTrace);

            return executionTrace;
        }

        public async Task<IReadOnlyList<ExecutionTrace>> TraceMultipleTestsAsync(
            IEnumerable<string> testMethodIds, 
            string solutionPath, 
            CancellationToken cancellationToken = default)
        {
            if (testMethodIds == null)
                throw new ArgumentNullException(nameof(testMethodIds));

            var testMethodIdList = testMethodIds.ToList();
            if (!testMethodIdList.Any())
                return Array.Empty<ExecutionTrace>();

            _logger.LogInformation("Tracing execution for {TestCount} test methods", testMethodIdList.Count);

            var callGraph = await GetOrBuildCallGraphAsync(solutionPath, cancellationToken);
            var results = new List<ExecutionTrace>();

            if (testMethodIdList.Count > 10) // Use parallel processing for larger batches
            {
                var parallelOptions = new ParallelOptions 
                { 
                    MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4),
                    CancellationToken = cancellationToken
                };
                
                var threadSafeResults = new System.Collections.Concurrent.ConcurrentBag<ExecutionTrace>();
                
                Parallel.ForEach(testMethodIdList, parallelOptions, testMethodId =>
                {
                    try
                    {
                        // Check cache first
                        if (_traceCache.TryGetValue(testMethodId, out var cachedTrace) && cachedTrace != null)
                        {
                            threadSafeResults.Add(cachedTrace);
                            return;
                        }

                        var testMethodInfo = callGraph.GetMethodInfo(testMethodId);
                        if (testMethodInfo != null && _testClassifier.IsTestMethod(testMethodInfo))
                        {
                            var trace = TraceTestExecution(testMethodInfo, callGraph);
                            _traceCache.TryAdd(testMethodId, trace);
                            threadSafeResults.Add(trace);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to trace execution for test method: {TestMethodId}", testMethodId);
                    }
                });
                
                results.AddRange(threadSafeResults);
            }
            else
            {
                // Sequential processing for smaller batches
                foreach (var testMethodId in testMethodIdList)
                {
                    try
                    {
                        var trace = await TraceTestExecutionAsync(testMethodId, solutionPath, cancellationToken);
                        results.Add(trace);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to trace execution for test method: {TestMethodId}", testMethodId);
                    }
                }
            }

            return results;
        }

        public async Task<ExecutionCoverageReport> GenerateCoverageReportAsync(
            string solutionPath,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Generating execution coverage report for solution: {SolutionPath}", solutionPath);

            var callGraph = await GetOrBuildCallGraphAsync(solutionPath, cancellationToken);
            
            var allMethods = callGraph.GetAllMethods()
                .Select(methodId => callGraph.GetMethodInfo(methodId))
                .Where(info => info != null)
                .Cast<MethodInfo>()
                .ToList();

            var testMethods = _testClassifier.GetTestMethods(allMethods);
            var testMethodIds = new HashSet<string>(testMethods.Select(tm => tm.Id));
            var productionMethods = allMethods.Where(m => !testMethodIds.Contains(m.Id)).ToList();

            // Trace all test methods
            var testTraces = await TraceMultipleTestsAsync(testMethods.Select(tm => tm.Id), solutionPath, cancellationToken);
            
            // Build test-to-execution map
            var testToExecutionMap = testTraces.ToDictionary(trace => trace.TestMethodId, trace => trace);
            
            // Find uncovered methods (production methods not hit by any test)
            var coveredMethodIds = new HashSet<string>(
                testTraces.SelectMany(trace => trace.ExecutedMethods)
                    .Where(em => em.IsProductionCode)
                    .Select(em => em.MethodId));
                    
            var uncoveredMethods = productionMethods
                .Where(pm => !coveredMethodIds.Contains(pm.Id))
                .Select(pm => pm.Id)
                .ToList();

            // Calculate statistics
            var statistics = CalculateCoverageStatistics(testTraces, productionMethods.Count);

            return new ExecutionCoverageReport
            {
                TestToExecutionMap = testToExecutionMap,
                UncoveredMethods = uncoveredMethods,
                Statistics = statistics,
                GeneratedTimestamp = DateTime.UtcNow
            };
        }

        private async Task<MethodCallGraph> GetOrBuildCallGraphAsync(string solutionPath, CancellationToken cancellationToken)
        {
            // Check if we have a cached call graph for this solution path
            if (_cachedCallGraph != null && _cachedSolutionPath == solutionPath)
            {
                _logger.LogInformation("Using cached call graph for solution: {SolutionPath}", solutionPath);
                return _cachedCallGraph;
            }

            // Build new call graph
            try 
            {
                var callGraph = await _roslynAnalyzer.BuildCallGraphAsync(new[] { solutionPath }, cancellationToken);
                
                // Cache the result
                _cachedCallGraph = callGraph;
                _cachedSolutionPath = solutionPath;
                
                return callGraph;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("System.CodeDom") || ex.Message.Contains("MSBuild workspace"))
            {
                _logger.LogWarning(ex, "MSBuild workspace failed, falling back to assembly-based analysis");
                
                // Fallback: Try to analyze compiled assemblies instead
                var assemblyPaths = FindTestAssembliesInSolution(solutionPath);
                if (assemblyPaths.Any())
                {
                    _logger.LogInformation("Found {AssemblyCount} test assemblies for fallback analysis", assemblyPaths.Count);
                    var callGraph = await _roslynAnalyzer.BuildCallGraphAsync(assemblyPaths.ToArray(), cancellationToken);
                    
                    // Cache the result
                    _cachedCallGraph = callGraph;
                    _cachedSolutionPath = solutionPath;
                    
                    return callGraph;
                }
                else
                {
                    throw new InvalidOperationException($"Unable to build call graph for solution: {solutionPath}");
                }
            }
        }

        private ExecutionTrace TraceTestExecution(MethodInfo testMethod, MethodCallGraph callGraph)
        {
            var executedMethods = new List<ExecutedMethod>();
            var visited = new HashSet<string>();
            var queue = new Queue<(string methodId, List<string> callPath, int depth)>();
            
            // Start with the test method itself
            queue.Enqueue((testMethod.Id, new List<string> { testMethod.Id }, 0));
            visited.Add(testMethod.Id);
            
            while (queue.Count > 0 && visited.Count < MaxVisitedNodes)
            {
                var (currentMethodId, currentCallPath, currentDepth) = queue.Dequeue();
                
                // Skip if we've reached max depth
                if (currentDepth >= MaxCallDepth)
                    continue;
                    
                var currentMethodInfo = callGraph.GetMethodInfo(currentMethodId);
                if (currentMethodInfo == null)
                    continue;
                
                // Add to executed methods (skip the starting test method for the results)
                if (currentDepth > 0)
                {
                    var isProductionCode = IsProductionCode(currentMethodInfo);
                    var category = CategorizeMethod(currentMethodInfo);
                    
                    var executedMethod = new ExecutedMethod(
                        currentMethodId,
                        currentMethodInfo.Name,
                        currentMethodInfo.ContainingType,
                        isProductionCode)
                    {
                        FilePath = currentMethodInfo.FilePath,
                        LineNumber = currentMethodInfo.LineNumber,
                        CallPath = currentCallPath.ToArray(),
                        CallDepth = currentDepth,
                        Category = category
                    };
                    
                    executedMethods.Add(executedMethod);
                }
                
                // Get methods called by the current method
                var calledMethods = callGraph.GetMethodCalls(currentMethodId).Take(50); // Limit breadth
                
                foreach (var calledMethodId in calledMethods)
                {
                    if (!visited.Contains(calledMethodId))
                    {
                        visited.Add(calledMethodId);
                        var newCallPath = new List<string>(currentCallPath) { calledMethodId };
                        queue.Enqueue((calledMethodId, newCallPath, currentDepth + 1));
                    }
                }
            }
            
            // Calculate metrics
            var productionMethodsCount = executedMethods.Count(em => em.IsProductionCode);
            var estimatedComplexity = TimeSpan.FromMilliseconds(executedMethods.Count * 10); // Rough estimation
            
            return new ExecutionTrace(
                testMethod.Id,
                testMethod.Name,
                testMethod.ContainingType)
            {
                ExecutedMethods = executedMethods,
                TotalMethodsCalled = executedMethods.Count,
                ProductionMethodsCalled = productionMethodsCount,
                EstimatedExecutionComplexity = estimatedComplexity,
                TraceTimestamp = DateTime.UtcNow
            };
        }

        private bool IsProductionCode(MethodInfo methodInfo)
        {
            // Not production code if it's a test method
            if (_testClassifier.IsTestMethod(methodInfo))
                return false;
                
            // Not production code if it's in a test project
            if (IsInTestProject(methodInfo.FilePath))
                return false;
                
            // Not production code if it's a framework method
            var category = CategorizeMethod(methodInfo);
            if (category == MethodCategory.Framework || category == MethodCategory.ThirdParty)
                return false;
                
            return true;
        }

        private MethodCategory CategorizeMethod(MethodInfo methodInfo)
        {
            var typeName = methodInfo.ContainingType.ToLowerInvariant();
            var methodName = methodInfo.Name.ToLowerInvariant();
            var filePath = methodInfo.FilePath.ToLowerInvariant();
            
            // Test utility methods
            if (_testClassifier.IsTestMethod(methodInfo) || IsInTestProject(filePath))
                return MethodCategory.TestUtility;
            
            // Framework methods
            if (IsFrameworkMethod(typeName))
                return MethodCategory.Framework;
                
            // Third-party methods
            if (IsThirdPartyMethod(typeName))
                return MethodCategory.ThirdParty;
                
            // Data access methods
            if (IsDataAccessMethod(typeName, methodName))
                return MethodCategory.DataAccess;
                
            // Infrastructure methods
            if (IsInfrastructureMethod(typeName, methodName))
                return MethodCategory.Infrastructure;
                
            // Default to business logic for production code
            return MethodCategory.BusinessLogic;
        }

        private bool IsFrameworkMethod(string typeName)
        {
            var frameworkPrefixes = new[]
            {
                "system.", "microsoft.", 
                "nunit.", "xunit.", "moq.", "fluentassertions."
            };
            
            return frameworkPrefixes.Any(prefix => typeName.StartsWith(prefix));
        }

        private bool IsThirdPartyMethod(string typeName)
        {
            // Common third-party library indicators
            var thirdPartyPrefixes = new[]
            {
                "newtonsoft.", "automapper.", 
                "serilog.", "npgsql.", "entityframework.", "dapper.",
                "polly.", "mediatr.", "hangfire.", "quartz."
            };
            
            return thirdPartyPrefixes.Any(prefix => typeName.StartsWith(prefix));
        }

        private bool IsDataAccessMethod(string typeName, string methodName)
        {
            var dataAccessIndicators = new[]
            {
                "repository", "dbcontext", "dataaccess", "dal", "entity",
                "mapper", "query", "command"
            };
            
            return dataAccessIndicators.Any(indicator => 
                typeName.Contains(indicator) || methodName.Contains(indicator));
        }

        private bool IsInfrastructureMethod(string typeName, string methodName)
        {
            var infrastructureIndicators = new[]
            {
                "factory", "provider", "manager", "helper",
                "utility", "configuration", "logging", "logger", "caching", "cache"
            };
            
            // More specific service detection - only certain patterns
            var infrastructureServicePatterns = new[]
            {
                "loggingservice", "configurationservice", "cachingservice",
                "utilityservice", "helperservice"
            };
            
            // Check for infrastructure patterns but exclude business services
            var hasInfrastructureIndicator = infrastructureIndicators.Any(indicator => 
                typeName.Contains(indicator) || methodName.Contains(indicator));
                
            var hasInfrastructureServicePattern = infrastructureServicePatterns.Any(pattern =>
                typeName.Contains(pattern));
                
            return hasInfrastructureIndicator || hasInfrastructureServicePattern;
        }

        private bool IsInTestProject(string filePath)
        {
            var testProjectPatterns = new[] { "test", "tests", "unittest", "integrationtest", "spec" };
            var pathParts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            
            return pathParts.Any(part => testProjectPatterns.Any(pattern => 
                part.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private CoverageStatistics CalculateCoverageStatistics(IEnumerable<ExecutionTrace> traces, int totalProductionMethods)
        {
            var tracesList = traces.ToList();
            var coveredMethods = new HashSet<string>(
                tracesList.SelectMany(t => t.ExecutedMethods)
                    .Where(em => em.IsProductionCode)
                    .Select(em => em.MethodId));
            
            var categoryBreakdown = new Dictionary<MethodCategory, int>();
            foreach (var trace in tracesList)
            {
                foreach (var method in trace.ExecutedMethods.Where(em => em.IsProductionCode))
                {
                    if (!categoryBreakdown.ContainsKey(method.Category))
                        categoryBreakdown[method.Category] = 0;
                    categoryBreakdown[method.Category]++;
                }
            }
            
            var callDepths = tracesList.SelectMany(t => t.ExecutedMethods.Select(em => em.CallDepth)).ToList();
            
            return new CoverageStatistics
            {
                TotalProductionMethods = totalProductionMethods,
                CoveredProductionMethods = coveredMethods.Count,
                TotalTestMethods = tracesList.Count,
                AverageCallDepth = callDepths.Any() ? (int)callDepths.Average() : 0,
                MaxCallDepth = callDepths.Any() ? callDepths.Max() : 0,
                CategoryBreakdown = categoryBreakdown
            };
        }

        private void ManageTraceCache()
        {
            if (_traceCache.Count >= MaxCacheSize)
            {
                var keysToRemove = _traceCache.Keys.Take(MaxCacheSize / 4).ToList(); // Remove oldest 25%
                foreach (var key in keysToRemove)
                {
                    _traceCache.TryRemove(key, out _);
                }
                _logger.LogDebug("Cache cleanup: removed {RemovedCount} entries, cache size now {CacheSize}", 
                    keysToRemove.Count, _traceCache.Count);
            }
        }

        private IReadOnlyList<string> FindTestAssembliesInSolution(string solutionPath)
        {
            var assemblies = new List<string>();
            var solutionDir = Path.GetDirectoryName(solutionPath);
            
            if (string.IsNullOrEmpty(solutionDir))
                return assemblies;

            try
            {
                // Look for test assemblies in bin/Debug and bin/Release folders
                var searchPatterns = new[] { "*Test*.dll", "*.Tests.dll" };
                
                foreach (var searchPattern in searchPatterns)
                {
                    var files = Directory.GetFiles(solutionDir, searchPattern, SearchOption.AllDirectories)
                        .Where(f => f.Contains("bin") && 
                                   (f.Contains("Debug") || f.Contains("Release")) &&
                                   f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    
                    assemblies.AddRange(files);
                }

                // Remove duplicates and prefer Debug over Release
                var uniqueAssemblies = assemblies
                    .GroupBy(f => Path.GetFileName(f))
                    .Select(g => g.OrderBy(f => f.Contains("Release") ? 1 : 0).First())
                    .ToList();

                return uniqueAssemblies;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching for test assemblies in solution directory: {SolutionDir}", solutionDir);
                return assemblies;
            }
        }

        /// <summary>
        /// Clear all caches. Call this when the solution or source files change.
        /// </summary>
        public void ClearCaches()
        {
            _cachedCallGraph = null;
            _cachedSolutionPath = null;
            _traceCache.Clear();
            _logger.LogDebug("Cleared all caches");
        }
    }
}