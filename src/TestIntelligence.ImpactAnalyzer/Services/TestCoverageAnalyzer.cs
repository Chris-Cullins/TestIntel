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
    /// </summary>
    public class TestCoverageAnalyzer : ITestCoverageAnalyzer
    {
        private readonly IRoslynAnalyzer _roslynAnalyzer;
        private readonly TestMethodClassifier _testClassifier;
        private readonly ILogger<TestCoverageAnalyzer> _logger;

        public TestCoverageAnalyzer(
            IRoslynAnalyzer roslynAnalyzer,
            ILogger<TestCoverageAnalyzer> logger)
        {
            _roslynAnalyzer = roslynAnalyzer ?? throw new ArgumentNullException(nameof(roslynAnalyzer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _testClassifier = new TestMethodClassifier();
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

            _logger.LogInformation("Finding tests exercising method: {MethodId}", methodId);

            var coverageMap = await BuildTestCoverageMapAsync(solutionPath, cancellationToken);
            return coverageMap.GetTestsForMethodPattern(methodId);
        }

        public async Task<TestCoverageMap> BuildTestCoverageMapAsync(
            string solutionPath, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(solutionPath))
                throw new ArgumentException("Solution path cannot be null or empty", nameof(solutionPath));

            _logger.LogInformation("Building test coverage map for solution: {SolutionPath}", solutionPath);

            // Get all source files in the solution
            var sourceFiles = await GetSourceFilesAsync(solutionPath, cancellationToken);
            
            // Build the complete call graph
            var callGraph = await _roslynAnalyzer.BuildCallGraphAsync(sourceFiles, cancellationToken);
            
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
            var methodToTests = new Dictionary<string, List<TestCoverageInfo>>();

            foreach (var method in allMethods)
            {
                if (testMethodIds.Contains(method.Id))
                    continue; // Skip test methods themselves

                var coveringTests = FindTestsCoveringMethod(method, testMethods, callGraph);
                if (coveringTests.Any())
                {
                    methodToTests[method.Id] = coveringTests.ToList();
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
                methodToTests,
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

        public async Task<TestCoverageStatistics> GetCoverageStatisticsAsync(
            string solutionPath,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Calculating coverage statistics for solution: {SolutionPath}", solutionPath);

            var coverageMap = await BuildTestCoverageMapAsync(solutionPath, cancellationToken);
            
            // Get all source files and build call graph to count total methods
            var sourceFiles = await GetSourceFilesAsync(solutionPath, cancellationToken);
            var callGraph = await _roslynAnalyzer.BuildCallGraphAsync(sourceFiles, cancellationToken);
            
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
            var coverageInfos = new List<TestCoverageInfo>();

            foreach (var testMethod in testMethods)
            {
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

            return coverageInfos;
        }

        private string[]? FindCallPath(string testMethodId, string targetMethodId, MethodCallGraph callGraph)
        {
            // Use BFS to find shortest path from test method to target method
            var queue = new Queue<(string methodId, List<string> path)>();
            var visited = new HashSet<string>();

            queue.Enqueue((testMethodId, new List<string> { testMethodId }));
            visited.Add(testMethodId);

            while (queue.Count > 0)
            {
                var (currentMethod, currentPath) = queue.Dequeue();

                // Check if we found the target
                if (currentMethod == targetMethodId)
                {
                    return currentPath.ToArray();
                }

                // Avoid paths that are too long (prevent infinite recursion and overly complex paths)
                if (currentPath.Count >= 10)
                    continue;

                // Explore methods called by the current method
                var calledMethods = callGraph.GetMethodCalls(currentMethod);
                foreach (var calledMethod in calledMethods)
                {
                    if (!visited.Contains(calledMethod))
                    {
                        visited.Add(calledMethod);
                        var newPath = new List<string>(currentPath) { calledMethod };
                        queue.Enqueue((calledMethod, newPath));
                    }
                }
            }

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

        private Task<string[]> GetSourceFilesAsync(string solutionPath, CancellationToken cancellationToken)
        {
            // For now, we'll get all .cs files in the solution directory and subdirectories
            // In a more sophisticated implementation, we'd parse the solution file
            var solutionDir = Path.GetDirectoryName(solutionPath) ?? solutionPath;
            
            if (Directory.Exists(solutionDir))
            {
                var files = Directory.GetFiles(solutionDir, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("bin") && !f.Contains("obj")) // Skip build artifacts
                    .ToArray();
                
                _logger.LogInformation("Found {FileCount} source files in solution", files.Length);
                return Task.FromResult(files);
            }

            _logger.LogWarning("Solution directory not found: {SolutionDir}", solutionDir);
            return Task.FromResult(Array.Empty<string>());
        }
    }
}