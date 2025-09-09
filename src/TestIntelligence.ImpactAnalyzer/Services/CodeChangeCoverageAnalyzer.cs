using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Models;
using TestIntelligence.Core.Services;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Models;

namespace TestIntelligence.ImpactAnalyzer.Services
{
    /// <summary>
    /// Implementation of code change coverage analysis.
    /// </summary>
    public class CodeChangeCoverageAnalyzer : ICodeChangeCoverageAnalyzer
    {
        private readonly IGitDiffParser _gitDiffParser;
        private readonly ITestCoverageAnalyzer _testCoverageAnalyzer;
        private readonly ILogger<CodeChangeCoverageAnalyzer> _logger;

        public CodeChangeCoverageAnalyzer(
            IGitDiffParser gitDiffParser,
            ITestCoverageAnalyzer testCoverageAnalyzer,
            ILogger<CodeChangeCoverageAnalyzer> logger)
        {
            _gitDiffParser = gitDiffParser ?? throw new ArgumentNullException(nameof(gitDiffParser));
            _testCoverageAnalyzer = testCoverageAnalyzer ?? throw new ArgumentNullException(nameof(testCoverageAnalyzer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<CodeChangeCoverageResult> AnalyzeCoverageAsync(
            string diffContent,
            IEnumerable<string> testMethodIds,
            string solutionPath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(diffContent))
                throw new ArgumentException("Diff content cannot be null or empty", nameof(diffContent));

            _logger.LogInformation("Parsing git diff content for coverage analysis");
            var codeChanges = await _gitDiffParser.ParseDiffAsync(diffContent);
            
            // Use incremental analysis for better performance on large solutions
            return await AnalyzeCoverageIncrementalInternalAsync(codeChanges, testMethodIds, solutionPath, null, cancellationToken);
        }

        public async Task<CodeChangeCoverageResult> AnalyzeCoverageIncrementalAsync(
            string diffContent,
            IEnumerable<string> testMethodIds,
            string solutionPath,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(diffContent))
                throw new ArgumentException("Diff content cannot be null or empty", nameof(diffContent));

            progress?.Report("Parsing git diff content");
            _logger.LogInformation("Parsing git diff content for incremental coverage analysis");
            var codeChanges = await _gitDiffParser.ParseDiffAsync(diffContent);
            
            return await AnalyzeCoverageIncrementalInternalAsync(codeChanges, testMethodIds, solutionPath, progress, cancellationToken);
        }

        public async Task<CodeChangeCoverageResult> AnalyzeCoverageFromFileAsync(
            string diffFilePath,
            IEnumerable<string> testMethodIds,
            string solutionPath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(diffFilePath))
                throw new ArgumentException("Diff file path cannot be null or empty", nameof(diffFilePath));

            _logger.LogInformation("Parsing git diff file: {DiffFilePath}", diffFilePath);
            var codeChanges = await _gitDiffParser.ParseDiffFileAsync(diffFilePath);
            
            return await AnalyzeCoverageIncrementalInternalAsync(codeChanges, testMethodIds, solutionPath, null, cancellationToken);
        }

        public async Task<CodeChangeCoverageResult> AnalyzeCoverageFromGitCommandAsync(
            string gitCommand,
            IEnumerable<string> testMethodIds,
            string solutionPath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(gitCommand))
                throw new ArgumentException("Git command cannot be null or empty", nameof(gitCommand));

            _logger.LogInformation("Executing git command for coverage analysis: {GitCommand}", gitCommand);
            var codeChanges = await _gitDiffParser.ParseDiffFromCommandAsync(gitCommand);
            
            return await AnalyzeCoverageIncrementalInternalAsync(codeChanges, testMethodIds, solutionPath, null, cancellationToken);
        }

        public async Task<CodeChangeCoverageResult> AnalyzeSingleTestCoverageAsync(
            CodeChangeSet codeChanges,
            string testMethodId,
            string solutionPath,
            CancellationToken cancellationToken = default)
        {
            if (codeChanges == null)
                throw new ArgumentNullException(nameof(codeChanges));
            
            if (string.IsNullOrWhiteSpace(testMethodId))
                throw new ArgumentException("Test method ID cannot be null or empty", nameof(testMethodId));

            return await AnalyzeCoverageIncrementalInternalAsync(
                codeChanges, 
                new[] { testMethodId }, 
                solutionPath,
                null,
                cancellationToken);
        }

        private async Task<CodeChangeCoverageResult> AnalyzeCoverageIncrementalInternalAsync(
            CodeChangeSet codeChanges,
            IEnumerable<string> testMethodIds,
            string solutionPath,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            if (codeChanges == null)
                throw new ArgumentNullException(nameof(codeChanges));
            
            if (testMethodIds == null)
                throw new ArgumentNullException(nameof(testMethodIds));
            
            if (string.IsNullOrWhiteSpace(solutionPath))
                throw new ArgumentException("Solution path cannot be null or empty", nameof(solutionPath));

            var testMethodIdList = testMethodIds.ToList();
            _logger.LogInformation("Analyzing coverage incrementally for {ChangeCount} code changes with {TestCount} tests", 
                codeChanges.Changes.Count, testMethodIdList.Count);

            progress?.Report($"Processing {testMethodIdList.Count} test methods");

            // Get all changed methods first to understand scope
            var changedMethods = codeChanges.GetChangedMethods().ToList();
            _logger.LogInformation("Found {MethodCount} changed methods: {Methods}", 
                changedMethods.Count, string.Join(", ", changedMethods.Take(3)));

            progress?.Report($"Found {changedMethods.Count} changed methods");

            // Use streaming analysis to find coverage relationships more efficiently
            var providedTestCoverage = new List<TestCoverageInfo>();
            var methodCoverageMap = new Dictionary<string, List<TestCoverageInfo>>();
            
            // Optimize lookups with HashSets for O(1) performance instead of O(n) linear searches
            var testMethodIdSet = new HashSet<string>(testMethodIdList, StringComparer.OrdinalIgnoreCase);
            var addedTestIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Track already added tests
            
            // Initialize method coverage map
            foreach (var method in changedMethods)
            {
                methodCoverageMap[method] = new List<TestCoverageInfo>();
            }

            try
            {
                // Use batch lookup for all changed methods to find covering tests (much more efficient)
                progress?.Report("Finding tests that cover changed methods");
                
                // Add timeout protection to prevent infinite call graph analysis
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout for coverage analysis
                
                IReadOnlyDictionary<string, IReadOnlyList<TestCoverageInfo>> coverageResults;
                try
                {
                    // Prefer scoped incremental lookup seeded by changed methods and provided tests
                    if (testMethodIdList.Any())
                    {
                        coverageResults = await _testCoverageAnalyzer.FindTestsExercisingMethodsScopedAsync(
                            changedMethods, testMethodIdList, solutionPath, timeoutCts.Token);
                    }
                    else
                    {
                        coverageResults = await _testCoverageAnalyzer.FindTestsExercisingMethodsAsync(
                            changedMethods, solutionPath, timeoutCts.Token);
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Test coverage analysis timed out after 30 seconds, returning partial results");
                    coverageResults = new Dictionary<string, IReadOnlyList<TestCoverageInfo>>();
                }
                
                foreach (var kvp in coverageResults)
                {
                    var changedMethod = kvp.Key;
                    var coveringTests = kvp.Value;
                    
                    // Check if any of these covering tests match our provided test IDs
                    foreach (var coverageInfo in coveringTests)
                    {
                        // Use O(1) HashSet lookups instead of O(n) Any() operations
                        if (testMethodIdSet.Contains(coverageInfo.TestMethodId) ||
                            testMethodIdSet.Any(testId => 
                                coverageInfo.TestMethodId.EndsWith("." + testId, StringComparison.OrdinalIgnoreCase) ||
                                coverageInfo.TestMethodName.Equals(testId, StringComparison.OrdinalIgnoreCase)))
                        {
                            methodCoverageMap[changedMethod].Add(coverageInfo);
                            
                            // Use O(1) HashSet check instead of O(n) Any() operation for duplicates
                            if (!addedTestIds.Contains(coverageInfo.TestMethodId))
                            {
                                addedTestIds.Add(coverageInfo.TestMethodId);
                                providedTestCoverage.Add(coverageInfo);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze coverage for changed methods");
            }

            progress?.Report("Finalizing coverage analysis");

            // Convert to the expected format
            var finalMethodCoverageMap = new Dictionary<string, IReadOnlyList<TestCoverageInfo>>();
            foreach (var kvp in methodCoverageMap)
            {
                if (kvp.Value.Any())
                {
                    finalMethodCoverageMap[kvp.Key] = kvp.Value.AsReadOnly();
                }
            }

            _logger.LogInformation("Incremental coverage analysis complete: {CoveredMethods}/{TotalMethods} methods covered with {TestCount} test relationships", 
                finalMethodCoverageMap.Count, changedMethods.Count, providedTestCoverage.Count);

            progress?.Report("Coverage analysis complete");

            return new CodeChangeCoverageResult(
                codeChanges,
                providedTestCoverage.AsReadOnly(),
                finalMethodCoverageMap,
                DateTime.UtcNow,
                solutionPath);
        }

        private IEnumerable<TestCoverageInfo> FindTestsCoveringMethod(
            string changedMethodName,
            IReadOnlyList<TestCoverageInfo> providedTestCoverage,
            TestCoverageMap fullCoverageMap)
        {
            // Find all tests that cover this method from the full coverage map
            var allCoveringTests = fullCoverageMap.GetTestsForMethodPattern(changedMethodName);
            
            // Filter to only include tests from our provided set
            var providedTestIds = new HashSet<string>(providedTestCoverage.Select(t => t.TestMethodId), StringComparer.OrdinalIgnoreCase);
            
            var matchingTests = allCoveringTests.Where(test => 
                providedTestIds.Contains(test.TestMethodId)).ToList();

            if (matchingTests.Any())
            {
                _logger.LogDebug("Method '{Method}' covered by {TestCount} provided tests: {Tests}", 
                    changedMethodName, matchingTests.Count, 
                    string.Join(", ", matchingTests.Take(3).Select(t => t.TestMethodName)));
            }
            else
            {
                _logger.LogDebug("Method '{Method}' not covered by any provided tests (but may be covered by other tests)", 
                    changedMethodName);
            }

            return matchingTests;
        }

        /// <summary>
        /// Attempts to find test methods using fuzzy matching when exact matches fail.
        /// Tries different matching strategies including method name only, class.method, etc.
        /// </summary>
        private IReadOnlyList<TestCoverageInfo> FindFuzzyTestMatches(string testId, TestCoverageMap coverageMap)
        {
            var matches = new List<TestCoverageInfo>();
            
            // Strategy 1: Match by method name only (last part after final dot)
            var methodNameOnly = testId.Split('.').LastOrDefault();
            if (!string.IsNullOrEmpty(methodNameOnly))
            {
                var methodNameMatches = coverageMap.MethodToTests.Values
                    .SelectMany(coverage => coverage)
                    .Where(c => c.TestMethodName.Equals(methodNameOnly, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                matches.AddRange(methodNameMatches);
                
                _logger.LogDebug("Strategy 1 (method name '{MethodName}'): Found {MatchCount} matches", 
                    methodNameOnly, methodNameMatches.Count);
            }
            
            // Strategy 2: Match by class.method pattern (last two parts)
            var parts = testId.Split('.');
            if (parts.Length >= 2)
            {
                var classMethod = $"{parts[^2]}.{parts[^1]}";
                var classMethodMatches = coverageMap.MethodToTests.Values
                    .SelectMany(coverage => coverage)
                    .Where(c => c.TestMethodId.EndsWith(classMethod, StringComparison.OrdinalIgnoreCase) ||
                               $"{c.TestClassName.Split('.').LastOrDefault()}.{c.TestMethodName}".Equals(classMethod, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                matches.AddRange(classMethodMatches);
                
                _logger.LogDebug("Strategy 2 (class.method '{ClassMethod}'): Found {MatchCount} matches", 
                    classMethod, classMethodMatches.Count);
            }
            
            // Strategy 3: Enhanced partial matching - look for test method names that end with the provided pattern
            var endPatternMatches = coverageMap.MethodToTests.Values
                .SelectMany(coverage => coverage)
                .Where(c => c.TestMethodName.EndsWith(testId, StringComparison.OrdinalIgnoreCase) ||
                           c.TestMethodId.EndsWith(testId + "()", StringComparison.OrdinalIgnoreCase) ||
                           c.TestMethodId.EndsWith("." + testId + "()", StringComparison.OrdinalIgnoreCase))
                .ToList();
            matches.AddRange(endPatternMatches);
            
            _logger.LogDebug("Strategy 3 (end pattern '{Pattern}'): Found {MatchCount} matches", 
                testId, endPatternMatches.Count);
            
            // Strategy 4: Partial substring matching (more aggressive)
            var substringMatches = coverageMap.MethodToTests.Values
                .SelectMany(coverage => coverage)
                .Where(c => c.TestMethodId.Contains(testId, StringComparison.OrdinalIgnoreCase) ||
                           testId.Contains(c.TestMethodName, StringComparison.OrdinalIgnoreCase) ||
                           c.TestClassName.Contains(testId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            matches.AddRange(substringMatches);
            
            _logger.LogDebug("Strategy 4 (substring '{Pattern}'): Found {MatchCount} matches", 
                testId, substringMatches.Count);
            
            // Remove duplicates and return
            var uniqueMatches = matches.GroupBy(m => m.TestMethodId).Select(g => g.First()).ToList();
            _logger.LogDebug("Total unique fuzzy matches for '{TestId}': {UniqueCount}", testId, uniqueMatches.Count);
            
            return uniqueMatches;
        }
    }
}
