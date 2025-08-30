using System;
using System.Collections.Generic;
using System.Linq;

namespace TestIntelligence.DataTracker.Models
{
    /// <summary>
    /// Represents a potential conflict between test methods due to shared data dependencies.
    /// </summary>
    public class DataConflict
    {
        public DataConflict(
            string testMethodA,
            string testMethodB,
            ConflictType conflictType,
            string conflictReason,
            IReadOnlyList<DataDependency> conflictingDependencies)
        {
            TestMethodA = testMethodA ?? throw new ArgumentNullException(nameof(testMethodA));
            TestMethodB = testMethodB ?? throw new ArgumentNullException(nameof(testMethodB));
            ConflictType = conflictType;
            ConflictReason = conflictReason ?? throw new ArgumentNullException(nameof(conflictReason));
            ConflictingDependencies = conflictingDependencies ?? throw new ArgumentNullException(nameof(conflictingDependencies));
            DetectedAt = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// First test method in the conflict.
        /// </summary>
        public string TestMethodA { get; }

        /// <summary>
        /// Second test method in the conflict.
        /// </summary>
        public string TestMethodB { get; }

        /// <summary>
        /// Type of conflict detected.
        /// </summary>
        public ConflictType ConflictType { get; }

        /// <summary>
        /// Human-readable explanation of why these tests conflict.
        /// </summary>
        public string ConflictReason { get; }

        /// <summary>
        /// The specific dependencies that cause the conflict.
        /// </summary>
        public IReadOnlyList<DataDependency> ConflictingDependencies { get; }

        /// <summary>
        /// When this conflict was detected.
        /// </summary>
        public DateTimeOffset DetectedAt { get; }

        /// <summary>
        /// Severity level of this conflict.
        /// </summary>
        public ConflictSeverity Severity => DetermineConflictSeverity();

        /// <summary>
        /// Whether this conflict prevents parallel execution.
        /// </summary>
        public bool PreventsParallelExecution => 
            ConflictType == ConflictType.SharedData ||
            ConflictType == ConflictType.ExclusiveResource ||
            ConflictType == ConflictType.OrderDependency;

        private ConflictSeverity DetermineConflictSeverity()
        {
            return ConflictType switch
            {
                ConflictType.ExclusiveResource => ConflictSeverity.High,
                ConflictType.SharedData => ConflictSeverity.High,
                ConflictType.OrderDependency => ConflictSeverity.Medium,
                ConflictType.SharedFixture => ConflictSeverity.Medium,
                ConflictType.ResourceContention => ConflictSeverity.Medium,
                ConflictType.PotentialRaceCondition => ConflictSeverity.Low,
                _ => ConflictSeverity.Low
            };
        }

        public override string ToString()
        {
            return $"{ConflictType} conflict between {TestMethodA} and {TestMethodB}: {ConflictReason}";
        }
    }

    /// <summary>
    /// Report containing all detected data conflicts for an assembly or test suite.
    /// </summary>
    public class DataConflictReport
    {
        public DataConflictReport(
            string assemblyPath,
            IReadOnlyList<DataConflict> conflicts,
            IReadOnlyList<DataDependency> dependencies)
        {
            AssemblyPath = assemblyPath ?? throw new ArgumentNullException(nameof(assemblyPath));
            Conflicts = conflicts ?? throw new ArgumentNullException(nameof(conflicts));
            Dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            GeneratedAt = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// Path to the assembly that was analyzed.
        /// </summary>
        public string AssemblyPath { get; }

        /// <summary>
        /// All detected conflicts.
        /// </summary>
        public IReadOnlyList<DataConflict> Conflicts { get; }

        /// <summary>
        /// All detected data dependencies.
        /// </summary>
        public IReadOnlyList<DataDependency> Dependencies { get; }

        /// <summary>
        /// When this report was generated.
        /// </summary>
        public DateTimeOffset GeneratedAt { get; }

        /// <summary>
        /// Number of high severity conflicts.
        /// </summary>
        public int HighSeverityConflictCount => 
            Conflicts.Count(c => c.Severity == ConflictSeverity.High);

        /// <summary>
        /// Number of conflicts that prevent parallel execution.
        /// </summary>
        public int ParallelBlockingConflictCount => 
            Conflicts.Count(c => c.PreventsParallelExecution);

        /// <summary>
        /// Gets conflicts by severity level.
        /// </summary>
        public IEnumerable<DataConflict> GetConflictsBySeverity(ConflictSeverity severity)
        {
            return Conflicts.Where(c => c.Severity == severity);
        }

        /// <summary>
        /// Gets all test methods that have dependencies.
        /// </summary>
        public IEnumerable<string> GetTestMethodsWithDependencies()
        {
            return Dependencies.Select(d => d.TestMethodId).Distinct();
        }

        public override string ToString()
        {
            return $"Data conflict report: {Conflicts.Count} conflicts, {Dependencies.Count} dependencies";
        }
    }

    /// <summary>
    /// Types of conflicts that can occur between test methods.
    /// </summary>
    public enum ConflictType
    {
        /// <summary>
        /// Tests modify the same shared data.
        /// </summary>
        SharedData,

        /// <summary>
        /// Tests require exclusive access to a resource.
        /// </summary>
        ExclusiveResource,

        /// <summary>
        /// Tests must run in a specific order.
        /// </summary>
        OrderDependency,

        /// <summary>
        /// Tests share the same test fixture instance.
        /// </summary>
        SharedFixture,

        /// <summary>
        /// Tests compete for limited resources.
        /// </summary>
        ResourceContention,

        /// <summary>
        /// Potential race condition between tests.
        /// </summary>
        PotentialRaceCondition
    }

    /// <summary>
    /// Severity levels for data conflicts.
    /// </summary>
    public enum ConflictSeverity
    {
        /// <summary>
        /// Low severity - may cause occasional test failures.
        /// </summary>
        Low,

        /// <summary>
        /// Medium severity - likely to cause test failures.
        /// </summary>
        Medium,

        /// <summary>
        /// High severity - will definitely cause test failures if run in parallel.
        /// </summary>
        High
    }
}