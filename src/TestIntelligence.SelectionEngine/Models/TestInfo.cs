using System;
using System.Collections.Generic;
using TestIntelligence.Core.Models;

namespace TestIntelligence.SelectionEngine.Models
{
    /// <summary>
    /// Represents extended information about a test method for selection purposes.
    /// </summary>
    public class TestInfo
    {
        public TestInfo(
            TestMethod testMethod,
            TestCategory category,
            TimeSpan averageExecutionTime,
            double selectionScore = 0.0)
        {
            TestMethod = testMethod ?? throw new ArgumentNullException(nameof(testMethod));
            Category = category;
            AverageExecutionTime = averageExecutionTime;
            SelectionScore = selectionScore;
            ExecutionHistory = new List<TestExecutionResult>();
            Dependencies = new HashSet<string>();
            Tags = new HashSet<string>();
            CreatedAt = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// The underlying test method.
        /// </summary>
        public TestMethod TestMethod { get; }

        /// <summary>
        /// The category of this test (Unit, Integration, Database, etc.).
        /// </summary>
        public TestCategory Category { get; }

        /// <summary>
        /// Average execution time based on historical data.
        /// </summary>
        public TimeSpan AverageExecutionTime { get; set; }

        /// <summary>
        /// Selection score calculated by the selection algorithm (0.0 to 1.0).
        /// Higher scores indicate higher priority for selection.
        /// </summary>
        public double SelectionScore { get; set; }

        /// <summary>
        /// Priority level for execution (higher numbers = higher priority).
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// Historical execution results for this test.
        /// </summary>
        public IList<TestExecutionResult> ExecutionHistory { get; }

        /// <summary>
        /// Dependencies that this test has on methods, types, or other tests.
        /// </summary>
        public ISet<string> Dependencies { get; }

        /// <summary>
        /// Tags associated with this test (e.g., "slow", "flaky", "critical").
        /// </summary>
        public ISet<string> Tags { get; }

        /// <summary>
        /// When this test info was created.
        /// </summary>
        public DateTimeOffset CreatedAt { get; }

        /// <summary>
        /// Last time this test was selected for execution.
        /// </summary>
        public DateTimeOffset? LastSelected { get; set; }

        /// <summary>
        /// Last time this test was executed.
        /// </summary>
        public DateTimeOffset? LastExecuted { get; set; }

        /// <summary>
        /// Whether this test is currently being executed.
        /// </summary>
        public bool IsRunning { get; set; }

        /// <summary>
        /// Gets the unique identifier for this test.
        /// </summary>
        public string GetUniqueId()
        {
            return TestMethod.GetUniqueId();
        }

        /// <summary>
        /// Gets the display name for this test.
        /// </summary>
        public string GetDisplayName()
        {
            return TestMethod.GetDisplayName();
        }

        /// <summary>
        /// Gets the full name for this test (for API compatibility).
        /// </summary>
        public string FullName => GetDisplayName();

        /// <summary>
        /// Calculates the failure rate based on execution history.
        /// </summary>
        public double CalculateFailureRate()
        {
            if (ExecutionHistory.Count == 0) return 0.0;

            var failureCount = 0;
            foreach (var result in ExecutionHistory)
            {
                if (!result.Passed) failureCount++;
            }

            return (double)failureCount / ExecutionHistory.Count;
        }

        /// <summary>
        /// Determines if this test is considered flaky based on execution history.
        /// </summary>
        public bool IsFlaky()
        {
            if (ExecutionHistory.Count < 5) return false;

            var failureRate = CalculateFailureRate();
            return failureRate > 0.1 && failureRate < 0.9; // 10-90% failure rate indicates flaky
        }

        /// <summary>
        /// Gets the most recent execution result.
        /// </summary>
        public TestExecutionResult? GetLastExecutionResult()
        {
            if (ExecutionHistory.Count == 0) return null;

            TestExecutionResult? mostRecent = null;
            foreach (var result in ExecutionHistory)
            {
                if (mostRecent == null || result.ExecutedAt > mostRecent.ExecutedAt)
                    mostRecent = result;
            }

            return mostRecent;
        }

        public override string ToString()
        {
            return $"{GetDisplayName()} (Score: {SelectionScore:F3}, Category: {Category})";
        }
    }

    /// <summary>
    /// Represents the result of a test execution.
    /// </summary>
    public class TestExecutionResult
    {
        public TestExecutionResult(
            bool passed,
            TimeSpan duration,
            DateTimeOffset executedAt,
            string? errorMessage = null)
        {
            Passed = passed;
            Duration = duration;
            ExecutedAt = executedAt;
            ErrorMessage = errorMessage;
        }

        /// <summary>
        /// Whether the test passed.
        /// </summary>
        public bool Passed { get; }

        /// <summary>
        /// How long the test took to execute.
        /// </summary>
        public TimeSpan Duration { get; }

        /// <summary>
        /// When the test was executed.
        /// </summary>
        public DateTimeOffset ExecutedAt { get; }

        /// <summary>
        /// Error message if the test failed.
        /// </summary>
        public string? ErrorMessage { get; }

        public override string ToString()
        {
            var status = Passed ? "PASSED" : "FAILED";
            return $"{status} in {Duration.TotalMilliseconds:F0}ms at {ExecutedAt:yyyy-MM-dd HH:mm:ss}";
        }
    }

    /// <summary>
    /// Categories for test classification.
    /// </summary>
    public enum TestCategory
    {
        /// <summary>
        /// Fast, isolated unit tests.
        /// </summary>
        Unit,

        /// <summary>
        /// Integration tests that test component interactions.
        /// </summary>
        Integration,

        /// <summary>
        /// Tests that interact with databases.
        /// </summary>
        Database,

        /// <summary>
        /// Tests that make HTTP/API calls.
        /// </summary>
        API,

        /// <summary>
        /// UI automation tests.
        /// </summary>
        UI,

        /// <summary>
        /// End-to-end tests that test complete workflows.
        /// </summary>
        EndToEnd,

        /// <summary>
        /// Performance or load tests.
        /// </summary>
        Performance,

        /// <summary>
        /// Security tests.
        /// </summary>
        Security,

        /// <summary>
        /// Unknown or unclassified tests.
        /// </summary>
        Unknown
    }
}