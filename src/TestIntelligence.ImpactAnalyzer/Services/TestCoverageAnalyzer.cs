using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Models;
using TestIntelligence.Core.Services;
using TestIntelligence.ImpactAnalyzer.Classification;
using TestIntelligence.ImpactAnalyzer.Analysis;

namespace TestIntelligence.ImpactAnalyzer.Services
{
    /// <summary>
    /// Implementation of test coverage analysis that finds which tests exercise specific production methods.
    /// Implements all focused test coverage interfaces for comprehensive functionality.
    /// </summary>
    public class TestCoverageAnalyzer : 
        ITestCoverageAnalyzer,
        ITestCoverageQuery,
        ITestCoverageMapBuilder,
        ITestCoverageStatistics,
        ITestCoverageCacheManager
    {
        private readonly IRoslynAnalyzer _roslynAnalyzer;
        private readonly TestMethodClassifier _testClassifier;
        private readonly IAssemblyPathResolver _assemblyPathResolver;
        private readonly ILogger<TestCoverageAnalyzer> _logger;
        
        // Cache to avoid rebuilding call graphs for the same solution
        private MethodCallGraph? _cachedCallGraph;
        private string? _cachedSolutionPath;
        
        // Cache BFS path calculations to avoid redundant traversals (LRU to bound memory)
        private readonly TestIntelligence.Core.Caching.LruCache<(string, string), string[]?> _pathCache = new(capacity: 10000);

        // Path search limits (configurable)
        private readonly int _maxVisitedNodes;
        private readonly int _maxPathDepth;

        public TestCoverageAnalyzer(
            IRoslynAnalyzer roslynAnalyzer,
            IAssemblyPathResolver assemblyPathResolver,
            ILogger<TestCoverageAnalyzer> logger)
        {
            _roslynAnalyzer = roslynAnalyzer ?? throw new ArgumentNullException(nameof(roslynAnalyzer));
            _assemblyPathResolver = assemblyPathResolver ?? throw new ArgumentNullException(nameof(assemblyPathResolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _testClassifier = new TestMethodClassifier();

            // Configure traversal limits via environment variables with sensible defaults
            _maxPathDepth = GetConfiguredInt("TI_MAX_PATH_DEPTH", 12, 2, 200);
            _maxVisitedNodes = GetConfiguredInt("TI_MAX_VISITED_NODES", 2000, 100, 100000);
        }

        private static int GetConfiguredInt(string envVar, int defaultValue, int min, int max)
        {
            try
            {
                var raw = Environment.GetEnvironmentVariable(envVar);
                if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var parsed))
                {
                    if (parsed < min) return min;
                    if (parsed > max) return max;
                    return parsed;
                }
            }
            catch
            {
                // ignore
            }
            return defaultValue;
        }

        public async Task<IReadOnlyList<TestCoverageInfo>> FindTestsExercisingMethodAsync(
            string methodId, 
            string solutionPath, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(methodId))
                throw new ArgumentException("Method ID cannot be null or empty", nameof(methodId));
            
            if (string.IsNullOrWhiteSpace(solutionPath))
                throw new ArgumentException("Solution path cannot be null or empty", nameof(solutionPath));

            // Enhanced logging for test coverage analysis debugging
            _logger.LogDebug("Analyzing method coverage for: {MethodId}", methodId);

            _logger.LogInformation("Finding tests exercising method: {MethodId} using streaming analysis", methodId);

            // Use streaming incremental analysis for much better performance
            var results = new List<TestCoverageInfo>();
            await foreach (var coverageInfo in FindTestsExercisingMethodStreamAsync(methodId, solutionPath, cancellationToken))
            {
                results.Add(coverageInfo);
            }

            _logger.LogInformation("Found {TestCount} tests exercising method {MethodId}", results.Count, methodId);
            return results;
        }

        public async Task<TestCoverageMap> BuildTestCoverageMapAsync(
            string solutionPath, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(solutionPath))
                throw new ArgumentException("Solution path cannot be null or empty", nameof(solutionPath));

            _logger.LogInformation("Building test coverage map for solution: {SolutionPath}", solutionPath);

            // Check if we have a cached call graph for this solution path
            MethodCallGraph callGraph;
            if (_cachedCallGraph != null && _cachedSolutionPath == solutionPath)
            {
                _logger.LogInformation("Using cached call graph for solution: {SolutionPath}", solutionPath);
                callGraph = _cachedCallGraph;
            }
            else
            {
                // Try to build call graph with MSBuild workspace first, fallback to assembly analysis
                try 
                {
                    // Apply timeout to prevent hanging during call graph building
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(3)); // 3 minute timeout
                    using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                    
                    // Build the complete call graph using the solution path
                    // The Roslyn analyzer will handle finding source files and prefer MSBuild workspace if .sln is provided
                    callGraph = await _roslynAnalyzer.BuildCallGraphAsync(new[] { solutionPath }, combinedCts.Token);
                    
                    // Cache the result
                    _cachedCallGraph = callGraph;
                    _cachedSolutionPath = solutionPath;
                }
                catch (TimeoutException ex)
                {
                    _logger.LogWarning(ex, "Call graph building timed out after 3 minutes, falling back to assembly-based analysis");
                    
                    // Fallback: Try to analyze compiled assemblies instead
                    var assemblyPaths = await _assemblyPathResolver.FindTestAssembliesInSolutionAsync(solutionPath);
                    if (assemblyPaths.Any())
                    {
                        _logger.LogInformation("Found {AssemblyCount} test assemblies for fallback analysis", assemblyPaths.Count);
                        callGraph = await _roslynAnalyzer.BuildCallGraphAsync(assemblyPaths.ToArray(), cancellationToken);
                        
                        // Cache the result
                        _cachedCallGraph = callGraph;
                        _cachedSolutionPath = solutionPath;
                    }
                    else
                    {
                        _logger.LogWarning("No assemblies found for fallback analysis");
                        return new TestCoverageMap(new Dictionary<string, List<TestCoverageInfo>>(), DateTime.UtcNow, solutionPath);
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("System.CodeDom") || ex.Message.Contains("MSBuild workspace"))
                {
                    _logger.LogWarning(ex, "MSBuild workspace failed, falling back to assembly-based analysis");
                    
                    // Fallback: Try to analyze compiled assemblies instead
                    var assemblyPaths = await _assemblyPathResolver.FindTestAssembliesInSolutionAsync(solutionPath);
                    if (assemblyPaths.Any())
                    {
                        _logger.LogInformation("Found {AssemblyCount} test assemblies for fallback analysis", assemblyPaths.Count);
                        callGraph = await _roslynAnalyzer.BuildCallGraphAsync(assemblyPaths.ToArray(), cancellationToken);
                        
                        // Cache the result
                        _cachedCallGraph = callGraph;
                        _cachedSolutionPath = solutionPath;
                    }
                    else
                    {
                        _logger.LogWarning("No assemblies found for fallback analysis");
                        return new TestCoverageMap(new Dictionary<string, List<TestCoverageInfo>>(), DateTime.UtcNow, solutionPath);
                    }
                }
            }
            
            // Identify test methods and production methods
            var allMethods = callGraph.GetAllMethods()
                .Select(methodId => callGraph.GetMethodInfo(methodId))
                .Where(info => info != null)
                .Cast<MethodInfo>()
                .ToList();

            var testMethods = _testClassifier.GetTestMethods(allMethods);
            var testMethodIds = new HashSet<string>(testMethods.Select(tm => tm.Id));

            _logger.LogInformation("Found {TestMethodCount} test methods out of {TotalMethodCount} total methods", 
                testMethods.Count, allMethods.Count);

            // Build the coverage map
            var methodToTests = new System.Collections.Concurrent.ConcurrentDictionary<string, List<TestCoverageInfo>>();

            // Filter out test methods upfront to avoid repeated checks
            var productionMethods = allMethods.Where(method => !testMethodIds.Contains(method.Id)).ToList();
            _logger.LogInformation("Processing {ProductionMethodCount} production methods (filtered from {TotalMethodCount})", 
                productionMethods.Count, allMethods.Count);

            if (productionMethods.Count > 20) // Use parallel for larger codebases
            {
                var parallelOptions = new ParallelOptions 
                { 
                    MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4) // Don't overload with too many threads
                };

                Parallel.ForEach(productionMethods, parallelOptions, method =>
                {
                    var coveringTests = FindTestsCoveringMethod(method, testMethods, callGraph);
                    if (coveringTests.Any())
                    {
                        methodToTests[method.Id] = coveringTests.ToList();
                    }
                });
            }
            else
            {
                // Sequential processing for smaller codebases
                foreach (var method in productionMethods)
                {
                    var coveringTests = FindTestsCoveringMethod(method, testMethods, callGraph);
                    if (coveringTests.Any())
                    {
                        methodToTests[method.Id] = coveringTests.ToList();
                    }
                }
            }

            _logger.LogInformation("Built coverage map with {CoveredMethodCount} methods having test coverage", 
                methodToTests.Count);

            // Debug: Log first few covered methods for troubleshooting
            if (methodToTests.Count > 0)
            {
                _logger.LogInformation("Sample covered methods:");
                foreach (var kvp in methodToTests.Take(5))
                {
                    _logger.LogInformation("  {MethodId} -> {TestCount} tests", kvp.Key, kvp.Value.Count);
                }
            }

            return new TestCoverageMap(
                new Dictionary<string, List<TestCoverageInfo>>(methodToTests),
                DateTime.UtcNow,
                solutionPath);
        }

        public async Task<IReadOnlyDictionary<string, IReadOnlyList<TestCoverageInfo>>> FindTestsExercisingMethodsAsync(
            IEnumerable<string> methodIds,
            string solutionPath,
            CancellationToken cancellationToken = default)
        {
            if (methodIds == null)
                throw new ArgumentNullException(nameof(methodIds));

            var methodIdList = methodIds.ToList();
            if (!methodIdList.Any())
                return new Dictionary<string, IReadOnlyList<TestCoverageInfo>>();

            _logger.LogInformation("Finding tests for {MethodCount} methods", methodIdList.Count);

            var coverageMap = await BuildTestCoverageMapAsync(solutionPath, cancellationToken);
            
            var result = new Dictionary<string, IReadOnlyList<TestCoverageInfo>>();
            foreach (var methodId in methodIdList)
            {
                result[methodId] = coverageMap.GetTestsForMethodPattern(methodId);
            }

            return result;
        }

        public async Task<IReadOnlyDictionary<string, IReadOnlyList<TestCoverageInfo>>> FindTestsExercisingMethodsScopedAsync(
            IEnumerable<string> methodIds,
            IEnumerable<string> providedTestIds,
            string solutionPath,
            CancellationToken cancellationToken = default)
        {
            if (methodIds == null) throw new ArgumentNullException(nameof(methodIds));
            if (providedTestIds == null) throw new ArgumentNullException(nameof(providedTestIds));

            var targetMethods = methodIds.ToList();
            var candidateTests = providedTestIds.ToList();
            var results = new Dictionary<string, IReadOnlyList<TestCoverageInfo>>();

            if (targetMethods.Count == 0 || candidateTests.Count == 0)
                return results;

            _logger.LogInformation("Scoped coverage: {Targets} methods, {Tests} candidate tests", targetMethods.Count, candidateTests.Count);

            // Build a scoped incremental call graph seeded by target methods + candidate tests
            var scope = new TestIntelligence.ImpactAnalyzer.Analysis.AnalysisScope(solutionPath)
            {
                MaxExpansionDepth = 5
            };
            foreach (var m in targetMethods) scope.ChangedMethods.Add(m);
            foreach (var t in candidateTests) scope.TargetTests.Add(t);

            MethodCallGraph? callGraph = null;
            try
            {
                callGraph = await _roslynAnalyzer.BuildCallGraphAsync(scope, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scoped call graph failed; will fall back to full coverage map");
            }

            if (callGraph == null)
            {
                // Fallback to full path
                var full = await FindTestsExercisingMethodsAsync(targetMethods, solutionPath, cancellationToken);
                // Filter to provided tests
                foreach (var kvp in full)
                {
                    results[kvp.Key] = kvp.Value.Where(v => candidateTests.Contains(v.TestMethodId, StringComparer.OrdinalIgnoreCase)).ToList();
                }
                return results;
            }

            // Get method info lists for calculation
            var allMethods = callGraph.GetAllMethods()
                .Select(id => callGraph.GetMethodInfo(id))
                .Where(info => info != null)
                .Cast<MethodInfo>()
                .ToList();

            // Map for quick lookup
            var methodInfoById = allMethods.ToDictionary(m => m.Id, m => m, StringComparer.OrdinalIgnoreCase);

            foreach (var target in targetMethods)
            {
                var list = new List<TestCoverageInfo>();
                foreach (var testId in candidateTests)
                {
                    var path = FindCallPath(testId, target, callGraph);
                    if (path != null && path.Length > 0)
                    {
                        methodInfoById.TryGetValue(testId, out var testInfo);
                        methodInfoById.TryGetValue(target, out var targetInfo);

                        var testMethodInfo = testInfo ?? new MethodInfo(testId, testId.Split('.').Last(), "", testId, 0, true);
                        var targetMethodInfo = targetInfo ?? new MethodInfo(target, target.Split('.').Last(), "", target, 0, false);

                        var conf = CalculateConfidence(path, testMethodInfo, targetMethodInfo);
                        var ttype = _testClassifier.ClassifyTestType(testMethodInfo);

                        list.Add(new TestCoverageInfo(
                            testMethodInfo.Id,
                            testMethodInfo.Name,
                            testMethodInfo.ContainingType,
                            System.IO.Path.GetFileName(testMethodInfo.FilePath),
                            path,
                            conf,
                            ttype));
                    }
                    else
                    {
                        _logger.LogDebug("No path found from test '{TestId}' to target '{TargetId}' in scoped graph", testId, target);
                    }
                }
                results[target] = list;
            }

            return results;
        }

        public async Task<TestCoverageStatistics> GetCoverageStatisticsAsync(
            string solutionPath,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Calculating coverage statistics for solution: {SolutionPath}", solutionPath);

            var coverageMap = await BuildTestCoverageMapAsync(solutionPath, cancellationToken);
            
            // Use cached call graph (already built in BuildTestCoverageMapAsync)
            if (_cachedCallGraph == null)
            {
                _logger.LogError("Call graph should be cached after BuildTestCoverageMapAsync, but it's null");
                throw new InvalidOperationException("Call graph not available after building coverage map");
            }
            
            var callGraph = _cachedCallGraph;
            
            var allMethods = callGraph.GetAllMethods()
                .Select(methodId => callGraph.GetMethodInfo(methodId))
                .Where(info => info != null)
                .Cast<MethodInfo>()
                .ToList();

            var testMethods = _testClassifier.GetTestMethods(allMethods);
            var testMethodIds = new HashSet<string>(testMethods.Select(tm => tm.Id));
            
            var productionMethods = allMethods.Where(m => !testMethodIds.Contains(m.Id)).ToList();
            
            // Calculate coverage by test type
            var coverageByTestType = new Dictionary<TestType, int>();
            foreach (var coverage in coverageMap.MethodToTests.Values.SelectMany(tests => tests))
            {
                if (!coverageByTestType.ContainsKey(coverage.TestType))
                    coverageByTestType[coverage.TestType] = 0;
                coverageByTestType[coverage.TestType]++;
            }

            return new TestCoverageStatistics(
                totalMethods: productionMethods.Count,
                coveredMethods: coverageMap.CoveredMethodCount,
                totalTests: testMethods.Count,
                totalCoverageRelationships: coverageMap.TotalCoverageRelationships,
                coverageByTestType: coverageByTestType);
        }

        private IEnumerable<TestCoverageInfo> FindTestsCoveringMethod(
            MethodInfo targetMethod, 
            IReadOnlyList<MethodInfo> testMethods, 
            MethodCallGraph callGraph)
        {
            // Use parallel processing for path finding when we have many test methods
            var coverageInfos = new List<TestCoverageInfo>();
            
            // LRU cache bounds memory; no additional throttling needed here.
            
            if (testMethods.Count > 50) // Use parallel for large test suites
            {
                var parallelOptions = new ParallelOptions 
                { 
                    MaxDegreeOfParallelism = Environment.ProcessorCount 
                };
                
                var threadSafeCoverageInfos = new System.Collections.Concurrent.ConcurrentBag<TestCoverageInfo>();
                var processedCount = 0;
                
                Parallel.ForEach(testMethods, parallelOptions, (testMethod, parallelLoopState) =>
                {
                    // Safety check: limit number of processed tests to prevent runaway analysis
                    if (Interlocked.Increment(ref processedCount) > 500)
                    {
                        _logger.LogWarning("Processed 500+ tests, stopping parallel analysis to prevent performance issues");
                        parallelLoopState.Stop();
                        return;
                    }
                    
                    var callPath = FindCallPath(testMethod.Id, targetMethod.Id, callGraph);
                    if (callPath != null && callPath.Any())
                    {
                        var confidence = CalculateConfidence(callPath, testMethod, targetMethod);
                        var testType = _testClassifier.ClassifyTestType(testMethod);

                        var coverageInfo = new TestCoverageInfo(
                            testMethod.Id,
                            testMethod.Name,
                            testMethod.ContainingType,
                            Path.GetFileName(testMethod.FilePath), // Just the assembly name
                            callPath,
                            confidence,
                            testType);

                        threadSafeCoverageInfos.Add(coverageInfo);
                    }
                });
                
                coverageInfos.AddRange(threadSafeCoverageInfos);
            }
            else
            {
                // Use sequential processing for smaller test suites
                var processedCount = 0;
                foreach (var testMethod in testMethods)
                {
                    // Safety check: limit sequential processing as well
                    if (processedCount++ > 200)
                    {
                        _logger.LogWarning("Processed 200+ tests sequentially, stopping to prevent performance issues");
                        break;
                    }
                    
                    var callPath = FindCallPath(testMethod.Id, targetMethod.Id, callGraph);
                    if (callPath != null && callPath.Any())
                    {
                        var confidence = CalculateConfidence(callPath, testMethod, targetMethod);
                        var testType = _testClassifier.ClassifyTestType(testMethod);

                        var coverageInfo = new TestCoverageInfo(
                            testMethod.Id,
                            testMethod.Name,
                            testMethod.ContainingType,
                            Path.GetFileName(testMethod.FilePath), // Just the assembly name
                            callPath,
                            confidence,
                            testType);

                        coverageInfos.Add(coverageInfo);
                    }
                }
            }

            return coverageInfos;
        }

		private string[]? FindCallPath(string testMethodId, string targetMethodId, MethodCallGraph callGraph)
		{
			// Check LRU cache first
			var cacheKey = (testMethodId, targetMethodId);
			if (_pathCache.TryGetValue(cacheKey, out var cachedPath))
				return cachedPath;
            
            // Early termination: if the test and target method are the same, return direct path
            if (testMethodId == targetMethodId)
            {
                var directPath = new[] { testMethodId };
                _pathCache.Set(cacheKey, directPath);
                return directPath;
            }
            
            // Use BFS to find shortest path from test method to target method
            var queue = new Queue<(string methodId, List<string> path)>();
            var visited = new HashSet<string>();
            var maxPathLength = _maxPathDepth;
            var maxVisitedNodes = _maxVisitedNodes;

            queue.Enqueue((testMethodId, new List<string> { testMethodId }));
            visited.Add(testMethodId);

            

			// TEMP DIAGNOSTIC counters
			var expansions = 0;
			var reachedDepthLimitCount = 0;
            while (queue.Count > 0 && visited.Count < maxVisitedNodes)
            {
                var (currentMethod, currentPath) = queue.Dequeue();
                expansions++;

                // Check if we found the target
                if (currentMethod == targetMethodId)
                {
					var path = currentPath.ToArray();
                    _pathCache.Set(cacheKey, path);
                    return path;
                }

                // Avoid paths that are too long (prevent infinite recursion and overly complex paths)
				if (currentPath.Count >= maxPathLength)
				{
					reachedDepthLimitCount++;
					continue;
				}

                // Get methods called by the current method
                var fullCalledMethods = callGraph.GetMethodCalls(currentMethod);

                // Prefer direct hit before exploring to avoid extra work
                if (fullCalledMethods.Contains(targetMethodId))
                {
                    var directPath = new List<string>(currentPath) { targetMethodId };
                    var arr = directPath.ToArray();
                    _pathCache.Set(cacheKey, arr);
                    return arr;
                }

                // Explore all neighbors, but prioritize likely matches.
                var targetInfo = callGraph.GetMethodInfo(targetMethodId);
                var orderedNeighbors = fullCalledMethods
                    .OrderBy(m => GetHeuristicRank(callGraph, m, targetInfo))
                    .ThenBy(m => m, StringComparer.Ordinal);

                foreach (var calledMethod in orderedNeighbors)
                {
                    if (!visited.Contains(calledMethod))
                    {
                        visited.Add(calledMethod);
                        var newPath = new List<string>(currentPath) { calledMethod };
                        queue.Enqueue((calledMethod, newPath));
                    }
                }
            }

				// Cache negative results too
				_pathCache.Set(cacheKey, null);
				return null; // No path found
		}

        private double CalculateConfidence(string[] callPath, MethodInfo testMethod, MethodInfo targetMethod)
        {
            if (callPath == null || callPath.Length < 2)
                return 0.0;

            double confidence = 1.0;

            // Reduce confidence based on call depth
            int depth = callPath.Length - 1;
            if (depth == 1)
                confidence = 1.0; // Direct call
            else if (depth <= 3)
                confidence = 0.8; // Short indirect call
            else if (depth <= 6)
                confidence = 0.6; // Medium indirect call
            else
                confidence = 0.4; // Long indirect call

            // Boost confidence for certain test types
            var testType = _testClassifier.ClassifyTestType(testMethod);
            switch (testType)
            {
                case TestType.Unit:
                    confidence *= 1.0; // No change
                    break;
                case TestType.Integration:
                    confidence *= 0.9; // Slightly lower confidence due to complexity
                    break;
                case TestType.End2End:
                    confidence *= 0.8; // Lower confidence due to many interactions
                    break;
                default:
                    confidence *= 0.9;
                    break;
            }

            // Boost confidence if test classifier is confident this is a test
            var testConfidence = _testClassifier.CalculateTestConfidence(testMethod);
            confidence *= (0.5 + 0.5 * testConfidence); // Scale between 0.5 and 1.0

            return Math.Max(0.0, Math.Min(1.0, confidence));
        }

        private static int GetHeuristicRank(MethodCallGraph callGraph, string methodId, MethodInfo? targetInfo)
        {
            if (targetInfo == null) return 2;
            var info = callGraph.GetMethodInfo(methodId);
            if (info == null) return 2;
            if (string.Equals(info.ContainingType, targetInfo.ContainingType, StringComparison.Ordinal)) return 0;
            return 1;
        }



        /// <summary>
        /// Streams test coverage results incrementally, yielding results as they are found.
        /// This provides better performance and responsiveness for large codebases.
        /// </summary>
        public async IAsyncEnumerable<TestCoverageInfo> FindTestsExercisingMethodStreamAsync(
            string methodId, 
            string solutionPath, 
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(methodId))
                throw new ArgumentException("Method ID cannot be null or empty", nameof(methodId));
            
            if (string.IsNullOrWhiteSpace(solutionPath))
                throw new ArgumentException("Solution path cannot be null or empty", nameof(solutionPath));

            _logger.LogInformation("Starting streaming analysis for method: {MethodId}", methodId);

            // Try to use incremental call graph builder if available through RoslynAnalyzer
            MethodCallGraph? callGraph = null;
            try
            {
                // Build call graph incrementally - this should be much faster than full solution analysis
                callGraph = await _roslynAnalyzer.BuildCallGraphAsync(new[] { solutionPath }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to perform streaming analysis for method: {MethodId}", methodId);
                callGraph = null;
            }

            // If call graph building failed, use fallback
            if (callGraph == null)
            {
                var fallbackResults = await FindTestsExercisingMethodFallbackAsync(methodId, solutionPath, cancellationToken);
                foreach (var result in fallbackResults)
                {
                    yield return result;
                }
                yield break;
            }
            
            // Find all test methods quickly
            var allMethods = callGraph.GetAllMethods()
                .Select(id => callGraph.GetMethodInfo(id))
                .Where(info => info != null)
                .Cast<MethodInfo>()
                .ToList();

            var testMethods = _testClassifier.GetTestMethods(allMethods);
            _logger.LogInformation("Found {TestMethodCount} test methods for streaming analysis", testMethods.Count);
            
            // Find actual method IDs that match the user's pattern
				var targetMethodIds = FindMatchingMethodIds(methodId, allMethods);
				_logger.LogDebug("Found {Count} target method IDs matching pattern: {Pattern}", targetMethodIds.Count, methodId);
            // Additional console debug output for ScoreTestsAsync
            if (methodId.Contains("ScoreTestsAsync"))
            {
                System.Console.WriteLine($"DEBUG STREAMING: Found {targetMethodIds.Count} target method IDs:");
                foreach (var id in targetMethodIds)
                {
                    System.Console.WriteLine($"  - {id}");
                }
            }
            
            if (targetMethodIds.Count == 0)
            {
                _logger.LogWarning("No methods found matching pattern: {MethodId}", methodId);
                yield break;
            }

            // Process test methods and yield results as we find them
            foreach (var testMethod in testMethods)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // Skip if this test method is actually one of our target methods
                // This prevents method signatures from being included as "test coverage"
					if (targetMethodIds.Contains(testMethod.Id))
					{
						continue;
					}
                
                TestCoverageInfo? result = null;
                try
                {
                    // Try to find call paths to any of the matching target methods
                    string[]? callPath = null;
                    string? matchedTargetMethodId = null;
                    
					foreach (var targetId in targetMethodIds)
					{
						callPath = FindCallPath(testMethod.Id, targetId, callGraph);
						if (callPath != null && callPath.Any())
						{
							matchedTargetMethodId = targetId;
								break;
							}
							else
							{
								// continue
							}
						}
                    if (callPath != null && callPath.Any() && matchedTargetMethodId != null)
                    {
                        var targetMethodInfo = callGraph.GetMethodInfo(matchedTargetMethodId)!;
                        var confidence = CalculateConfidence(callPath, testMethod, targetMethodInfo);
                        var testType = _testClassifier.ClassifyTestType(testMethod);

						result = new TestCoverageInfo(
							testMethod.Id,
							testMethod.Name,
							testMethod.ContainingType,
							Path.GetFileName(testMethod.FilePath),
							callPath,
							confidence,
							testType);
						}
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to analyze test method: {TestMethodId}", testMethod.Id);
                }
                
					if (result != null)
					{
						yield return result;
					}
            }
        }

        /// <summary>
        /// Fallback method that uses the original full-analysis approach
        /// </summary>
        private async Task<IReadOnlyList<TestCoverageInfo>> FindTestsExercisingMethodFallbackAsync(
            string methodId, 
            string solutionPath, 
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Using fallback analysis for method: {MethodId}", methodId);
            var coverageMap = await BuildTestCoverageMapAsync(solutionPath, cancellationToken);
            return coverageMap.GetTestsForMethodPattern(methodId);
        }

        /// <summary>
        /// Streams test coverage results for multiple methods efficiently
        /// </summary>
        public async IAsyncEnumerable<KeyValuePair<string, TestCoverageInfo>> FindTestsExercisingMethodsStreamAsync(
            IEnumerable<string> methodIds,
            string solutionPath,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var methodIdList = methodIds.ToList();
            _logger.LogInformation("Starting batch streaming analysis for {MethodCount} methods", methodIdList.Count);

            foreach (var methodId in methodIdList)
            {
                await foreach (var coverageInfo in FindTestsExercisingMethodStreamAsync(methodId, solutionPath, cancellationToken))
                {
                    yield return new KeyValuePair<string, TestCoverageInfo>(methodId, coverageInfo);
                }
            }
        }

        /// <summary>
        /// Clear all caches. Call this when the solution or source files change.
        /// </summary>
        public void ClearCaches()
        {
            _cachedCallGraph = null;
            _cachedSolutionPath = null;
            _pathCache.Clear();
            _logger.LogDebug("Cleared all caches");
        }

        /// <summary>
        /// Find all method IDs in the call graph that match the given pattern.
        /// Handles global:: prefix and parameter variations.
        /// </summary>
        private List<string> FindMatchingMethodIds(string pattern, IReadOnlyList<MethodInfo> allMethods)
        {
            var matchingIds = new List<string>();
            
            foreach (var method in allMethods)
            {
                if (IsMethodPatternMatch(method.Id, pattern))
                {
                    matchingIds.Add(method.Id);
                }
            }
            
            _logger.LogDebug("Method pattern resolution: pattern='{Pattern}' matched {Count} methods", pattern, matchingIds.Count);
            return matchingIds;
        }

        /// <summary>
        /// Determines if a method ID matches the given pattern.
        /// Supports pattern matching like the TestCoverageMap.IsMethodMatch method.
        /// </summary>
        private static bool IsMethodPatternMatch(string fullMethodId, string pattern)
        {
            if (string.IsNullOrEmpty(fullMethodId) || string.IsNullOrEmpty(pattern))
                return false;

            // Exact match
            if (fullMethodId.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                return true;

            // Remove global:: prefix if present for comparison
            var normalizedMethodId = fullMethodId.StartsWith("global::", StringComparison.OrdinalIgnoreCase) 
                ? fullMethodId.Substring(8) // Remove "global::" prefix
                : fullMethodId;

            // Extract method name without parameters from normalized ID
            // Format: Namespace.Class.Method(params)
            var parenIndex = normalizedMethodId.IndexOf('(');
            var methodWithoutParams = parenIndex > 0 ? normalizedMethodId.Substring(0, parenIndex) : normalizedMethodId;

            // Check if pattern matches the method without parameters
            if (methodWithoutParams.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                return true;

            // Check if pattern is just the method name (last part after final dot)
            var lastDotIndex = methodWithoutParams.LastIndexOf('.');
            if (lastDotIndex >= 0 && lastDotIndex < methodWithoutParams.Length - 1)
            {
                var methodNameOnly = methodWithoutParams.Substring(lastDotIndex + 1);
                if (methodNameOnly.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
