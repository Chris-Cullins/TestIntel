using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Core.Models;
using TestIntelligence.DataTracker.Models;

namespace TestIntelligence.DataTracker.Analysis
{
    /// <summary>
    /// Defines the contract for detecting database access patterns in test methods.
    /// </summary>
    public interface IDatabasePatternDetector
    {
        /// <summary>
        /// Detects database operations in a test method.
        /// </summary>
        /// <param name="testMethod">The test method to analyze.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of detected database dependencies.</returns>
        Task<IReadOnlyList<DataDependency>> DetectDatabaseOperationsAsync(
            TestMethod testMethod, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Detects database operations in setup methods.
        /// </summary>
        /// <param name="setupMethods">Setup methods to analyze.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of detected database dependencies.</returns>
        Task<IReadOnlyList<DataDependency>> DetectDatabaseOperationsAsync(
            IEnumerable<MethodInfo> setupMethods, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Determines if a test method requires exclusive database access.
        /// </summary>
        /// <param name="testMethod">The test method to check.</param>
        /// <returns>True if the test requires exclusive access.</returns>
        bool RequiresExclusiveDbAccess(TestMethod testMethod);

        /// <summary>
        /// Checks if two test methods share database dependencies.
        /// </summary>
        /// <param name="testA">First test method.</param>
        /// <param name="testB">Second test method.</param>
        /// <param name="dependenciesA">Dependencies for test A.</param>
        /// <param name="dependenciesB">Dependencies for test B.</param>
        /// <returns>True if tests share database dependencies.</returns>
        bool SharesDatabaseDependency(
            TestMethod testA, 
            TestMethod testB, 
            IReadOnlyList<DataDependency> dependenciesA,
            IReadOnlyList<DataDependency> dependenciesB);

        /// <summary>
        /// Gets the supported database frameworks by this detector.
        /// </summary>
        IReadOnlyList<DatabaseFramework> SupportedFrameworks { get; }
    }

    /// <summary>
    /// Specific detector for Entity Framework 6 patterns.
    /// </summary>
    public interface IEF6PatternDetector : IDatabasePatternDetector
    {
        /// <summary>
        /// Detects EF6 DbContext usage in a method.
        /// </summary>
        /// <param name="method">Method to analyze.</param>
        /// <returns>List of detected DbContext types and operations.</returns>
        Task<IReadOnlyList<EF6ContextUsage>> DetectDbContextUsageAsync(MethodInfo method);

        /// <summary>
        /// Detects data seeding patterns in EF6 setup methods.
        /// </summary>
        /// <param name="setupMethod">Setup method to analyze.</param>
        /// <returns>List of detected seeding operations.</returns>
        Task<IReadOnlyList<DataSeedingOperation>> DetectDataSeedingAsync(MethodInfo setupMethod);
    }

    /// <summary>
    /// Specific detector for Entity Framework Core patterns.
    /// </summary>
    public interface IEFCorePatternDetector : IDatabasePatternDetector
    {
        /// <summary>
        /// Detects EF Core DbContext usage in a method.
        /// </summary>
        /// <param name="method">Method to analyze.</param>
        /// <returns>List of detected DbContext types and operations.</returns>
        Task<IReadOnlyList<EFCoreContextUsage>> DetectDbContextUsageAsync(MethodInfo method);

        /// <summary>
        /// Detects in-memory database usage patterns.
        /// </summary>
        /// <param name="method">Method to analyze.</param>
        /// <returns>List of in-memory database operations.</returns>
        Task<IReadOnlyList<InMemoryDatabaseUsage>> DetectInMemoryDatabaseUsageAsync(MethodInfo method);
    }

    /// <summary>
    /// Represents usage of an Entity Framework 6 DbContext.
    /// </summary>
    public class EF6ContextUsage
    {
        public EF6ContextUsage(Type contextType, IReadOnlyList<string> entitySets, DataAccessType accessType)
        {
            ContextType = contextType ?? throw new ArgumentNullException(nameof(contextType));
            EntitySets = entitySets ?? throw new ArgumentNullException(nameof(entitySets));
            AccessType = accessType;
        }

        public Type ContextType { get; }
        public IReadOnlyList<string> EntitySets { get; }
        public DataAccessType AccessType { get; }
    }

    /// <summary>
    /// Represents usage of an Entity Framework Core DbContext.
    /// </summary>
    public class EFCoreContextUsage
    {
        public EFCoreContextUsage(Type contextType, IReadOnlyList<string> entitySets, DataAccessType accessType)
        {
            ContextType = contextType ?? throw new ArgumentNullException(nameof(contextType));
            EntitySets = entitySets ?? throw new ArgumentNullException(nameof(entitySets));
            AccessType = accessType;
        }

        public Type ContextType { get; }
        public IReadOnlyList<string> EntitySets { get; }
        public DataAccessType AccessType { get; }
    }

    /// <summary>
    /// Represents usage of in-memory databases in tests.
    /// </summary>
    public class InMemoryDatabaseUsage
    {
        public InMemoryDatabaseUsage(string databaseName, Type contextType)
        {
            DatabaseName = databaseName ?? throw new ArgumentNullException(nameof(databaseName));
            ContextType = contextType ?? throw new ArgumentNullException(nameof(contextType));
        }

        public string DatabaseName { get; }
        public Type ContextType { get; }
    }

    /// <summary>
    /// Represents a data seeding operation in test setup.
    /// </summary>
    public class DataSeedingOperation
    {
        public DataSeedingOperation(Type entityType, string operationType, int estimatedRecordCount)
        {
            EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
            OperationType = operationType ?? throw new ArgumentNullException(nameof(operationType));
            EstimatedRecordCount = estimatedRecordCount;
        }

        public Type EntityType { get; }
        public string OperationType { get; }
        public int EstimatedRecordCount { get; }
    }

    /// <summary>
    /// Supported database frameworks for pattern detection.
    /// </summary>
    public enum DatabaseFramework
    {
        EntityFramework6,
        EntityFrameworkCore,
        ADONet,
        Dapper,
        NHibernate
    }
}