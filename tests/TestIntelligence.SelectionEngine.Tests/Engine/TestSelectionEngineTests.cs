using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Models;
using TestIntelligence.ImpactAnalyzer.Models;
using TestIntelligence.SelectionEngine.Engine;
using TestIntelligence.SelectionEngine.Interfaces;
using TestIntelligence.SelectionEngine.Models;
using Xunit;

namespace TestIntelligence.SelectionEngine.Tests.Engine
{
    public class TestSelectionEngineTests
    {
        private readonly Mock<ILogger<TestSelectionEngine>> _mockLogger;
        private readonly Mock<ITestScoringAlgorithm> _mockScoringAlgorithm;

        public TestSelectionEngineTests()
        {
            _mockLogger = new Mock<ILogger<TestSelectionEngine>>();
            _mockScoringAlgorithm = new Mock<ITestScoringAlgorithm>();
            _mockScoringAlgorithm.Setup(x => x.Name).Returns("Mock Algorithm");
            _mockScoringAlgorithm.Setup(x => x.Weight).Returns(1.0);
            _mockScoringAlgorithm
                .Setup(x => x.CalculateScoreAsync(It.IsAny<TestInfo>(), It.IsAny<TestScoringContext>(), default))
                .ReturnsAsync(0.75);
        }

        [Fact]
        public void Constructor_WithValidParameters_ShouldCreateInstance()
        {
            var engine = new TestSelectionEngine(_mockLogger.Object);

            engine.Should().NotBeNull();
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            Action act = () => new TestSelectionEngine(null!);

            act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
        }

        [Fact]
        public async Task GetTestPlanAsync_WithFastConfidence_ShouldCreatePlan()
        {
            var engine = new TestSelectionEngine(_mockLogger.Object, new[] { _mockScoringAlgorithm.Object });

            var plan = await engine.GetTestPlanAsync(ConfidenceLevel.Fast);

            plan.Should().NotBeNull();
            plan.ConfidenceLevel.Should().Be(ConfidenceLevel.Fast);
            plan.Confidence.Should().Be(0.7);
        }

        [Fact]
        public async Task GetOptimalTestPlanAsync_WithCodeChanges_ShouldCreatePlan()
        {
            var changes = CreateCodeChangeSet();
            var engine = new TestSelectionEngine(_mockLogger.Object, new[] { _mockScoringAlgorithm.Object });

            var plan = await engine.GetOptimalTestPlanAsync(changes, ConfidenceLevel.Medium);

            plan.Should().NotBeNull();
            plan.ConfidenceLevel.Should().Be(ConfidenceLevel.Medium);
            plan.Confidence.Should().Be(0.85);
        }

        [Fact]
        public async Task ScoreTestsAsync_WithCandidateTests_ShouldReturnScoredTests()
        {
            var tests = new[]
            {
                CreateTestInfo("Test1", TestCategory.Unit, TimeSpan.FromMilliseconds(100)),
                CreateTestInfo("Test2", TestCategory.Integration, TimeSpan.FromMilliseconds(500))
            };

            var engine = new TestSelectionEngine(_mockLogger.Object, new[] { _mockScoringAlgorithm.Object });

            var scoredTests = await engine.ScoreTestsAsync(tests);

            scoredTests.Should().HaveCount(2);
            scoredTests.All(t => t.SelectionScore > 0).Should().BeTrue();
            scoredTests.Should().BeInDescendingOrder(t => t.SelectionScore);
        }

        [Fact]
        public async Task UpdateTestExecutionHistoryAsync_WithResults_ShouldUpdateHistory()
        {
            var results = new[]
            {
                new TestExecutionResult(true, TimeSpan.FromMilliseconds(150), DateTimeOffset.UtcNow),
                new TestExecutionResult(false, TimeSpan.FromMilliseconds(200), DateTimeOffset.UtcNow, "Test failed")
            };

            var engine = new TestSelectionEngine(_mockLogger.Object);

            // Should not throw
            await engine.UpdateTestExecutionHistoryAsync(results);

            // Verify logging occurred (simplified verification)
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Updating execution history")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task GetTestHistoryAsync_WithoutFilter_ShouldReturnAllTests()
        {
            var engine = new TestSelectionEngine(_mockLogger.Object);

            var history = await engine.GetTestHistoryAsync();

            history.Should().NotBeNull();
            history.Should().BeEmpty(); // Empty because no tests are registered in mock setup
        }

        [Fact]
        public async Task GetTestHistoryAsync_WithFilter_ShouldReturnFilteredTests()
        {
            var engine = new TestSelectionEngine(_mockLogger.Object);

            var history = await engine.GetTestHistoryAsync("SampleTest");

            history.Should().NotBeNull();
            history.Should().BeEmpty(); // Empty because no tests are registered in mock setup
        }

        [Theory]
        [InlineData(ConfidenceLevel.Fast)]
        [InlineData(ConfidenceLevel.Medium)]
        [InlineData(ConfidenceLevel.High)]
        [InlineData(ConfidenceLevel.Full)]
        public async Task GetTestPlanAsync_WithDifferentConfidenceLevels_ShouldRespectLimits(ConfidenceLevel confidence)
        {
            var engine = new TestSelectionEngine(_mockLogger.Object, new[] { _mockScoringAlgorithm.Object });

            var plan = await engine.GetTestPlanAsync(confidence);

            plan.Should().NotBeNull();
            plan.ConfidenceLevel.Should().Be(confidence);
            plan.Tests.Count.Should().BeLessOrEqualTo(confidence.GetMaxTestCount());
            plan.EstimatedDuration.Should().BeLessOrEqualTo(confidence.GetEstimatedDuration());
        }

        [Fact]
        public async Task GetTestPlanAsync_WithSelectionOptions_ShouldApplyConstraints()
        {
            var options = new TestSelectionOptions
            {
                MaxTestCount = 5,
                MaxExecutionTime = TimeSpan.FromMinutes(2),
                MinSelectionScore = 0.8,
                ExcludedCategories = new HashSet<TestCategory> { TestCategory.UI },
                IncludeFlakyTests = false
            };

            var engine = new TestSelectionEngine(_mockLogger.Object, new[] { _mockScoringAlgorithm.Object });

            var plan = await engine.GetTestPlanAsync(ConfidenceLevel.Medium, options);

            plan.Should().NotBeNull();
            plan.Tests.Count.Should().BeLessOrEqualTo(5);
            plan.EstimatedDuration.Should().BeLessOrEqualTo(TimeSpan.FromMinutes(2));
            plan.Tests.Should().NotContain(t => t.Category == TestCategory.UI);
        }

        private TestInfo CreateTestInfo(string methodName, TestCategory category, TimeSpan executionTime, double score = 0.5)
        {
            var type = typeof(TestSelectionEngineTests);
            var method = type.GetMethod(nameof(SampleTestMethod), BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Sample method not found");

            var testMethod = new TestMethod(method, type, "/test/assembly.dll", FrameworkVersion.Net5Plus);
            return new TestInfo(testMethod, category, executionTime, score);
        }

        private CodeChangeSet CreateCodeChangeSet()
        {
            var changes = new[]
            {
                new CodeChange(
                    "/src/MyClass.cs",
                    CodeChangeType.Modified,
                    new[] { "MyMethod", "AnotherMethod" },
                    new[] { "MyClass" })
            };

            return new CodeChangeSet(changes);
        }

        private void SampleTestMethod()
        {
            // Sample method for reflection
        }
    }
}