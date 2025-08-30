using System;
using System.Collections.Generic;
using FluentAssertions;
using TestIntelligence.DataTracker.Models;
using Xunit;

namespace TestIntelligence.DataTracker.Tests.Models
{
    public class DataDependencyTests
    {
        private readonly List<string> _sampleEntityTypes = new() { "User", "Product" };

        [Fact]
        public void Constructor_WithValidParameters_SetsPropertiesCorrectly()
        {
            // Arrange
            const string testMethodId = "TestProject.TestClass.TestMethod";
            const DataDependencyType dependencyType = DataDependencyType.Database;
            const string resourceIdentifier = "EF6:TestContext";
            const DataAccessType accessType = DataAccessType.ReadWrite;

            // Act
            var dependency = new DataDependency(
                testMethodId, 
                dependencyType, 
                resourceIdentifier, 
                accessType, 
                _sampleEntityTypes);

            // Assert
            dependency.TestMethodId.Should().Be(testMethodId);
            dependency.DependencyType.Should().Be(dependencyType);
            dependency.ResourceIdentifier.Should().Be(resourceIdentifier);
            dependency.AccessType.Should().Be(accessType);
            dependency.EntityTypes.Should().BeEquivalentTo(_sampleEntityTypes);
            dependency.DetectedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Constructor_WithNullTestMethodId_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => new DataDependency(
                null!, 
                DataDependencyType.Database, 
                "resource", 
                DataAccessType.Read, 
                _sampleEntityTypes);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("testMethodId");
        }

        [Fact]
        public void Constructor_WithNullResourceIdentifier_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => new DataDependency(
                "TestMethod", 
                DataDependencyType.Database, 
                null!, 
                DataAccessType.Read, 
                _sampleEntityTypes);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("resourceIdentifier");
        }

        [Fact]
        public void Constructor_WithNullEntityTypes_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => new DataDependency(
                "TestMethod", 
                DataDependencyType.Database, 
                "resource", 
                DataAccessType.Read, 
                null!);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("entityTypes");
        }

        [Theory]
        [InlineData(DataDependencyType.Database)]
        [InlineData(DataDependencyType.FileSystem)]
        [InlineData(DataDependencyType.Network)]
        [InlineData(DataDependencyType.Cache)]
        [InlineData(DataDependencyType.ExternalService)]
        [InlineData(DataDependencyType.Configuration)]
        public void DependencyType_Property_AcceptsAllValidValues(DataDependencyType dependencyType)
        {
            // Act
            var dependency = new DataDependency(
                "TestMethod", 
                dependencyType, 
                "resource", 
                DataAccessType.Read, 
                _sampleEntityTypes);

            // Assert
            dependency.DependencyType.Should().Be(dependencyType);
        }

        [Theory]
        [InlineData(DataAccessType.Read)]
        [InlineData(DataAccessType.Write)]
        [InlineData(DataAccessType.ReadWrite)]
        [InlineData(DataAccessType.Create)]
        [InlineData(DataAccessType.Update)]
        [InlineData(DataAccessType.Delete)]
        public void AccessType_Property_AcceptsAllValidValues(DataAccessType accessType)
        {
            // Act
            var dependency = new DataDependency(
                "TestMethod", 
                DataDependencyType.Database, 
                "resource", 
                accessType, 
                _sampleEntityTypes);

            // Assert
            dependency.AccessType.Should().Be(accessType);
        }

        [Fact]
        public void ToString_ReturnsExpectedFormat()
        {
            // Arrange
            var dependency = new DataDependency(
                "TestMethod", 
                DataDependencyType.Database, 
                "EF6:TestContext", 
                DataAccessType.ReadWrite, 
                _sampleEntityTypes);

            // Act
            var result = dependency.ToString();

            // Assert
            result.Should().Be("Database dependency on EF6:TestContext (ReadWrite)");
        }

        [Fact]
        public void EntityTypes_PropertyIsReadOnly()
        {
            // Arrange
            var dependency = new DataDependency(
                "TestMethod", 
                DataDependencyType.Database, 
                "resource", 
                DataAccessType.Read, 
                _sampleEntityTypes);

            // Assert
            dependency.EntityTypes.Should().BeAssignableTo<IReadOnlyList<string>>();
        }

        [Fact]
        public void DetectedAt_IsSetToCurrentTime()
        {
            // Arrange
            var beforeCreation = DateTimeOffset.UtcNow;

            // Act
            var dependency = new DataDependency(
                "TestMethod", 
                DataDependencyType.Database, 
                "resource", 
                DataAccessType.Read, 
                _sampleEntityTypes);

            var afterCreation = DateTimeOffset.UtcNow;

            // Assert
            dependency.DetectedAt.Should().BeOnOrAfter(beforeCreation);
            dependency.DetectedAt.Should().BeOnOrBefore(afterCreation);
        }

        [Fact]
        public void Constructor_WithEmptyEntityTypes_AllowsEmptyList()
        {
            // Arrange
            var emptyEntityTypes = new List<string>();

            // Act
            var dependency = new DataDependency(
                "TestMethod", 
                DataDependencyType.Database, 
                "resource", 
                DataAccessType.Read, 
                emptyEntityTypes);

            // Assert
            dependency.EntityTypes.Should().BeEmpty();
        }

        [Fact]
        public void Properties_AreImmutable()
        {
            // Arrange
            var dependency = new DataDependency(
                "TestMethod", 
                DataDependencyType.Database, 
                "resource", 
                DataAccessType.Read, 
                _sampleEntityTypes);

            // Assert - All properties should only have getters
            typeof(DataDependency).GetProperty(nameof(DataDependency.TestMethodId))!
                .CanWrite.Should().BeFalse();
            typeof(DataDependency).GetProperty(nameof(DataDependency.DependencyType))!
                .CanWrite.Should().BeFalse();
            typeof(DataDependency).GetProperty(nameof(DataDependency.ResourceIdentifier))!
                .CanWrite.Should().BeFalse();
            typeof(DataDependency).GetProperty(nameof(DataDependency.AccessType))!
                .CanWrite.Should().BeFalse();
            typeof(DataDependency).GetProperty(nameof(DataDependency.EntityTypes))!
                .CanWrite.Should().BeFalse();
            typeof(DataDependency).GetProperty(nameof(DataDependency.DetectedAt))!
                .CanWrite.Should().BeFalse();
        }
    }
}