using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using TestIntelligence.Core.Models;
using TestIntelligence.SelectionEngine.Algorithms;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.ImpactAnalyzer.Models;
using Xunit;
using FluentAssertions;

namespace TestIntelligence.SelectionEngine.Tests.Algorithms
{
    public class ImpactBasedScoringAlgorithmTests
    {
        private readonly Mock<ILogger<ImpactBasedScoringAlgorithm>> _mockLogger;
        private readonly ImpactBasedScoringAlgorithm _algorithm;

        public ImpactBasedScoringAlgorithmTests()
        {
            _mockLogger = new Mock<ILogger<ImpactBasedScoringAlgorithm>>();
            _algorithm = new ImpactBasedScoringAlgorithm(_mockLogger.Object);
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new ImpactBasedScoringAlgorithm(null!));
        }

        [Fact]
        public void Name_ReturnsExpectedValue()
        {
            _algorithm.Name.Should().Be("Impact-Based Scoring");
        }

        [Fact]
        public void Weight_Returns40Percent()
        {
            _algorithm.Weight.Should().Be(0.4);
        }

        [Theory]
        [InlineData(TestCategory.Unit, 0.3)]
        [InlineData(TestCategory.Integration, 0.6)]
        [InlineData(TestCategory.Database, 0.4)]
        [InlineData(TestCategory.API, 0.7)]
        [InlineData(TestCategory.UI, 0.5)]
        [InlineData(TestCategory.EndToEnd, 0.8)]
        [InlineData(TestCategory.Performance, 0.3)]
        [InlineData(TestCategory.Security, 0.9)]
        public async Task CalculateScoreAsync_WithNoCodeChanges_ReturnsBaselineCategoryScores(
            TestCategory category, double expectedBaseScore)
        {
            // Arrange
            var testInfo = CreateTestInfo(category);
            var context = CreateScoringContextWithoutChanges();

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            score.Should().BeApproximately(expectedBaseScore, 0.01);
        }

        [Fact]
        public async Task CalculateScoreAsync_WithRecentFailure_BoostsBaselineScore()
        {
            // Arrange
            var testInfo = CreateTestInfo(TestCategory.Unit);
            testInfo.ExecutionHistory.Add(new TestExecutionResult
            {
                Passed = false,
                ExecutedAt = DateTimeOffset.UtcNow.AddDays(-3) // Recent failure
            });
            
            var context = CreateScoringContextWithoutChanges();

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            // Should get base score (0.3) + recent failure boost (0.3) = 0.6
            score.Should().BeApproximately(0.6, 0.01);
        }

        [Fact]
        public async Task CalculateScoreAsync_WithOldFailure_GetsSmallBoost()
        {
            // Arrange
            var testInfo = CreateTestInfo(TestCategory.Unit);
            testInfo.ExecutionHistory.Add(new TestExecutionResult
            {
                Passed = false,
                ExecutedAt = DateTimeOffset.UtcNow.AddDays(-20) // Old failure (within month)
            });
            
            var context = CreateScoringContextWithoutChanges();

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            // Should get base score (0.3) + old failure boost (0.1) = 0.4
            score.Should().BeApproximately(0.4, 0.01);
        }

        [Fact]
        public async Task CalculateScoreAsync_WithFlakyTest_AppliesPenalty()
        {
            // Arrange
            var testInfo = CreateFlakyTestInfo(TestCategory.Integration);
            var context = CreateScoringContextWithoutChanges();

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            // Should get base score (0.6) with flaky penalty (0.6 * 0.7) = 0.42
            score.Should().BeApproximately(0.42, 0.01);
        }

        [Fact]
        public async Task CalculateScoreAsync_WithDirectDependencyMatch_GetsHighScore()
        {
            // Arrange
            var testInfo = CreateTestInfo(TestCategory.Unit);
            testInfo.Dependencies.Add("MyNamespace.MyClass.MyMethod");
            
            var changes = CreateCodeChanges(new[] { "MyNamespace.MyClass.MyMethod" }, new[] { "MyNamespace.MyClass" });
            var context = CreateScoringContext(changes);

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            // Should get high score due to direct dependency match
            score.Should().BeGreaterThan(0.8);
        }

        [Fact]
        public async Task CalculateScoreAsync_WithUnitTestDirectMatch_GetsExtraBoost()
        {
            // Arrange
            var testInfo = CreateTestInfo(TestCategory.Unit);
            testInfo.Dependencies.Add("MyNamespace.MyClass.MyMethod");
            
            var changes = CreateCodeChanges(new[] { "MyNamespace.MyClass.MyMethod" }, new[] { "MyNamespace.MyClass" });
            var context = CreateScoringContext(changes);

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            // Should get high score with unit test boost
            score.Should().BeGreaterThan(0.9);
        }

        [Fact]
        public async Task CalculateScoreAsync_WithNameMatch_GetsHighScore()
        {
            // Arrange
            var testMethod = new TestMethod(
                "MyClass", // Test class name matches changed type
                "TestMyMethod",
                "/test/path",
                FrameworkVersion.Net5Plus,
                "TestAssembly"
            );
            
            var testInfo = new TestInfo(testMethod, TestCategory.Unit, TimeSpan.FromMilliseconds(100));
            
            var changes = CreateCodeChanges(Array.Empty<string>(), new[] { "MyNamespace.MyClass" });
            var context = CreateScoringContext(changes);

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            // Should get high score due to name match
            score.Should().BeGreaterThan(0.8);
        }

        [Theory]
        [InlineData(TestCategory.Database, "UserRepository", 0.7)]
        [InlineData(TestCategory.Database, "SomeOtherClass", 0.2)]
        [InlineData(TestCategory.API, "UserController", 0.8)]
        [InlineData(TestCategory.API, "SomeOtherClass", 0.3)]
        [InlineData(TestCategory.Security, "AuthService", 1.0)]
        [InlineData(TestCategory.Security, "SomeOtherClass", 0.1)]
        public async Task CalculateScoreAsync_WithCategorySpecificChanges_ReturnsAppropriateScores(
            TestCategory category, string changedType, double expectedMinScore)
        {
            // Arrange
            var testInfo = CreateTestInfo(category);
            var changes = CreateCodeChanges(Array.Empty<string>(), new[] { changedType });
            var context = CreateScoringContext(changes);

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            score.Should().BeGreaterOrEqualTo(expectedMinScore - 0.1);
        }

        [Fact]
        public async Task CalculateScoreAsync_WithConfigurationChange_BoostsIntegrationTests()
        {
            // Arrange
            var testInfo = CreateTestInfo(TestCategory.Integration);
            var changes = CreateCodeChangeSet(new[]
            {
                new CodeChange
                {
                    FilePath = "appsettings.json",
                    ChangeType = CodeChangeType.Configuration,
                    ChangedTypes = Array.Empty<string>(),
                    ChangedMethods = Array.Empty<string>()
                }
            });
            var context = CreateScoringContext(changes);

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            // Should get integration base score + config boost
            score.Should().BeGreaterThan(0.6);
        }

        [Fact]
        public async Task CalculateScoreAsync_WithHistoricalSuccess_GetsBonus()
        {
            // Arrange
            var testInfo = CreateTestInfo(TestCategory.Unit);
            
            // Add some historical failures (but not too many to indicate flakiness)
            testInfo.ExecutionHistory.AddRange(new[]
            {
                new TestExecutionResult { Passed = false, ExecutedAt = DateTimeOffset.UtcNow.AddDays(-15) },
                new TestExecutionResult { Passed = true, ExecutedAt = DateTimeOffset.UtcNow.AddDays(-16) },
                new TestExecutionResult { Passed = true, ExecutedAt = DateTimeOffset.UtcNow.AddDays(-17) },
                new TestExecutionResult { Passed = true, ExecutedAt = DateTimeOffset.UtcNow.AddDays(-18) }
            });
            
            var changes = CreateCodeChanges(new[] { "SomeMethod" }, new[] { "SomeClass" });
            var context = CreateScoringContext(changes);

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            // Should get historical success bonus
            score.Should().BeGreaterThan(0.2); // Base score + some boost
        }

        [Fact]
        public async Task CalculateScoreAsync_ScoreNeverExceedsOne()
        {
            // Arrange - create scenario that could potentially exceed 1.0
            var testInfo = CreateTestInfo(TestCategory.Security);
            testInfo.Dependencies.AddRange(new[] { "Method1", "Method2", "Method3" });
            
            var changes = CreateCodeChanges(
                new[] { "Method1", "Method2", "Method3" }, 
                new[] { "AuthService", "SecurityService" }
            );
            var context = CreateScoringContext(changes);

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context);

            // Assert
            score.Should().BeLessOrEqualTo(1.0);
        }

        [Fact]
        public async Task CalculateScoreAsync_WithNullTestInfo_ThrowsArgumentNullException()
        {
            // Arrange
            var context = CreateScoringContextWithoutChanges();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _algorithm.CalculateScoreAsync(null!, context));
        }

        [Fact]
        public async Task CalculateScoreAsync_WithNullContext_ThrowsArgumentNullException()
        {
            // Arrange
            var testInfo = CreateTestInfo(TestCategory.Unit);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _algorithm.CalculateScoreAsync(testInfo, null!));
        }

        [Fact]
        public async Task CalculateScoreAsync_WithCancellationToken_CompletesSuccessfully()
        {
            // Arrange
            var testInfo = CreateTestInfo(TestCategory.Unit);
            var context = CreateScoringContextWithoutChanges();
            var cts = new CancellationTokenSource();

            // Act
            var score = await _algorithm.CalculateScoreAsync(testInfo, context, cts.Token);

            // Assert
            score.Should().BeGreaterOrEqualTo(0.0);
            score.Should().BeLessOrEqualTo(1.0);
        }

        [Fact]
        public void Weight_IsHighestInCombinedScoring()
        {
            // Impact should have highest weight since it's most relevant for targeted testing
            _algorithm.Weight.Should().BeGreaterThan(0.3); // Higher than execution time and historical
            _algorithm.Weight.Should().Be(0.4); // 40% seems appropriate for impact-based scoring
        }

        private TestInfo CreateTestInfo(TestCategory category)
        {
            var testMethod = new TestMethod(
                "TestClass",
                "TestMethod",
                "/test/path",
                FrameworkVersion.Net5Plus,
                "TestAssembly"
            );

            return new TestInfo(testMethod, category, TimeSpan.FromMilliseconds(100));
        }

        private TestInfo CreateFlakyTestInfo(TestCategory category)
        {
            var testInfo = CreateTestInfo(category);
            
            // Add execution history that makes it appear flaky
            testInfo.ExecutionHistory.AddRange(new[]
            {
                new TestExecutionResult { Passed = true, ExecutedAt = DateTimeOffset.UtcNow.AddDays(-1) },
                new TestExecutionResult { Passed = false, ExecutedAt = DateTimeOffset.UtcNow.AddDays(-2) },
                new TestExecutionResult { Passed = true, ExecutedAt = DateTimeOffset.UtcNow.AddDays(-3) },
                new TestExecutionResult { Passed = false, ExecutedAt = DateTimeOffset.UtcNow.AddDays(-4) }
            });
            
            return testInfo;
        }

        private TestScoringContext CreateScoringContextWithoutChanges()
        {
            return new TestScoringContext(ConfidenceLevel.Medium);
        }

        private TestScoringContext CreateScoringContext(CodeChangeSet codeChanges)
        {
            return new TestScoringContext(ConfidenceLevel.Medium, codeChanges);
        }

        private CodeChangeSet CreateCodeChanges(string[] changedMethods, string[] changedTypes)
        {
            var changes = new List<CodeChange>
            {
                new CodeChange
                {
                    FilePath = "/some/path/File.cs",
                    ChangeType = CodeChangeType.Modified,
                    ChangedMethods = changedMethods,
                    ChangedTypes = changedTypes
                }
            };

            return CreateCodeChangeSet(changes);
        }

        private CodeChangeSet CreateCodeChangeSet(IEnumerable<CodeChange> changes)
        {
            return new CodeChangeSet(changes, DateTimeOffset.UtcNow);
        }
    }
}