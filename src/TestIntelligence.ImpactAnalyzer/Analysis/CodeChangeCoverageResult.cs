using System;
using System.Collections.Generic;
using System.Linq;
using TestIntelligence.Core.Models;
using TestIntelligence.ImpactAnalyzer.Models;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    /// <summary>
    /// Result of analyzing how well specific tests cover code changes from a git diff.
    /// </summary>
    public class CodeChangeCoverageResult
    {
        public CodeChangeCoverageResult(
            CodeChangeSet codeChanges,
            IReadOnlyList<TestCoverageInfo> providedTests,
            IReadOnlyDictionary<string, IReadOnlyList<TestCoverageInfo>> methodCoverage,
            DateTime analyzedAt,
            string solutionPath)
        {
            CodeChanges = codeChanges ?? throw new ArgumentNullException(nameof(codeChanges));
            ProvidedTests = providedTests ?? throw new ArgumentNullException(nameof(providedTests));
            MethodCoverage = methodCoverage ?? throw new ArgumentNullException(nameof(methodCoverage));
            AnalyzedAt = analyzedAt;
            SolutionPath = solutionPath ?? throw new ArgumentNullException(nameof(solutionPath));
            
            CalculateStatistics();
        }

        /// <summary>
        /// The code changes that were analyzed.
        /// </summary>
        public CodeChangeSet CodeChanges { get; }

        /// <summary>
        /// The tests that were checked for coverage.
        /// </summary>
        public IReadOnlyList<TestCoverageInfo> ProvidedTests { get; }

        /// <summary>
        /// Mapping from method identifiers to tests that cover them (from the provided test set).
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<TestCoverageInfo>> MethodCoverage { get; }

        /// <summary>
        /// When this analysis was performed.
        /// </summary>
        public DateTime AnalyzedAt { get; }

        /// <summary>
        /// Path to the solution that was analyzed.
        /// </summary>
        public string SolutionPath { get; }

        /// <summary>
        /// Percentage of changed methods that are covered by the provided tests (0.0 - 100.0).
        /// </summary>
        public double CoveragePercentage { get; private set; }

        /// <summary>
        /// Total number of methods changed in the diff.
        /// </summary>
        public int TotalChangedMethods { get; private set; }

        /// <summary>
        /// Number of changed methods covered by the provided tests.
        /// </summary>
        public int CoveredChangedMethods { get; private set; }

        /// <summary>
        /// Number of changed methods not covered by any of the provided tests.
        /// </summary>
        public int UncoveredChangedMethods { get; private set; }

        /// <summary>
        /// Files that had changes but no test coverage.
        /// </summary>
        public IReadOnlyList<string> UncoveredFiles { get; private set; } = new List<string>();

        /// <summary>
        /// Methods that were changed but have no test coverage.
        /// </summary>
        public IReadOnlyList<string> UncoveredMethods { get; private set; } = new List<string>();

        /// <summary>
        /// Test coverage breakdown by confidence level.
        /// </summary>
        public CoverageByConfidence ConfidenceBreakdown { get; private set; } = new CoverageByConfidence();

        /// <summary>
        /// Test coverage breakdown by test type.
        /// </summary>
        public Dictionary<TestType, int> CoverageByTestType { get; private set; } = new Dictionary<TestType, int>();

        /// <summary>
        /// Recommendations for improving test coverage of the changes.
        /// </summary>
        public IReadOnlyList<CoverageRecommendation> Recommendations { get; private set; } = new List<CoverageRecommendation>();

        private void CalculateStatistics()
        {
            var allChangedMethods = CodeChanges.GetChangedMethods().ToList();
            TotalChangedMethods = allChangedMethods.Count;

            var coveredMethods = new HashSet<string>();
            var confidenceScores = new List<double>();
            var testTypeCounts = new Dictionary<TestType, int>();
            var uncoveredMethods = new List<string>();
            var uncoveredFiles = new HashSet<string>();

            // Analyze coverage for each changed method
            foreach (var method in allChangedMethods)
            {
                var coveringTests = FindCoveringTests(method);
                
                if (coveringTests.Any())
                {
                    coveredMethods.Add(method);
                    
                    // Collect confidence scores and test types
                    foreach (var test in coveringTests)
                    {
                        confidenceScores.Add(test.Confidence);
                        
                        if (!testTypeCounts.ContainsKey(test.TestType))
                            testTypeCounts[test.TestType] = 0;
                        testTypeCounts[test.TestType]++;
                    }
                }
                else
                {
                    uncoveredMethods.Add(method);
                }
            }

            // Identify files with no coverage
            foreach (var change in CodeChanges.Changes)
            {
                var hasAnyCoverage = change.ChangedMethods.Any(method => FindCoveringTests(method).Any());
                if (!hasAnyCoverage)
                {
                    uncoveredFiles.Add(change.FilePath);
                }
            }

            CoveredChangedMethods = coveredMethods.Count;
            UncoveredChangedMethods = TotalChangedMethods - CoveredChangedMethods;
            CoveragePercentage = TotalChangedMethods == 0 ? 100.0 : (double)CoveredChangedMethods / TotalChangedMethods * 100.0;
            
            UncoveredMethods = uncoveredMethods.AsReadOnly();
            UncoveredFiles = uncoveredFiles.ToList().AsReadOnly();
            CoverageByTestType = testTypeCounts;

            // Calculate confidence breakdown
            ConfidenceBreakdown = new CoverageByConfidence(confidenceScores);

            // Generate recommendations
            Recommendations = GenerateRecommendations().AsReadOnly();
        }

        private IReadOnlyList<TestCoverageInfo> FindCoveringTests(string methodName)
        {
            var coveringTests = new List<TestCoverageInfo>();
            
            foreach (var kvp in MethodCoverage)
            {
                var methodId = kvp.Key;
                var tests = kvp.Value;
                
                // Check if this method ID matches the changed method name
                if (IsMethodMatch(methodId, methodName))
                {
                    coveringTests.AddRange(tests);
                }
            }
            
            return coveringTests.AsReadOnly();
        }

        private static bool IsMethodMatch(string fullMethodId, string methodName)
        {
            if (string.IsNullOrEmpty(fullMethodId) || string.IsNullOrEmpty(methodName))
                return false;

            // Exact match
            if (fullMethodId.Equals(methodName, StringComparison.OrdinalIgnoreCase))
                return true;

            // Extract method name from full ID (Namespace.Class.Method)
            var lastDotIndex = fullMethodId.LastIndexOf('.');
            if (lastDotIndex >= 0 && lastDotIndex < fullMethodId.Length - 1)
            {
                var actualMethodName = fullMethodId.Substring(lastDotIndex + 1);
                
                // Remove parameters if present
                var parenIndex = actualMethodName.IndexOf('(');
                if (parenIndex > 0)
                    actualMethodName = actualMethodName.Substring(0, parenIndex);
                
                if (actualMethodName.Equals(methodName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Check if method name is contained in the full ID
            return fullMethodId.IndexOf(methodName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private List<CoverageRecommendation> GenerateRecommendations()
        {
            var recommendations = new List<CoverageRecommendation>();

            // Recommend tests for uncovered methods
            if (UncoveredMethods.Any())
            {
                recommendations.Add(new CoverageRecommendation(
                    CoverageRecommendationType.MissingTests,
                    $"Add tests for {UncoveredMethods.Count} uncovered methods: {string.Join(", ", UncoveredMethods.Take(3))}" +
                    (UncoveredMethods.Count > 3 ? "..." : ""),
                    UncoveredMethods.ToList(),
                    CoverageRecommendationPriority.High));
            }

            // Recommend improving low-confidence coverage
            var lowConfidenceTests = ProvidedTests.Where(t => t.Confidence < 0.6).ToList();
            if (lowConfidenceTests.Any())
            {
                recommendations.Add(new CoverageRecommendation(
                    CoverageRecommendationType.LowConfidence,
                    $"Improve {lowConfidenceTests.Count} tests with low confidence (< 0.6)",
                    lowConfidenceTests.Select(t => t.TestMethodId).ToList(),
                    CoverageRecommendationPriority.Medium));
            }

            // Recommend more direct tests for indirect coverage
            var indirectTests = ProvidedTests.Where(t => t.CallDepth > 3).ToList();
            if (indirectTests.Any())
            {
                recommendations.Add(new CoverageRecommendation(
                    CoverageRecommendationType.IndirectCoverage,
                    $"Consider adding more direct tests - {indirectTests.Count} tests have deep call chains (>3 levels)",
                    indirectTests.Select(t => t.TestMethodId).ToList(),
                    CoverageRecommendationPriority.Low));
            }

            return recommendations;
        }

        public override string ToString()
        {
            return $"Coverage: {CoveragePercentage:F1}% ({CoveredChangedMethods}/{TotalChangedMethods} methods)";
        }
    }

    /// <summary>
    /// Coverage statistics broken down by confidence level.
    /// </summary>
    public class CoverageByConfidence
    {
        public CoverageByConfidence(IReadOnlyList<double>? confidenceScores = null)
        {
            confidenceScores ??= new List<double>();
            
            HighConfidence = confidenceScores.Count(c => c >= 0.8);
            MediumConfidence = confidenceScores.Count(c => c >= 0.5 && c < 0.8);
            LowConfidence = confidenceScores.Count(c => c < 0.5);
            AverageConfidence = confidenceScores.Any() ? confidenceScores.Average() : 0.0;
        }

        public int HighConfidence { get; }
        public int MediumConfidence { get; }
        public int LowConfidence { get; }
        public double AverageConfidence { get; }
    }

    /// <summary>
    /// Recommendation for improving test coverage.
    /// </summary>
    public class CoverageRecommendation
    {
        public CoverageRecommendation(
            CoverageRecommendationType type,
            string description,
            IReadOnlyList<string> affectedItems,
            CoverageRecommendationPriority priority)
        {
            Type = type;
            Description = description ?? throw new ArgumentNullException(nameof(description));
            AffectedItems = affectedItems ?? throw new ArgumentNullException(nameof(affectedItems));
            Priority = priority;
        }

        public CoverageRecommendationType Type { get; }
        public string Description { get; }
        public IReadOnlyList<string> AffectedItems { get; }
        public CoverageRecommendationPriority Priority { get; }
    }

    public enum CoverageRecommendationType
    {
        MissingTests,
        LowConfidence,
        IndirectCoverage,
        TestDuplication
    }

    public enum CoverageRecommendationPriority
    {
        Low,
        Medium,
        High
    }
}