using System;
using System.Collections.Generic;

namespace TestIntelligence.DataTracker.Models
{
    /// <summary>
    /// Represents a data dependency for a test method.
    /// </summary>
    public class DataDependency
    {
        public DataDependency(
            string testMethodId,
            DataDependencyType dependencyType,
            string resourceIdentifier,
            DataAccessType accessType,
            IReadOnlyList<string> entityTypes)
        {
            TestMethodId = testMethodId ?? throw new ArgumentNullException(nameof(testMethodId));
            DependencyType = dependencyType;
            ResourceIdentifier = resourceIdentifier ?? throw new ArgumentNullException(nameof(resourceIdentifier));
            AccessType = accessType;
            EntityTypes = entityTypes ?? throw new ArgumentNullException(nameof(entityTypes));
            DetectedAt = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// Unique identifier for the test method that has this dependency.
        /// </summary>
        public string TestMethodId { get; }

        /// <summary>
        /// Type of data dependency (Database, File System, Network, etc.).
        /// </summary>
        public DataDependencyType DependencyType { get; }

        /// <summary>
        /// Identifier for the resource (connection string, file path, URL, etc.).
        /// </summary>
        public string ResourceIdentifier { get; }

        /// <summary>
        /// Type of access (Read, Write, ReadWrite).
        /// </summary>
        public DataAccessType AccessType { get; }

        /// <summary>
        /// Entity types or tables accessed by this dependency.
        /// </summary>
        public IReadOnlyList<string> EntityTypes { get; }

        /// <summary>
        /// When this dependency was detected.
        /// </summary>
        public DateTimeOffset DetectedAt { get; }

        public override string ToString()
        {
            return $"{DependencyType} dependency on {ResourceIdentifier} ({AccessType})";
        }
    }

    /// <summary>
    /// Types of data dependencies that can be detected.
    /// </summary>
    public enum DataDependencyType
    {
        /// <summary>
        /// Database dependency (EF6, EF Core, ADO.NET).
        /// </summary>
        Database,

        /// <summary>
        /// File system dependency.
        /// </summary>
        FileSystem,

        /// <summary>
        /// Network or HTTP dependency.
        /// </summary>
        Network,

        /// <summary>
        /// In-memory cache dependency.
        /// </summary>
        Cache,

        /// <summary>
        /// External service dependency.
        /// </summary>
        ExternalService,

        /// <summary>
        /// Configuration dependency.
        /// </summary>
        Configuration
    }

    /// <summary>
    /// Types of data access patterns.
    /// </summary>
    public enum DataAccessType
    {
        /// <summary>
        /// Read-only access.
        /// </summary>
        Read,

        /// <summary>
        /// Write-only access.
        /// </summary>
        Write,

        /// <summary>
        /// Both read and write access.
        /// </summary>
        ReadWrite,

        /// <summary>
        /// Creates new data.
        /// </summary>
        Create,

        /// <summary>
        /// Updates existing data.
        /// </summary>
        Update,

        /// <summary>
        /// Deletes data.
        /// </summary>
        Delete
    }
}