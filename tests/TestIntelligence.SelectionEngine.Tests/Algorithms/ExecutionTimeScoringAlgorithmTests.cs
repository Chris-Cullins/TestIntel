using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using TestIntelligence.Core.Models;
using TestIntelligence.SelectionEngine.Algorithms;
using TestIntelligence.SelectionEngine.Models;
using Xunit;
using FluentAssertions;

namespace TestIntelligence.SelectionEngine.Tests.Algorithms
{
    public class ExecutionTimeScoringAlgorithmTests
    {
        private readonly Mock<ILogger<ExecutionTimeScoringAlgorithm>> _mockLogger;
        private readonly ExecutionTimeScoringAlgorithm _algorithm;

        public ExecutionTimeScoringAlgorithmTests()
        {
            _mockLogger = new Mock<ILogger<ExecutionTimeScoringAlgorithm>>();
            _algorithm = new ExecutionTimeScoringAlgorithm(_mockLogger.Object);
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new ExecutionTimeScoringAlgorithm(null!));
        }

        [Fact]
        public void Name_ReturnsExpectedValue()
        {
            _algorithm.Name.Should().Be("Execution Time Scoring");
        }

        [Fact]
        public void Weight_Returns30Percent()
        {
            _algorithm.Weight.Should().Be(0.3);
        }

        [Theory]
        [InlineData(25, 1.0)]      // Super fast (≤50ms) -> 1.0
        [InlineData(75, 0.9)]      // Very fast (≤100ms) -> 0.9
        [InlineData(200, 0.8)]     // Fast (≤250ms) -> 0.8
        [InlineData(400, 0.7)]     // Medium-fast (≤500ms) -> 0.7
        [InlineData(750, 0.5)]     // Medium (≤1s) -> 0.5
        [InlineData(3000, 0.3)]    // Slow (≤5s) -> 0.3
        [InlineData(10000, 0.2)]   // Very slow (≤15s) -> 0.2
        [InlineData(20000, 0.1)]   // Extremely slow (>15s) -> 0.1
        public async Task CalculateScoreAsync_WithDifferentExecutionTimes_ReturnsExpectedBaseScores(
            int milliseconds, double expectedScore)
        {
            // Arrange
            var testInfo = CreateTestInfo(TimeSpan.FromMilliseconds(milliseconds));
            var context = CreateScoringContext(ConfidenceLevel.Medium);

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            score.Should().BeApproximately(expectedScore, 0.01);
        }

        [Theory]
        [InlineData(ConfidenceLevel.Fast, 100, 0.9)]     // Fast tests boosted by 20%
        [InlineData(ConfidenceLevel.Fast, 600, 0.21)]    // Slow tests penalized to 30% (0.7 * 0.3)
        [InlineData(ConfidenceLevel.Medium, 100, 0.9)]   // No adjustment for medium confidence
        [InlineData(ConfidenceLevel.Medium, 6000, 0.18)] // Penalty for >5s (0.3 * 0.6)
        [InlineData(ConfidenceLevel.High, 100, 0.9)]     // No adjustment for high confidence
        [InlineData(ConfidenceLevel.High, 35000, 0.08)]  // Small penalty for >30s (0.1 * 0.8)
        [InlineData(ConfidenceLevel.Full, 100, 0.81)]    // Slight preference for faster (0.9 * 0.9)
        public async Task CalculateScoreAsync_WithDifferentConfidenceLevels_AppliesCorrectAdjustments(
            ConfidenceLevel confidenceLevel, int milliseconds, double expectedScore)
        {
            // Arrange
            var testInfo = CreateTestInfo(TimeSpan.FromMilliseconds(milliseconds));
            var context = CreateScoringContext(confidenceLevel);

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            score.Should().BeApproximately(expectedScore, 0.01);
        }

        [Fact]
        public async Task CalculateScoreAsync_WithHighVariability_AppliesPenalty()
        {
            // Arrange
            var testInfo = CreateTestInfoWithVariableExecutionTimes(new[]
            {
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(200),
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(300)
            });
            
            var context = CreateScoringContext(ConfidenceLevel.Medium);

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            // Base score for 200ms average would be 0.8, with high variability penalty should be ~0.64
            score.Should().BeLessThan(0.8);
            score.Should().BeGreaterThan(0.6);
        }

        [Fact]
        public async Task CalculateScoreAsync_WithLowVariability_NoViolabilityPenalty()
        {
            // Arrange
            var testInfo = CreateTestInfoWithVariableExecutionTimes(new[]
            {
                TimeSpan.FromMilliseconds(95),
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(105),
                TimeSpan.FromMilliseconds(98),
                TimeSpan.FromMilliseconds(102)
            });
            
            var context = CreateScoringContext(ConfidenceLevel.Medium);

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            // Base score for 100ms average should be 0.9, no variability penalty
            score.Should().BeApproximately(0.9, 0.01);
        }

        [Fact]
        public async Task CalculateScoreAsync_WithInsufficientHistory_NoVariabilityPenalty()
        {
            // Arrange
            var testInfo = CreateTestInfoWithVariableExecutionTimes(new[]
            {
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(500) // Only 2 history items
            });
            
            var context = CreateScoringContext(ConfidenceLevel.Medium);

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            // Should use average (300ms) for base score, no variability penalty
            score.Should().BeApproximately(0.3, 0.05); // ~0.3 for 300ms
        }

        [Fact]
        public async Task CalculateScoreAsync_ScoreAlwaysBetweenZeroAndOne()
        {
            // Arrange - extremely slow test
            var testInfo = CreateTestInfo(TimeSpan.FromMinutes(5));
            var context = CreateScoringContext(ConfidenceLevel.Fast); // Maximum penalty

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
            var testInfo = CreateTestInfo(TimeSpan.FromMilliseconds(100));

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _algorithm.CalculateScoreAsync(testInfo, null!));
        }

        [Fact]
        public async Task CalculateScoreAsync_WithCancellationToken_CompletesSuccessfully()
        {
            // Arrange
            var testInfo = CreateTestInfo(TimeSpan.FromMilliseconds(100));
            var context = CreateScoringContext(ConfidenceLevel.Medium);
            var cts = new CancellationTokenSource();

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context, cts.Token);

            // Assert
            score.Should().BeApproximately(0.9, 0.01);
        }

        [Fact]
        public void Weight_IsReasonableForCombinedScoring()
        {
            // Execution time should have moderate weight in overall scoring
            _algorithm.Weight.Should().BeGreaterThan(0.0);
            _algorithm.Weight.Should().BeLessOrEqualTo(1.0);
            _algorithm.Weight.Should().Be(0.3); // 30% seems reasonable for execution time
        }

        private TestInfo CreateTestInfo(TimeSpan averageExecutionTime)
        {
            var testMethod = new TestMethod(
                "TestClass",
                "TestMethod",
                "/test/path",
                FrameworkVersion.Net5Plus,
                "TestAssembly"
            );

            return new TestInfo(
                testMethod,
                TestCategory.Unit,
                averageExecutionTime
            );
        }

        private TestInfo CreateTestInfoWithVariableExecutionTimes(TimeSpan[] executionTimes)
        {
            var testMethod = new TestMethod(
                "TestClass",
                "TestMethod",
                "/test/path",
                FrameworkVersion.Net5Plus,
                "TestAssembly"
            );

            var averageTime = TimeSpan.FromMilliseconds(
                executionTimes.Select(t => t.TotalMilliseconds).Average()
            );

            var testInfo = new TestInfo(testMethod, TestCategory.Unit, averageTime);

            // Add execution history
            foreach (var time in executionTimes)
            {
                testInfo.ExecutionHistory.Add(new TestExecutionResult
                {
                    Duration = time,
                    Passed = true,
                    ExecutedAt = DateTimeOffset.UtcNow
                });
            }

            return testInfo;
        }

        private TestScoringContext CreateScoringContext(ConfidenceLevel confidenceLevel)
        {
            return new TestScoringContext(confidenceLevel);
        }
    }
}