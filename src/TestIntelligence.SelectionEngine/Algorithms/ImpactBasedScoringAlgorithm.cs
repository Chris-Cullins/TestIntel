using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.SelectionEngine.Interfaces;
using TestIntelligence.SelectionEngine.Models;

namespace TestIntelligence.SelectionEngine.Algorithms
{
    /// <summary>
    /// Scoring algorithm that prioritizes tests based on code change impact.
    /// </summary>
    public class ImpactBasedScoringAlgorithm : ITestScoringAlgorithm
    {
        private readonly ILogger<ImpactBasedScoringAlgorithm> _logger;

        public ImpactBasedScoringAlgorithm(ILogger<ImpactBasedScoringAlgorithm> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string Name => "Impact-Based Scoring";

        public double Weight => 0.4; // 40% weight in combined scoring

        public Task<double> CalculateScoreAsync(
            TestInfo testInfo, 
            TestScoringContext context, 
            CancellationToken cancellationToken = default)
        {
            if (testInfo == null) throw new ArgumentNullException(nameof(testInfo));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var score = 0.0;

            // If no code changes, use baseline scoring
            if (context.CodeChanges == null || !context.CodeChanges.Changes.Any())
            {
                score = CalculateBaselineImpactScore(testInfo);
            }
            else
            {
                score = CalculateChangeImpactScore(testInfo, context);
            }

            _logger.LogTrace("Impact score for {TestName}: {Score:F3}", 
                testInfo.GetDisplayName(), score);

            return Task.FromResult(score);
        }

        private double CalculateBaselineImpactScore(TestInfo testInfo)
        {
            var score = 0.0;

            // Score based on test category (higher impact categories get higher scores)
            score += testInfo.Category switch
            {
                TestCategory.Unit => 0.3,
                TestCategory.Integration => 0.6,
                TestCategory.Database => 0.4,
                TestCategory.API => 0.7,
                TestCategory.UI => 0.5,
                TestCategory.EndToEnd => 0.8,
                TestCategory.Performance => 0.3,
                TestCategory.Security => 0.9,
                _ => 0.2
            };

            // Boost score for recently failing tests (they might catch regressions)
            var lastResult = testInfo.GetLastExecutionResult();
            if (lastResult != null && !lastResult.Passed)
            {
                var daysSinceFailure = (DateTimeOffset.UtcNow - lastResult.ExecutedAt).TotalDays;
                if (daysSinceFailure < 7) // Within last week
                {
                    score += 0.3;
                }
                else if (daysSinceFailure < 30) // Within last month
                {
                    score += 0.1;
                }
            }

            // Reduce score for flaky tests
            if (testInfo.IsFlaky())
            {
                score *= 0.7;
            }

            return Math.Min(1.0, score);
        }

        private double CalculateChangeImpactScore(TestInfo testInfo, TestScoringContext context)
        {
            var score = 0.0;
            var changes = context.CodeChanges!;

            // Check if test has dependencies that overlap with changed methods/types
            var changedMethods = new HashSet<string>(changes.GetChangedMethods(), StringComparer.OrdinalIgnoreCase);
            var changedTypes = new HashSet<string>(changes.GetChangedTypes(), StringComparer.OrdinalIgnoreCase);

            // Direct dependency match
            var directMatches = testInfo.Dependencies.Count(dep => 
                changedMethods.Contains(dep) || changedTypes.Contains(dep));
            
            if (directMatches > 0)
            {
                score += Math.Min(0.8, directMatches * 0.2); // Up to 0.8 for direct matches
            }

            // Test in same file as changes
            var changedFiles = new HashSet<string>(changes.GetChangedFiles(), StringComparer.OrdinalIgnoreCase);
            var testFilePath = testInfo.TestMethod.AssemblyPath;
            if (changedFiles.Any(file => string.Equals(file, testFilePath, StringComparison.OrdinalIgnoreCase)))
            {
                score += 0.5;
            }

            // Category-based impact scoring
            score += testInfo.Category switch
            {
                TestCategory.Unit => directMatches > 0 ? 0.4 : 0.1,
                TestCategory.Integration => 0.6,
                TestCategory.Database => changes.Changes.Any(c => c.ChangedTypes.Any(t => 
                    t.Contains("Repository") || t.Contains("DbContext") || t.Contains("Entity"))) ? 0.8 : 0.3,
                TestCategory.API => changes.Changes.Any(c => c.ChangedTypes.Any(t => 
                    t.Contains("Controller") || t.Contains("Service"))) ? 0.9 : 0.4,
                TestCategory.UI => changes.Changes.Any(c => c.FilePath.Contains("View") || 
                    c.FilePath.Contains("Component")) ? 0.7 : 0.2,
                TestCategory.EndToEnd => 0.5, // E2E tests are generally relevant to most changes
                TestCategory.Security => changes.Changes.Any(c => c.FilePath.Contains("Auth") || 
                    c.FilePath.Contains("Security")) ? 1.0 : 0.2,
                _ => 0.2
            };

            // Boost for configuration changes
            if (changes.Changes.Any(c => c.ChangeType == ImpactAnalyzer.Models.CodeChangeType.Configuration))
            {
                score += testInfo.Category switch
                {
                    TestCategory.Integration => 0.3,
                    TestCategory.EndToEnd => 0.4,
                    TestCategory.API => 0.3,
                    _ => 0.1
                };
            }

            // Historical success in catching similar changes
            if (HasHistoricalSuccess(testInfo, context))
            {
                score += 0.2;
            }

            return Math.Min(1.0, score);
        }

        private bool HasHistoricalSuccess(TestInfo testInfo, TestScoringContext context)
        {
            // Check if this test has historically failed when similar changes occurred
            // This would require historical data analysis - simplified for now
            var recentFailures = testInfo.ExecutionHistory
                .Where(r => !r.Passed)
                .Where(r => (DateTimeOffset.UtcNow - r.ExecutedAt).TotalDays < 30)
                .Count();

            // If test has failed recently, it might be good at catching similar issues
            return recentFailures > 0 && recentFailures < testInfo.ExecutionHistory.Count * 0.5;
        }
    }
}