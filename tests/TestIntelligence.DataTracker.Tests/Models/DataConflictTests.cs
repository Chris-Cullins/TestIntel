using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using TestIntelligence.DataTracker.Models;
using Xunit;

namespace TestIntelligence.DataTracker.Tests.Models
{
    public class DataConflictTests
    {
        private readonly List<DataDependency> _sampleDependencies;

        public DataConflictTests()
        {
            _sampleDependencies = new List<DataDependency>
            {
                new("TestMethodA", DataDependencyType.Database, "EF6:TestContext", DataAccessType.Write, new[] { "User" }),
                new("TestMethodB", DataDependencyType.Database, "EF6:TestContext", DataAccessType.Read, new[] { "User" })
            };
        }

        [Fact]
        public void Constructor_WithValidParameters_SetsPropertiesCorrectly()
        {
            // Arrange
            const string testMethodA = "TestProject.TestClass.TestMethodA";
            const string testMethodB = "TestProject.TestClass.TestMethodB";
            const ConflictType conflictType = ConflictType.SharedData;
            const string conflictReason = "Both tests access the same database";

            // Act
            var conflict = new DataConflict(
                testMethodA, 
                testMethodB, 
                conflictType, 
                conflictReason, 
                _sampleDependencies);

            // Assert
            conflict.TestMethodA.Should().Be(testMethodA);
            conflict.TestMethodB.Should().Be(testMethodB);
            conflict.ConflictType.Should().Be(conflictType);
            conflict.ConflictReason.Should().Be(conflictReason);
            conflict.ConflictingDependencies.Should().BeEquivalentTo(_sampleDependencies);
            conflict.DetectedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Constructor_WithNullTestMethodA_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => new DataConflict(
                null!, 
                "TestMethodB", 
                ConflictType.SharedData, 
                "reason", 
                _sampleDependencies);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("testMethodA");
        }

        [Fact]
        public void Constructor_WithNullTestMethodB_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => new DataConflict(
                "TestMethodA", 
                null!, 
                ConflictType.SharedData, 
                "reason", 
                _sampleDependencies);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("testMethodB");
        }

        [Fact]
        public void Constructor_WithNullConflictReason_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => new DataConflict(
                "TestMethodA", 
                "TestMethodB", 
                ConflictType.SharedData, 
                null!, 
                _sampleDependencies);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("conflictReason");
        }

        [Fact]
        public void Constructor_WithNullConflictingDependencies_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => new DataConflict(
                "TestMethodA", 
                "TestMethodB", 
                ConflictType.SharedData, 
                "reason", 
                null!);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("conflictingDependencies");
        }

        [Theory]
        [InlineData(ConflictType.ExclusiveResource, ConflictSeverity.High)]
        [InlineData(ConflictType.SharedData, ConflictSeverity.High)]
        [InlineData(ConflictType.OrderDependency, ConflictSeverity.Medium)]
        [InlineData(ConflictType.SharedFixture, ConflictSeverity.Medium)]
        [InlineData(ConflictType.ResourceContention, ConflictSeverity.Medium)]
        [InlineData(ConflictType.PotentialRaceCondition, ConflictSeverity.Low)]
        public void Severity_Property_ReturnsCorrectSeverityForConflictType(ConflictType conflictType, ConflictSeverity expectedSeverity)
        {
            // Act
            var conflict = new DataConflict(
                "TestMethodA", 
                "TestMethodB", 
                conflictType, 
                "reason", 
                _sampleDependencies);

            // Assert
            conflict.Severity.Should().Be(expectedSeverity);
        }

        [Theory]
        [InlineData(ConflictType.SharedData, true)]
        [InlineData(ConflictType.ExclusiveResource, true)]
        [InlineData(ConflictType.OrderDependency, true)]
        [InlineData(ConflictType.SharedFixture, false)]
        [InlineData(ConflictType.ResourceContention, false)]
        [InlineData(ConflictType.PotentialRaceCondition, false)]
        public void PreventsParallelExecution_Property_ReturnsCorrectValueForConflictType(ConflictType conflictType, bool expectedPrevention)
        {
            // Act
            var conflict = new DataConflict(
                "TestMethodA", 
                "TestMethodB", 
                conflictType, 
                "reason", 
                _sampleDependencies);

            // Assert
            conflict.PreventsParallelExecution.Should().Be(expectedPrevention);
        }

        [Fact]
        public void ToString_ReturnsExpectedFormat()
        {
            // Arrange
            var conflict = new DataConflict(
                "TestMethodA", 
                "TestMethodB", 
                ConflictType.SharedData, 
                "Both tests access the same database", 
                _sampleDependencies);

            // Act
            var result = conflict.ToString();

            // Assert
            result.Should().Be("SharedData conflict between TestMethodA and TestMethodB: Both tests access the same database");
        }

        [Fact]
        public void Properties_AreImmutable()
        {
            // Arrange
            var conflict = new DataConflict(
                "TestMethodA", 
                "TestMethodB", 
                ConflictType.SharedData, 
                "reason", 
                _sampleDependencies);

            // Assert - All properties should only have getters
            typeof(DataConflict).GetProperty(nameof(DataConflict.TestMethodA))!
                .CanWrite.Should().BeFalse();
            typeof(DataConflict).GetProperty(nameof(DataConflict.TestMethodB))!
                .CanWrite.Should().BeFalse();
            typeof(DataConflict).GetProperty(nameof(DataConflict.ConflictType))!
                .CanWrite.Should().BeFalse();
            typeof(DataConflict).GetProperty(nameof(DataConflict.ConflictReason))!
                .CanWrite.Should().BeFalse();
            typeof(DataConflict).GetProperty(nameof(DataConflict.ConflictingDependencies))!
                .CanWrite.Should().BeFalse();
            typeof(DataConflict).GetProperty(nameof(DataConflict.DetectedAt))!
                .CanWrite.Should().BeFalse();
        }

        [Fact]
        public void ConflictingDependencies_PropertyIsReadOnly()
        {
            // Arrange
            var conflict = new DataConflict(
                "TestMethodA", 
                "TestMethodB", 
                ConflictType.SharedData, 
                "reason", 
                _sampleDependencies);

            // Assert
            conflict.ConflictingDependencies.Should().BeAssignableTo<IReadOnlyList<DataDependency>>();
        }

        [Fact]
        public void DetectedAt_IsSetToCurrentTime()
        {
            // Arrange
            var beforeCreation = DateTimeOffset.UtcNow;

            // Act
            var conflict = new DataConflict(
                "TestMethodA", 
                "TestMethodB", 
                ConflictType.SharedData, 
                "reason", 
                _sampleDependencies);

            var afterCreation = DateTimeOffset.UtcNow;

            // Assert
            conflict.DetectedAt.Should().BeOnOrAfter(beforeCreation);
            conflict.DetectedAt.Should().BeOnOrBefore(afterCreation);
        }
    }
}