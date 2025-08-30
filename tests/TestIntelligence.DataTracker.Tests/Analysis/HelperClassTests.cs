using System;
using System.Collections.Generic;
using FluentAssertions;
using TestIntelligence.DataTracker.Analysis;
using TestIntelligence.DataTracker.Models;
using Xunit;

namespace TestIntelligence.DataTracker.Tests.Analysis
{
    public class HelperClassTests
    {
        [Fact]
        public void EF6ContextUsage_Constructor_WithValidParameters_SetsProperties()
        {
            // Arrange
            var contextType = typeof(string);
            var entitySets = new List<string> { "Users", "Products" };
            var accessType = DataAccessType.ReadWrite;

            // Act
            var usage = new EF6ContextUsage(contextType, entitySets, accessType);

            // Assert
            usage.ContextType.Should().Be(contextType);
            usage.EntitySets.Should().BeEquivalentTo(entitySets);
            usage.AccessType.Should().Be(accessType);
        }

        [Fact]
        public void EF6ContextUsage_Constructor_WithNullContextType_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => new EF6ContextUsage(null!, new List<string>(), DataAccessType.Read);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("contextType");
        }

        [Fact]
        public void EF6ContextUsage_Constructor_WithNullEntitySets_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => new EF6ContextUsage(typeof(string), null!, DataAccessType.Read);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("entitySets");
        }

        [Fact]
        public void EFCoreContextUsage_Constructor_WithValidParameters_SetsProperties()
        {
            // Arrange
            var contextType = typeof(string);
            var entitySets = new List<string> { "Users", "Products" };
            var accessType = DataAccessType.ReadWrite;

            // Act
            var usage = new EFCoreContextUsage(contextType, entitySets, accessType);

            // Assert
            usage.ContextType.Should().Be(contextType);
            usage.EntitySets.Should().BeEquivalentTo(entitySets);
            usage.AccessType.Should().Be(accessType);
        }

        [Fact]
        public void EFCoreContextUsage_Constructor_WithNullContextType_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => new EFCoreContextUsage(null!, new List<string>(), DataAccessType.Read);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("contextType");
        }

        [Fact]
        public void EFCoreContextUsage_Constructor_WithNullEntitySets_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => new EFCoreContextUsage(typeof(string), null!, DataAccessType.Read);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("entitySets");
        }

        [Fact]
        public void InMemoryDatabaseUsage_Constructor_WithValidParameters_SetsProperties()
        {
            // Arrange
            var databaseName = "TestDatabase";
            var contextType = typeof(string);

            // Act
            var usage = new InMemoryDatabaseUsage(databaseName, contextType);

            // Assert
            usage.DatabaseName.Should().Be(databaseName);
            usage.ContextType.Should().Be(contextType);
        }

        [Fact]
        public void InMemoryDatabaseUsage_Constructor_WithNullDatabaseName_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => new InMemoryDatabaseUsage(null!, typeof(string));

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("databaseName");
        }

        [Fact]
        public void InMemoryDatabaseUsage_Constructor_WithNullContextType_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => new InMemoryDatabaseUsage("TestDb", null!);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("contextType");
        }

        [Fact]
        public void DataSeedingOperation_Constructor_WithValidParameters_SetsProperties()
        {
            // Arrange
            var entityType = typeof(string);
            var operationType = "Insert";
            var recordCount = 10;

            // Act
            var operation = new DataSeedingOperation(entityType, operationType, recordCount);

            // Assert
            operation.EntityType.Should().Be(entityType);
            operation.OperationType.Should().Be(operationType);
            operation.EstimatedRecordCount.Should().Be(recordCount);
        }

        [Fact]
        public void DataSeedingOperation_Constructor_WithNullEntityType_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => new DataSeedingOperation(null!, "Insert", 10);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("entityType");
        }

        [Fact]
        public void DataSeedingOperation_Constructor_WithNullOperationType_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => new DataSeedingOperation(typeof(string), null!, 10);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("operationType");
        }

        [Fact]
        public void DataSeedingOperation_Constructor_WithZeroRecordCount_AllowsZero()
        {
            // Act
            var operation = new DataSeedingOperation(typeof(string), "Insert", 0);

            // Assert
            operation.EstimatedRecordCount.Should().Be(0);
        }

        [Fact]
        public void DataSeedingOperation_Constructor_WithNegativeRecordCount_AllowsNegative()
        {
            // Act
            var operation = new DataSeedingOperation(typeof(string), "Insert", -1);

            // Assert
            operation.EstimatedRecordCount.Should().Be(-1);
        }
    }
}