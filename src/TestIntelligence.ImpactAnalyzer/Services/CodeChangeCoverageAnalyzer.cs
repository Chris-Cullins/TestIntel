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
            
            // Find all coverage relationships where the test method ID matches our provided tests
            foreach (var kvp in testCoverageMap.MethodToTests)
            {
                var coverageInfos = kvp.Value.Where(coverage => testMethodIdSet.Contains(coverage.TestMethodId));
                providedTestCoverage.AddRange(coverageInfos);
            }

            _logger.LogDebug("Found {CoverageCount} coverage relationships for provided tests", providedTestCoverage.Count);

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
    }
}