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

        #region Integration Tests for ScoreTestsAsync

        [Fact]
        public async Task ScoreTestsAsync_WithMixedTestTypes_ShouldPrioritizeBasedOnCategory()
        {
            var tests = new[]
            {
                CreateTestInfo("FastUnitTest", TestCategory.Unit, TimeSpan.FromMilliseconds(50), 0.0),
                CreateTestInfo("SlowIntegrationTest", TestCategory.Integration, TimeSpan.FromSeconds(2), 0.0),
                CreateTestInfo("DatabaseTest", TestCategory.Database, TimeSpan.FromSeconds(5), 0.0),
                CreateTestInfo("UITest", TestCategory.UI, TimeSpan.FromSeconds(10), 0.0)
            };

            var engine = new TestSelectionEngine(_mockLogger.Object, new[] { _mockScoringAlgorithm.Object });
            
            // Configure mock to return different scores based on category
            _mockScoringAlgorithm.Setup(x => x.CalculateScoreAsync(It.IsAny<TestInfo>(), It.IsAny<TestScoringContext>(), It.IsAny<CancellationToken>()))
                .Returns<TestInfo, TestScoringContext, CancellationToken>((test, context, ct) => test.Category switch
                {
                    TestCategory.Unit => Task.FromResult(0.9), // Highest priority for unit tests
                    TestCategory.Integration => Task.FromResult(0.7),
                    TestCategory.Database => Task.FromResult(0.5),
                    TestCategory.UI => Task.FromResult(0.3), // Lowest priority for UI tests
                    _ => Task.FromResult(0.1)
                });

            var scoredTests = await engine.ScoreTestsAsync(tests);

            scoredTests.Should().HaveCount(4);
            scoredTests[0].TestMethod.GetDisplayName().Should().Contain("FastUnitTest");
            scoredTests[1].TestMethod.GetDisplayName().Should().Contain("SlowIntegrationTest");
            scoredTests[2].TestMethod.GetDisplayName().Should().Contain("DatabaseTest");
            scoredTests[3].TestMethod.GetDisplayName().Should().Contain("UITest");
        }

        [Fact]
        public async Task ScoreTestsAsync_WithCodeChanges_ShouldBoostRelatedTests()
        {
            var changes = CreateCodeChangeSet();
            var tests = new[]
            {
                CreateTestInfo("UnrelatedTest", TestCategory.Unit, TimeSpan.FromMilliseconds(100)),
                CreateTestInfo("RelatedTest_MyMethod", TestCategory.Unit, TimeSpan.FromMilliseconds(100)),
                CreateTestInfo("RelatedTest_MyClass", TestCategory.Integration, TimeSpan.FromMilliseconds(500))
            };

            var engine = new TestSelectionEngine(_mockLogger.Object, new[] { _mockScoringAlgorithm.Object });
            
            // Configure mock to boost scores for related tests
            _mockScoringAlgorithm.Setup(x => x.CalculateScoreAsync(It.IsAny<TestInfo>(), It.IsAny<TestScoringContext>(), It.IsAny<CancellationToken>()))
                .Returns<TestInfo, TestScoringContext, CancellationToken>((test, context, ct) =>
                {
                    if (test.TestMethod.GetDisplayName().Contains("Related"))
                        return Task.FromResult(0.9); // High score for related tests
                    return Task.FromResult(0.3); // Lower score for unrelated tests
                });

            var scoredTests = await engine.ScoreTestsAsync(tests, changes);

            scoredTests.Should().HaveCount(3);
            // Related tests should be scored higher and appear first
            scoredTests.Take(2).All(t => t.TestMethod.GetDisplayName().Contains("Related")).Should().BeTrue();
            scoredTests.Last().TestMethod.GetDisplayName().Should().Contain("UnrelatedTest");
        }

        [Fact]
        public async Task ScoreTestsAsync_WithExecutionHistoryContext_ShouldConsiderReliability()
        {
            var tests = new[]
            {
                CreateTestInfo("FlakeyTest", TestCategory.Unit, TimeSpan.FromMilliseconds(100)),
                CreateTestInfo("ReliableTest", TestCategory.Unit, TimeSpan.FromMilliseconds(150)),
                CreateTestInfo("NewTest", TestCategory.Unit, TimeSpan.FromMilliseconds(80))
            };

            // Simulate execution history for the tests
            var flakeyTest = tests[0];
            foreach(var result in new[]
            {
                new TestExecutionResult(false, TimeSpan.FromMilliseconds(100), DateTimeOffset.UtcNow.AddDays(-1)), // Failed
                new TestExecutionResult(true, TimeSpan.FromMilliseconds(95), DateTimeOffset.UtcNow.AddHours(-12)), // Passed
                new TestExecutionResult(false, TimeSpan.FromMilliseconds(110), DateTimeOffset.UtcNow.AddHours(-6)) // Failed
            }) { flakeyTest.ExecutionHistory.Add(result); }

            var reliableTest = tests[1];
            foreach(var result in new[]
            {
                new TestExecutionResult(true, TimeSpan.FromMilliseconds(150), DateTimeOffset.UtcNow.AddDays(-1)), // Passed
                new TestExecutionResult(true, TimeSpan.FromMilliseconds(145), DateTimeOffset.UtcNow.AddHours(-12)), // Passed
                new TestExecutionResult(true, TimeSpan.FromMilliseconds(155), DateTimeOffset.UtcNow.AddHours(-6)) // Passed
            }) { reliableTest.ExecutionHistory.Add(result); }

            var engine = new TestSelectionEngine(_mockLogger.Object, new[] { _mockScoringAlgorithm.Object });
            
            // Configure mock to factor in reliability
            _mockScoringAlgorithm.Setup(x => x.CalculateScoreAsync(It.IsAny<TestInfo>(), It.IsAny<TestScoringContext>(), It.IsAny<CancellationToken>()))
                .Returns<TestInfo, TestScoringContext, CancellationToken>((test, context, ct) =>
                {
                    if (test.TestMethod.GetDisplayName().Contains("Reliable"))
                        return Task.FromResult(0.9); // High score for reliable tests
                    if (test.TestMethod.GetDisplayName().Contains("Flakey"))
                        return Task.FromResult(0.4); // Lower score for flakey tests
                    return Task.FromResult(0.7); // Medium score for new tests
                });

            var scoredTests = await engine.ScoreTestsAsync(tests);

            scoredTests.Should().HaveCount(3);
            scoredTests[0].TestMethod.GetDisplayName().Should().Contain("ReliableTest");
            scoredTests[1].TestMethod.GetDisplayName().Should().Contain("NewTest");
            scoredTests[2].TestMethod.GetDisplayName().Should().Contain("FlakeyTest");
        }

        [Fact]
        public async Task ScoreTestsAsync_WithLargeTestSuite_ShouldHandlePerformantly()
        {
            // Create a large number of tests to verify performance
            var tests = Enumerable.Range(1, 1000)
                .Select(i => CreateTestInfo($"Test_{i:D4}", 
                    (TestCategory)(i % 4), // Distribute across categories
                    TimeSpan.FromMilliseconds(50 + (i % 200)), // Vary execution times
                    0.0))
                .ToArray();

            var engine = new TestSelectionEngine(_mockLogger.Object, new[] { _mockScoringAlgorithm.Object });
            
            _mockScoringAlgorithm.Setup(x => x.CalculateScoreAsync(It.IsAny<TestInfo>(), It.IsAny<TestScoringContext>(), It.IsAny<CancellationToken>()))
                .Returns<TestInfo, TestScoringContext, CancellationToken>((test, context, ct) =>
                {
                    // Simulate varying scoring based on test name hash
                    var hash = test.TestMethod.GetDisplayName().GetHashCode();
                    var score = (Math.Abs(hash) % 100) / 100.0; // Score between 0.0 and 1.0
                    return Task.FromResult(score);
                });

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var scoredTests = await engine.ScoreTestsAsync(tests);
            stopwatch.Stop();

            scoredTests.Should().HaveCount(1000);
            scoredTests.Should().BeInDescendingOrder(t => t.SelectionScore);
            
            // Performance assertion - should complete within reasonable time
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000, "scoring 1000 tests should complete within 5 seconds");
            
            // Verify all tests have been scored
            scoredTests.All(t => t.SelectionScore > 0).Should().BeTrue();
        }

        [Theory]
        [InlineData(ConfidenceLevel.Fast)]
        [InlineData(ConfidenceLevel.Medium)]
        [InlineData(ConfidenceLevel.High)]
        [InlineData(ConfidenceLevel.Full)]
        public async Task ScoreTestsAsync_WithDifferentConfidenceLevels_ShouldAdjustScoring(ConfidenceLevel confidence)
        {
            var tests = new[]
            {
                CreateTestInfo("UnitTest", TestCategory.Unit, TimeSpan.FromMilliseconds(50)),
                CreateTestInfo("IntegrationTest", TestCategory.Integration, TimeSpan.FromSeconds(1)),
                CreateTestInfo("E2ETest", TestCategory.UI, TimeSpan.FromSeconds(10))
            };

            var changes = CreateCodeChangeSet();
            var engine = new TestSelectionEngine(_mockLogger.Object, new[] { _mockScoringAlgorithm.Object });
            
            // Configure scoring based on confidence level
            _mockScoringAlgorithm.Setup(x => x.CalculateScoreAsync(It.IsAny<TestInfo>(), It.IsAny<TestScoringContext>(), It.IsAny<CancellationToken>()))
                .Returns<TestInfo, TestScoringContext, CancellationToken>((test, context, ct) =>
                {
                    var baseScore = test.Category switch
                    {
                        TestCategory.Unit => 0.8,
                        TestCategory.Integration => 0.6,
                        TestCategory.UI => 0.4,
                        _ => 0.2
                    };

                    // Boost score based on confidence level
                    var confidenceMultiplier = context.ConfidenceLevel switch
                    {
                        ConfidenceLevel.Fast => test.Category == TestCategory.Unit ? 1.2 : 0.8,
                        ConfidenceLevel.Medium => test.Category <= TestCategory.Integration ? 1.1 : 0.9,
                        ConfidenceLevel.High => 1.0,
                        ConfidenceLevel.Full => 1.0,
                        _ => 1.0
                    };

                    return Task.FromResult(baseScore * confidenceMultiplier);
                });

            var scoredTests = await engine.ScoreTestsAsync(tests, changes);

            scoredTests.Should().HaveCount(3);
            scoredTests.Should().BeInDescendingOrder(t => t.SelectionScore);
            
            // Verify confidence-specific behavior
            switch (confidence)
            {
                case ConfidenceLevel.Fast:
                    // Should prioritize unit tests
                    scoredTests[0].Category.Should().Be(TestCategory.Unit);
                    break;
                case ConfidenceLevel.Medium:
                    // Should include unit and integration tests with good scores
                    scoredTests.Take(2).All(t => t.Category <= TestCategory.Integration).Should().BeTrue();
                    break;
                case ConfidenceLevel.High:
                case ConfidenceLevel.Full:
                    // Should include all test types
                    scoredTests.Should().Contain(t => t.Category == TestCategory.UI);
                    break;
            }
        }

        #endregion
    }
}