using System;
using System.Collections.Generic;
using System.Linq;

namespace TestIntelligence.ImpactAnalyzer.Models
{
    /// <summary>
    /// Represents a code change that needs impact analysis.
    /// </summary>
    public class CodeChange
    {
        public CodeChange(
            string filePath,
            CodeChangeType changeType,
            IReadOnlyList<string> changedMethods,
            IReadOnlyList<string> changedTypes)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            ChangeType = changeType;
            ChangedMethods = changedMethods ?? throw new ArgumentNullException(nameof(changedMethods));
            ChangedTypes = changedTypes ?? throw new ArgumentNullException(nameof(changedTypes));
            DetectedAt = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// Path to the file that was changed.
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Type of change (Added, Modified, Deleted, Renamed).
        /// </summary>
        public CodeChangeType ChangeType { get; }

        /// <summary>
        /// List of method names that were changed.
        /// </summary>
        public IReadOnlyList<string> ChangedMethods { get; }

        /// <summary>
        /// List of type names that were changed.
        /// </summary>
        public IReadOnlyList<string> ChangedTypes { get; }

        /// <summary>
        /// When this change was detected.
        /// </summary>
        public DateTimeOffset DetectedAt { get; }

        public override string ToString()
        {
            var methodCount = ChangedMethods.Count;
            var typeCount = ChangedTypes.Count;
            return $"{ChangeType} in {FilePath}: {methodCount} methods, {typeCount} types";
        }
    }

    /// <summary>
    /// Represents a set of code changes for analysis.
    /// </summary>
    public class CodeChangeSet
    {
        public CodeChangeSet(IReadOnlyList<CodeChange> changes)
        {
            Changes = changes ?? throw new ArgumentNullException(nameof(changes));
            CreatedAt = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// All code changes in this set.
        /// </summary>
        public IReadOnlyList<CodeChange> Changes { get; }

        /// <summary>
        /// When this change set was created.
        /// </summary>
        public DateTimeOffset CreatedAt { get; }

        /// <summary>
        /// Gets all unique file paths in this change set.
        /// </summary>
        public IEnumerable<string> GetChangedFiles()
        {
            return Changes.Select(c => c.FilePath).Distinct();
        }

        /// <summary>
        /// Gets all unique method names in this change set.
        /// </summary>
        public IEnumerable<string> GetChangedMethods()
        {
            return Changes.SelectMany(c => c.ChangedMethods).Distinct();
        }

        /// <summary>
        /// Gets all unique type names in this change set.
        /// </summary>
        public IEnumerable<string> GetChangedTypes()
        {
            return Changes.SelectMany(c => c.ChangedTypes).Distinct();
        }

        public override string ToString()
        {
            var fileCount = GetChangedFiles().Count();
            var methodCount = GetChangedMethods().Count();
            return $"Change set: {Changes.Count} changes across {fileCount} files, {methodCount} methods";
        }
    }

    /// <summary>
    /// Types of code changes that can be detected.
    /// </summary>
    public enum CodeChangeType
    {
        /// <summary>
        /// New code was added.
        /// </summary>
        Added,

        /// <summary>
        /// Existing code was modified.
        /// </summary>
        Modified,

        /// <summary>
        /// Code was deleted.
        /// </summary>
        Deleted,

        /// <summary>
        /// Code was moved or renamed.
        /// </summary>
        Renamed,

        /// <summary>
        /// Configuration file was changed.
        /// </summary>
        Configuration
    }
}