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
            
            return await AnalyzeCoverageInternalAsync(codeChanges, testMethodIds, solutionPath, cancellationToken);
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
            
            return await AnalyzeCoverageInternalAsync(codeChanges, testMethodIds, solutionPath, cancellationToken);
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
            
            return await AnalyzeCoverageInternalAsync(codeChanges, testMethodIds, solutionPath, cancellationToken);
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

            return await AnalyzeCoverageInternalAsync(
                codeChanges, 
                new[] { testMethodId }, 
                solutionPath, 
                cancellationToken);
        }

        private async Task<CodeChangeCoverageResult> AnalyzeCoverageInternalAsync(
            CodeChangeSet codeChanges,
            IEnumerable<string> testMethodIds,
            string solutionPath,
            CancellationToken cancellationToken)
        {
            if (codeChanges == null)
                throw new ArgumentNullException(nameof(codeChanges));
            
            if (testMethodIds == null)
                throw new ArgumentNullException(nameof(testMethodIds));
            
            if (string.IsNullOrWhiteSpace(solutionPath))
                throw new ArgumentException("Solution path cannot be null or empty", nameof(solutionPath));

            var testMethodIdList = testMethodIds.ToList();
            _logger.LogInformation("Analyzing coverage for {ChangeCount} code changes with {TestCount} tests", 
                codeChanges.Changes.Count, testMethodIdList.Count);

            // Build complete test coverage map for the solution
            _logger.LogDebug("Building test coverage map for solution");
            var testCoverageMap = await _testCoverageAnalyzer.BuildTestCoverageMapAsync(solutionPath, cancellationToken);

            // Get coverage information for all provided test methods
            var providedTestCoverage = new List<TestCoverageInfo>();
            var testMethodIdSet = new HashSet<string>(testMethodIdList, StringComparer.OrdinalIgnoreCase);
            
            // Debug: Log what test IDs we're looking for
            _logger.LogInformation("Looking for test method IDs: {TestIds}", string.Join(", ", testMethodIdList));
            
            // Debug: Sample test method IDs from the coverage map, try to get diverse samples
            var allTestIds = testCoverageMap.MethodToTests.Values
                .SelectMany(coverage => coverage.Select(c => c.TestMethodId))
                .Distinct()
                .ToList();
            
            _logger.LogInformation("Total test methods in coverage map: {TotalCount}", allTestIds.Count);
            
            // Group by namespace/project and show samples from each
            var testsByProject = allTestIds
                .GroupBy(id => id.Split('.')[1]) // Get project name part
                .ToDictionary(g => g.Key, g => g.ToList());
            
            _logger.LogInformation("Tests discovered by project:");
            foreach (var project in testsByProject.Take(5)) // Show up to 5 projects
            {
                var sampleFromProject = project.Value.Take(3).ToList();
                _logger.LogInformation("  {ProjectName}: {TestCount} tests, samples: {Samples}", 
                    project.Key, project.Value.Count, string.Join(", ", sampleFromProject));
            }
            
            // Show overall sample
            var sampleTestIds = allTestIds.Take(15).ToList();
            _logger.LogInformation("Sample test method IDs from coverage map: {SampleIds}", string.Join(", ", sampleTestIds));
            
            // Find all coverage relationships where the test method ID matches our provided tests
            var exactMatches = new HashSet<string>();
            var fuzzyMatches = new List<TestCoverageInfo>();
            
            foreach (var kvp in testCoverageMap.MethodToTests)
            {
                var coverageInfos = kvp.Value.Where(coverage => testMethodIdSet.Contains(coverage.TestMethodId));
                providedTestCoverage.AddRange(coverageInfos);
                
                foreach (var coverage in coverageInfos)
                {
                    exactMatches.Add(coverage.TestMethodId);
                }
            }
            
            // If no exact matches found, try fuzzy matching
            if (!exactMatches.Any())
            {
                _logger.LogWarning("No exact test method ID matches found, attempting fuzzy matching");
                
                foreach (var testId in testMethodIdList)
                {
                    var fuzzyMatchesForTest = FindFuzzyTestMatches(testId, testCoverageMap);
                    fuzzyMatches.AddRange(fuzzyMatchesForTest);
                    
                    if (fuzzyMatchesForTest.Any())
                    {
                        _logger.LogInformation("Found fuzzy matches for '{TestId}': {FuzzyMatches}", 
                            testId, string.Join(", ", fuzzyMatchesForTest.Select(p => p.TestMethodId)));
                    }
                    else
                    {
                        _logger.LogWarning("No matches (exact or fuzzy) found for test: '{TestId}'", testId);
                    }
                }
                
                // Use fuzzy matches if no exact matches were found
                providedTestCoverage.AddRange(fuzzyMatches);
            }
            else
            {
                _logger.LogInformation("Found exact matches for test IDs: {ExactMatches}", string.Join(", ", exactMatches));
            }

            _logger.LogInformation("Found {CoverageCount} coverage relationships for provided tests", providedTestCoverage.Count);

            // Get all changed methods from the code changes
            var changedMethods = codeChanges.GetChangedMethods().ToList();
            _logger.LogDebug("Analyzing {MethodCount} changed methods: {Methods}", 
                changedMethods.Count, string.Join(", ", changedMethods.Take(5)));

            // Find which tests (from our provided set) cover the changed methods
            var methodCoverageMap = new Dictionary<string, IReadOnlyList<TestCoverageInfo>>();
            
            // For each changed method, find which of our provided tests cover it
            foreach (var changedMethod in changedMethods)
            {
                var coveringTests = FindTestsCoveringMethod(changedMethod, providedTestCoverage, testCoverageMap);
                if (coveringTests.Any())
                {
                    methodCoverageMap[changedMethod] = coveringTests.ToList();
                }
            }

            _logger.LogInformation("Coverage analysis complete: {CoveredMethods}/{TotalMethods} methods covered", 
                methodCoverageMap.Count, changedMethods.Count);

            return new CodeChangeCoverageResult(
                codeChanges,
                providedTestCoverage.AsReadOnly(),
                methodCoverageMap,
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