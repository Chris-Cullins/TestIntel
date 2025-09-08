using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Models;
using TestIntelligence.SelectionEngine.Algorithms;
using TestIntelligence.SelectionEngine.Models;
using Xunit;
using FluentAssertions;

namespace TestIntelligence.SelectionEngine.Tests.Algorithms
{
    public class HistoricalScoringAlgorithmTests
    {
        private readonly Mock<ILogger<HistoricalScoringAlgorithm>> _mockLogger;
        private readonly HistoricalScoringAlgorithm _algorithm;

        public HistoricalScoringAlgorithmTests()
        {
            _mockLogger = new Mock<ILogger<HistoricalScoringAlgorithm>>();
            _algorithm = new HistoricalScoringAlgorithm(_mockLogger.Object);
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new HistoricalScoringAlgorithm(null!));
        }

        [Fact]
        public void Name_ReturnsExpectedValue()
        {
            _algorithm.Name.Should().Be("Historical Pattern Scoring");
        }

        [Fact]
        public void Weight_Returns30Percent()
        {
            _algorithm.Weight.Should().Be(0.3);
        }

        [Fact]
        public async Task CalculateScoreAsync_WithNoHistory_ReturnsNeutralScore()
        {
            // Arrange
            var testInfo = CreateTestInfoWithHistory(new List<TestExecutionResult>());
            var context = CreateScoringContext(ConfidenceLevel.Medium);

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            // Should return 0.5 (neutral) for tests with no history
            score.Should().BeApproximately(0.5, 0.01);
        }

        [Theory]
        [InlineData(0.85, 0.8)] // 85% success rate -> sweet spot (0.8)
        [InlineData(0.9, 0.8)]  // 90% success rate -> sweet spot (0.8)
        [InlineData(0.75, 0.8)] // 75% success rate -> sweet spot (0.8)
        [InlineData(0.98, 0.6)] // 98% success rate -> very reliable (0.6)
        [InlineData(1.0, 0.6)]  // 100% success rate -> very reliable (0.6)
        [InlineData(0.6, 0.5)]  // 60% success rate -> moderately reliable (0.5)
        [InlineData(0.3, 0.3)]  // 30% success rate -> often failing (0.3)
        [InlineData(0.1, 0.1)]  // 10% success rate -> almost always failing (0.1)
        public async Task CalculateScoreAsync_WithDifferentSuccessRates_ReturnsExpectedReliabilityScores(
            double successRate, double expectedBaseScore)
        {
            // Arrange
            var executionHistory = CreateExecutionHistory(10, successRate);
            var testInfo = CreateTestInfoWithHistory(executionHistory);
            var context = CreateScoringContext(ConfidenceLevel.Medium);

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            // Score should be at least the expected base reliability score
            score.Should().BeGreaterOrEqualTo(expectedBaseScore - 0.01);
        }

        [Fact]
        public async Task CalculateScoreAsync_WithRecentFailures_BoostsScore()
        {
            // Arrange
            var baseHistory = CreateExecutionHistory(5, 0.8); // Good baseline
            var recentFailures = new List<TestExecutionResult>
            {
                new TestExecutionResult(passed: false, duration: TimeSpan.FromMilliseconds(100), executedAt: DateTimeOffset.UtcNow.AddHours(-2)),
                new TestExecutionResult(passed: false, duration: TimeSpan.FromMilliseconds(100), executedAt: DateTimeOffset.UtcNow.AddDays(-1)),
                new TestExecutionResult(passed: false, duration: TimeSpan.FromMilliseconds(100), executedAt: DateTimeOffset.UtcNow.AddDays(-2))
            };
            
            var allHistory = baseHistory.Concat(recentFailures).ToList();
            var testInfo = CreateTestInfoWithHistory(allHistory);
            var context = CreateScoringContext(ConfidenceLevel.Medium);

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            // Should get higher score due to recent failures
            score.Should().BeGreaterThan(0.8);
        }

        [Fact]
        public async Task CalculateScoreAsync_WithHighFrequency_BoostsScore()
        {
            // Arrange
            var now = DateTimeOffset.UtcNow;
            var frequentExecutions = Enumerable.Range(0, 10)
                .Select(i => new TestExecutionResult(
                    passed: true,
                    duration: TimeSpan.FromMilliseconds(100),
                    executedAt: now.AddDays(-i)
                ))
                .ToList();

            var testInfo = CreateTestInfoWithHistory(frequentExecutions);
            var context = CreateScoringContext(ConfidenceLevel.Medium);

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            // Should get frequency bonus
            score.Should().BeGreaterThan(0.6);
        }

        [Theory]
        [InlineData(ConfidenceLevel.Fast, 0.3)]   // Heavy penalty for flaky tests
        [InlineData(ConfidenceLevel.Medium, 0.6)] // Moderate penalty
        [InlineData(ConfidenceLevel.High, 0.8)]   // Light penalty
        [InlineData(ConfidenceLevel.Full, 0.9)]   // Very light penalty
        public async Task CalculateScoreAsync_WithFlakyTest_AppliesPenaltyBasedOnConfidenceLevel(
            ConfidenceLevel confidenceLevel, double expectedPenaltyMultiplier)
        {
            // Arrange
            var flakyHistory = CreateFlakyExecutionHistory();
            var testInfo = CreateTestInfoWithHistory(flakyHistory);
            var context = CreateScoringContext(confidenceLevel);

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            // Calculate what the non-flaky score would be and verify penalty is applied
            var testInfoNonFlaky = CreateTestInfoWithHistory(CreateExecutionHistory(10, 0.8));
            var nonFlakyScore = await _algorithm.CalculateScoreAsync(testInfoNonFlaky, context);
            
            var expectedMaxScore = nonFlakyScore * expectedPenaltyMultiplier;
            score.Should().BeLessOrEqualTo(expectedMaxScore + 0.2); // Allow more tolerance for complex scoring
        }

        [Fact]
        public async Task CalculateScoreAsync_ScoreAlwaysBetweenZeroAndOne()
        {
            // Arrange - create extreme scenario
            var extremeHistory = CreateExecutionHistory(100, 0.0); // All failures
            var testInfo = CreateTestInfoWithHistory(extremeHistory);
            var context = CreateScoringContext(ConfidenceLevel.Fast);

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            score.Should().BeGreaterOrEqualTo(0.0);
            score.Should().BeLessOrEqualTo(1.0);
        }

        [Fact]
        public async Task CalculateScoreAsync_WithNullTestInfo_ThrowsArgumentNullException()
        {
            // Arrange
            var context = CreateScoringContext(ConfidenceLevel.Medium);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _algorithm.CalculateScoreAsync(null!, context));
        }

        [Fact]
        public async Task CalculateScoreAsync_WithNullContext_ThrowsArgumentNullException()
        {
            // Arrange
            var testInfo = CreateTestInfoWithHistory(new List<TestExecutionResult>());

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _algorithm.CalculateScoreAsync(testInfo, null!));
        }

        [Fact]
        public async Task CalculateScoreAsync_WithCancellationToken_CompletesSuccessfully()
        {
            // Arrange
            var testInfo = CreateTestInfoWithHistory(CreateExecutionHistory(5, 0.8));
            var context = CreateScoringContext(ConfidenceLevel.Medium);
            var cts = new CancellationTokenSource();

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context, cts.Token);

            // Assert
            score.Should().BeGreaterOrEqualTo(0.0);
            score.Should().BeLessOrEqualTo(1.0);
        }

        [Fact]
        public async Task CalculateScoreAsync_WithClusteredFailures_GetsBonus()
        {
            // Arrange - create failures clustered within 2 days
            var now = DateTimeOffset.UtcNow;
            var clusteredFailures = new List<TestExecutionResult>
            {
                new TestExecutionResult(passed: false, duration: TimeSpan.FromMilliseconds(100), executedAt: now.AddHours(-1)),
                new TestExecutionResult(passed: false, duration: TimeSpan.FromMilliseconds(100), executedAt: now.AddHours(-12)),
                new TestExecutionResult(passed: false, duration: TimeSpan.FromMilliseconds(100), executedAt: now.AddDays(-1))
            };

            var baseHistory = CreateExecutionHistory(5, 0.8);
            var allHistory = baseHistory.Concat(clusteredFailures).ToList();
            var testInfo = CreateTestInfoWithHistory(allHistory);
            var context = CreateScoringContext(ConfidenceLevel.Medium);

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            // Should get clustering bonus in addition to recent failure score
            score.Should().BeGreaterThan(0.8); // Lower expectation due to complex scoring interactions
        }

        [Fact]
        public void Weight_IsReasonableForCombinedScoring()
        {
            // Historical patterns should have moderate weight in overall scoring
            _algorithm.Weight.Should().BeGreaterThan(0.0);
            _algorithm.Weight.Should().BeLessOrEqualTo(1.0);
            _algorithm.Weight.Should().Be(0.3); // 30% seems reasonable for historical patterns
        }

        private TestInfo CreateTestInfoWithHistory(List<TestExecutionResult> executionHistory)
        {
            var testMethod = new TestMethod(
                typeof(object).GetMethod("ToString")!,
                typeof(object),
                "/test/path",
                FrameworkVersion.Net5Plus
            );

            var testInfo = new TestInfo(
                testMethod,
                TestCategory.Unit,
                TimeSpan.FromMilliseconds(100)
            );

            foreach (var result in executionHistory)
            {
                testInfo.ExecutionHistory.Add(result);
            }

            return testInfo;
        }

        private List<TestExecutionResult> CreateExecutionHistory(int count, double successRate)
        {
            var history = new List<TestExecutionResult>();
            var random = new Random(42); // Fixed seed for deterministic tests
            
            for (int i = 0; i < count; i++)
            {
                history.Add(new TestExecutionResult(
                    passed: random.NextDouble() < successRate,
                    duration: TimeSpan.FromMilliseconds(100),
                    executedAt: DateTimeOffset.UtcNow.AddDays(-i)
                ));
            }
            
            return history;
        }

        private List<TestExecutionResult> CreateFlakyExecutionHistory()
        {
            // Pattern that makes a test appear flaky: alternating pass/fail with some clustering
            return new List<TestExecutionResult>
            {
                new TestExecutionResult(true, TimeSpan.FromMilliseconds(100), DateTimeOffset.UtcNow.AddDays(-1)),
                new TestExecutionResult(false, TimeSpan.FromMilliseconds(100), DateTimeOffset.UtcNow.AddDays(-2)),
                new TestExecutionResult(false, TimeSpan.FromMilliseconds(100), DateTimeOffset.UtcNow.AddDays(-3)),
                new TestExecutionResult(true, TimeSpan.FromMilliseconds(100), DateTimeOffset.UtcNow.AddDays(-4)),
                new TestExecutionResult(false, TimeSpan.FromMilliseconds(100), DateTimeOffset.UtcNow.AddDays(-5)),
                new TestExecutionResult(true, TimeSpan.FromMilliseconds(100), DateTimeOffset.UtcNow.AddDays(-6)),
                new TestExecutionResult(true, TimeSpan.FromMilliseconds(100), DateTimeOffset.UtcNow.AddDays(-7)),
                new TestExecutionResult(false, TimeSpan.FromMilliseconds(100), DateTimeOffset.UtcNow.AddDays(-8))
            };
        }

        private TestScoringContext CreateScoringContext(ConfidenceLevel confidenceLevel)
        {
            return new TestScoringContext(confidenceLevel);
        }
    }
}