using System;
using System.Collections.Generic;
using System.Linq;
using TestIntelligence.Core.Models;

namespace TestIntelligence.SelectionEngine.Models
{
    /// <summary>
    /// Represents a plan for executing selected tests.
    /// </summary>
    public class TestExecutionPlan
    {
        public TestExecutionPlan(
            IEnumerable<TestInfo> tests,
            ConfidenceLevel confidenceLevel,
            TimeSpan estimatedDuration,
            string? description = null)
        {
            Tests = (tests ?? throw new ArgumentNullException(nameof(tests))).ToList().AsReadOnly();
            ConfidenceLevel = confidenceLevel;
            EstimatedDuration = estimatedDuration;
            Description = description;
            CreatedAt = DateTimeOffset.UtcNow;
            ExecutionBatches = new List<TestExecutionBatch>();

            CalculateMetrics();
        }

        /// <summary>
        /// Tests selected for execution.
        /// </summary>
        public IReadOnlyList<TestInfo> Tests { get; }

        /// <summary>
        /// Confidence level of this execution plan.
        /// </summary>
        public ConfidenceLevel ConfidenceLevel { get; }

        /// <summary>
        /// Estimated total execution time.
        /// </summary>
        public TimeSpan EstimatedDuration { get; }

        /// <summary>
        /// Optional description of the execution plan.
        /// </summary>
        public string? Description { get; }

        /// <summary>
        /// When this plan was created.
        /// </summary>
        public DateTimeOffset CreatedAt { get; }

        /// <summary>
        /// Batches for parallel execution optimization.
        /// </summary>
        public IList<TestExecutionBatch> ExecutionBatches { get; }

        /// <summary>
        /// Confidence score for this plan (0.0 to 1.0).
        /// </summary>
        public double Confidence { get; private set; }

        /// <summary>
        /// Total number of tests in the plan.
        /// </summary>
        public int TotalTestCount { get; private set; }

        /// <summary>
        /// Breakdown of tests by category.
        /// </summary>
        public IReadOnlyDictionary<TestCategory, int> TestCategoryBreakdown { get; private set; } = new Dictionary<TestCategory, int>();

        /// <summary>
        /// Average test selection score.
        /// </summary>
        public double AverageSelectionScore { get; private set; }

        /// <summary>
        /// Number of potential parallel execution threads.
        /// </summary>
        public int ParallelismDegree { get; set; }

        /// <summary>
        /// Selected tests for execution (for API compatibility).
        /// </summary>
        public IReadOnlyList<TestInfo> SelectedTests => Tests;

        /// <summary>
        /// Estimated execution time (for API compatibility).
        /// </summary>
        public TimeSpan EstimatedExecutionTime => EstimatedDuration;

        /// <summary>
        /// Reason for test selection (for API compatibility).
        /// </summary>
        public string SelectionReason => Description ?? "Selected based on configured selection criteria";

        /// <summary>
        /// Creates optimized execution batches for parallel execution.
        /// </summary>
        public void CreateExecutionBatches(int maxParallelism = 4)
        {
            ExecutionBatches.Clear();
            ParallelismDegree = Math.Min(maxParallelism, Tests.Count);

            if (Tests.Count == 0) return;

            // Group tests by compatibility for parallel execution
            var compatibleGroups = GroupTestsByCompatibility();
            var currentBatch = new List<TestInfo>();
            var currentBatchDuration = TimeSpan.Zero;
            var maxBatchDuration = TimeSpan.FromMinutes(5); // Target batch size

            foreach (var group in compatibleGroups.OrderByDescending(g => g.Sum(t => t.AverageExecutionTime.TotalMilliseconds)))
            {
                foreach (var test in group.OrderByDescending(t => t.SelectionScore))
                {
                    if (currentBatch.Count >= ParallelismDegree || 
                        (currentBatchDuration + test.AverageExecutionTime > maxBatchDuration && currentBatch.Count > 0))
                    {
                        // Create batch and start new one
                        ExecutionBatches.Add(new TestExecutionBatch(currentBatch, currentBatchDuration));
                        currentBatch = new List<TestInfo>();
                        currentBatchDuration = TimeSpan.Zero;
                    }

                    currentBatch.Add(test);
                    currentBatchDuration = TimeSpan.FromMilliseconds(
                        Math.Max(currentBatchDuration.TotalMilliseconds, test.AverageExecutionTime.TotalMilliseconds));
                }
            }

            // Add final batch if it has tests
            if (currentBatch.Count > 0)
            {
                ExecutionBatches.Add(new TestExecutionBatch(currentBatch, currentBatchDuration));
            }
        }

        /// <summary>
        /// Gets tests filtered by category.
        /// </summary>
        public IEnumerable<TestInfo> GetTestsByCategory(TestCategory category)
        {
            return Tests.Where(t => t.Category == category);
        }

        /// <summary>
        /// Gets the estimated total execution time considering parallelism.
        /// </summary>
        public TimeSpan GetOptimizedExecutionTime()
        {
            if (ExecutionBatches.Count == 0) return EstimatedDuration;

            return TimeSpan.FromMilliseconds(ExecutionBatches.Sum(b => b.EstimatedDuration.TotalMilliseconds));
        }

        private void CalculateMetrics()
        {
            TotalTestCount = Tests.Count;
            Confidence = ConfidenceLevel.GetConfidenceScore();

            if (Tests.Count == 0)
            {
                AverageSelectionScore = 0.0;
                TestCategoryBreakdown = new Dictionary<TestCategory, int>();
                return;
            }

            AverageSelectionScore = Tests.Average(t => t.SelectionScore);

            var breakdown = new Dictionary<TestCategory, int>();
            foreach (var test in Tests)
            {
                if (breakdown.ContainsKey(test.Category))
                {
                    breakdown[test.Category]++;
                }
                else
                {
                    breakdown[test.Category] = 1;
                }
            }
            TestCategoryBreakdown = breakdown;
        }

        private IEnumerable<List<TestInfo>> GroupTestsByCompatibility()
        {
            var groups = new List<List<TestInfo>>();
            var ungroupedTests = Tests.ToList();

            while (ungroupedTests.Count > 0)
            {
                var currentGroup = new List<TestInfo> { ungroupedTests[0] };
                ungroupedTests.RemoveAt(0);

                for (int i = ungroupedTests.Count - 1; i >= 0; i--)
                {
                    if (CanRunInParallel(currentGroup[0], ungroupedTests[i]))
                    {
                        currentGroup.Add(ungroupedTests[i]);
                        ungroupedTests.RemoveAt(i);
                    }
                }

                groups.Add(currentGroup);
            }

            return groups;
        }

        private static bool CanRunInParallel(TestInfo test1, TestInfo test2)
        {
            // Simple compatibility rules - can be enhanced
            
            // Database tests generally can't run in parallel
            if (test1.Category == TestCategory.Database || test2.Category == TestCategory.Database)
                return false;

            // UI tests can't run in parallel
            if (test1.Category == TestCategory.UI || test2.Category == TestCategory.UI)
                return false;

            // EndToEnd tests usually can't run in parallel
            if (test1.Category == TestCategory.EndToEnd || test2.Category == TestCategory.EndToEnd)
                return false;

            // Check for shared dependencies
            if (test1.Dependencies.Overlaps(test2.Dependencies))
                return false;

            return true;
        }

        public override string ToString()
        {
            return $"Execution Plan: {TotalTestCount} tests, {ConfidenceLevel} confidence ({Confidence:P0}), ~{EstimatedDuration.TotalMinutes:F1}min";
        }
    }

    /// <summary>
    /// Represents a batch of tests that can be executed in parallel.
    /// </summary>
    public class TestExecutionBatch
    {
        public TestExecutionBatch(IEnumerable<TestInfo> tests, TimeSpan estimatedDuration)
        {
            Tests = (tests ?? throw new ArgumentNullException(nameof(tests))).ToList().AsReadOnly();
            EstimatedDuration = estimatedDuration;
            BatchNumber = 0;
        }

        /// <summary>
        /// Tests in this batch.
        /// </summary>
        public IReadOnlyList<TestInfo> Tests { get; }

        /// <summary>
        /// Estimated duration for this batch (considering parallel execution).
        /// </summary>
        public TimeSpan EstimatedDuration { get; }

        /// <summary>
        /// Batch number in the execution sequence.
        /// </summary>
        public int BatchNumber { get; set; }

        /// <summary>
        /// Whether this batch can be executed in parallel.
        /// </summary>
        public bool CanExecuteInParallel => Tests.Count > 1;

        public override string ToString()
        {
            var parallelism = CanExecuteInParallel ? $" ({Tests.Count} parallel)" : "";
            return $"Batch {BatchNumber}: {Tests.Count} tests{parallelism}, ~{EstimatedDuration.TotalSeconds:F0}s";
        }
    }
}