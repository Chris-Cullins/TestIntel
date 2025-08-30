using System;
using System.Reflection;
using FluentAssertions;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Models;
using TestIntelligence.SelectionEngine.Models;
using Xunit;

namespace TestIntelligence.SelectionEngine.Tests.Models
{
    public class TestInfoTests
    {
        private TestMethod CreateTestMethod(string methodName = "TestMethod")
        {
            var type = typeof(TestInfoTests);
            var method = type.GetMethod(nameof(SampleTestMethod), BindingFlags.NonPublic | BindingFlags.Instance) 
                ?? throw new InvalidOperationException("Sample method not found");
            
            return new TestMethod(method, type, "/test/assembly.dll", FrameworkVersion.Net5Plus);
        }

        [Fact]
        public void Constructor_WithValidParameters_ShouldCreateInstance()
        {
            var testMethod = CreateTestMethod();
            var category = TestCategory.Unit;
            var executionTime = TimeSpan.FromMilliseconds(100);
            var score = 0.75;

            var testInfo = new TestInfo(testMethod, category, executionTime, score);

            testInfo.TestMethod.Should().Be(testMethod);
            testInfo.Category.Should().Be(category);
            testInfo.AverageExecutionTime.Should().Be(executionTime);
            testInfo.SelectionScore.Should().Be(score);
            testInfo.ExecutionHistory.Should().BeEmpty();
            testInfo.Dependencies.Should().BeEmpty();
            testInfo.Tags.Should().BeEmpty();
            testInfo.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Constructor_WithNullTestMethod_ShouldThrowArgumentNullException()
        {
            Action act = () => new TestInfo(null!, TestCategory.Unit, TimeSpan.FromMilliseconds(100));
            
            act.Should().Throw<ArgumentNullException>().WithParameterName("testMethod");
        }

        [Fact]
        public void GetUniqueId_ShouldReturnTestMethodUniqueId()
        {
            var testMethod = CreateTestMethod();
            var testInfo = new TestInfo(testMethod, TestCategory.Unit, TimeSpan.FromMilliseconds(100));

            var uniqueId = testInfo.GetUniqueId();

            uniqueId.Should().Be(testMethod.GetUniqueId());
        }

        [Fact]
        public void GetDisplayName_ShouldReturnTestMethodDisplayName()
        {
            var testMethod = CreateTestMethod();
            var testInfo = new TestInfo(testMethod, TestCategory.Unit, TimeSpan.FromMilliseconds(100));

            var displayName = testInfo.GetDisplayName();

            displayName.Should().Be(testMethod.GetDisplayName());
        }

        [Fact]
        public void CalculateFailureRate_WithNoHistory_ShouldReturnZero()
        {
            var testInfo = new TestInfo(CreateTestMethod(), TestCategory.Unit, TimeSpan.FromMilliseconds(100));

            var failureRate = testInfo.CalculateFailureRate();

            failureRate.Should().Be(0.0);
        }

        [Fact]
        public void CalculateFailureRate_WithMixedResults_ShouldReturnCorrectRate()
        {
            var testInfo = new TestInfo(CreateTestMethod(), TestCategory.Unit, TimeSpan.FromMilliseconds(100));
            
            // Add 2 failures out of 5 executions
            testInfo.ExecutionHistory.Add(new TestExecutionResult(true, TimeSpan.FromMilliseconds(50), DateTimeOffset.UtcNow));
            testInfo.ExecutionHistory.Add(new TestExecutionResult(false, TimeSpan.FromMilliseconds(75), DateTimeOffset.UtcNow, "Error"));
            testInfo.ExecutionHistory.Add(new TestExecutionResult(true, TimeSpan.FromMilliseconds(60), DateTimeOffset.UtcNow));
            testInfo.ExecutionHistory.Add(new TestExecutionResult(false, TimeSpan.FromMilliseconds(80), DateTimeOffset.UtcNow, "Error"));
            testInfo.ExecutionHistory.Add(new TestExecutionResult(true, TimeSpan.FromMilliseconds(55), DateTimeOffset.UtcNow));

            var failureRate = testInfo.CalculateFailureRate();

            failureRate.Should().Be(0.4); // 2 failures out of 5 = 40%
        }

        [Fact]
        public void IsFlaky_WithInsufficientHistory_ShouldReturnFalse()
        {
            var testInfo = new TestInfo(CreateTestMethod(), TestCategory.Unit, TimeSpan.FromMilliseconds(100));
            
            // Add only 3 results (less than 5 required)
            testInfo.ExecutionHistory.Add(new TestExecutionResult(true, TimeSpan.FromMilliseconds(50), DateTimeOffset.UtcNow));
            testInfo.ExecutionHistory.Add(new TestExecutionResult(false, TimeSpan.FromMilliseconds(75), DateTimeOffset.UtcNow, "Error"));
            testInfo.ExecutionHistory.Add(new TestExecutionResult(true, TimeSpan.FromMilliseconds(60), DateTimeOffset.UtcNow));

            var isFlaky = testInfo.IsFlaky();

            isFlaky.Should().BeFalse();
        }

        [Theory]
        [InlineData(0.0, false)] // Never fails - not flaky
        [InlineData(0.05, false)] // Very low failure rate - not flaky
        [InlineData(0.3, true)] // 30% failure rate - flaky
        [InlineData(0.5, true)] // 50% failure rate - flaky
        [InlineData(0.7, true)] // 70% failure rate - flaky
        [InlineData(0.95, false)] // Very high failure rate - consistently failing, not flaky
        [InlineData(1.0, false)] // Always fails - not flaky
        public void IsFlaky_WithVariousFailureRates_ShouldDetectFlakyTests(double failureRate, bool expectedFlaky)
        {
            var testInfo = new TestInfo(CreateTestMethod(), TestCategory.Unit, TimeSpan.FromMilliseconds(100));
            
            // Add 10 results with the specified failure rate
            var totalResults = 10;
            var failureCount = (int)(totalResults * failureRate);
            
            for (int i = 0; i < failureCount; i++)
            {
                testInfo.ExecutionHistory.Add(new TestExecutionResult(false, TimeSpan.FromMilliseconds(50), DateTimeOffset.UtcNow, "Error"));
            }
            
            for (int i = failureCount; i < totalResults; i++)
            {
                testInfo.ExecutionHistory.Add(new TestExecutionResult(true, TimeSpan.FromMilliseconds(50), DateTimeOffset.UtcNow));
            }

            var isFlaky = testInfo.IsFlaky();

            isFlaky.Should().Be(expectedFlaky);
        }

        [Fact]
        public void GetLastExecutionResult_WithNoHistory_ShouldReturnNull()
        {
            var testInfo = new TestInfo(CreateTestMethod(), TestCategory.Unit, TimeSpan.FromMilliseconds(100));

            var lastResult = testInfo.GetLastExecutionResult();

            lastResult.Should().BeNull();
        }

        [Fact]
        public void GetLastExecutionResult_WithHistory_ShouldReturnMostRecentResult()
        {
            var testInfo = new TestInfo(CreateTestMethod(), TestCategory.Unit, TimeSpan.FromMilliseconds(100));
            
            var oldResult = new TestExecutionResult(true, TimeSpan.FromMilliseconds(50), DateTimeOffset.UtcNow.AddMinutes(-10));
            var middleResult = new TestExecutionResult(false, TimeSpan.FromMilliseconds(75), DateTimeOffset.UtcNow.AddMinutes(-5), "Error");
            var newestResult = new TestExecutionResult(true, TimeSpan.FromMilliseconds(60), DateTimeOffset.UtcNow);
            
            testInfo.ExecutionHistory.Add(oldResult);
            testInfo.ExecutionHistory.Add(middleResult);
            testInfo.ExecutionHistory.Add(newestResult);

            var lastResult = testInfo.GetLastExecutionResult();

            lastResult.Should().Be(newestResult);
        }

        [Fact]
        public void ToString_ShouldIncludeDisplayNameScoreAndCategory()
        {
            var testInfo = new TestInfo(CreateTestMethod(), TestCategory.Integration, TimeSpan.FromMilliseconds(100), 0.789);

            var result = testInfo.ToString();

            result.Should().Contain("TestInfoTests.SampleTestMethod");
            result.Should().Contain("Score: 0.789");
            result.Should().Contain("Category: Integration");
        }

        private void SampleTestMethod()
        {
            // Sample method for reflection
        }
    }
}