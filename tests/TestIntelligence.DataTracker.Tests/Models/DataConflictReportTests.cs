using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using TestIntelligence.DataTracker.Models;
using Xunit;

namespace TestIntelligence.DataTracker.Tests.Models
{
    public class DataConflictReportTests
    {
        private readonly List<DataDependency> _sampleDependencies;
        private readonly List<DataConflict> _sampleConflicts;

        public DataConflictReportTests()
        {
            _sampleDependencies = new List<DataDependency>
            {
                new("TestMethodA", DataDependencyType.Database, "EF6:TestContext", DataAccessType.Write, new[] { "User" }),
                new("TestMethodB", DataDependencyType.Database, "EF6:TestContext", DataAccessType.Read, new[] { "User" }),
                new("TestMethodC", DataDependencyType.FileSystem, "TestFile.txt", DataAccessType.ReadWrite, new[] { "File" })
            };

            _sampleConflicts = new List<DataConflict>
            {
                new("TestMethodA", "TestMethodB", ConflictType.SharedData, "Share database", _sampleDependencies.Take(2).ToList()),
                new("TestMethodB", "TestMethodC", ConflictType.ExclusiveResource, "Exclusive access", new[] { _sampleDependencies[2] }),
                new("TestMethodA", "TestMethodC", ConflictType.PotentialRaceCondition, "Race condition", Array.Empty<DataDependency>())
            };
        }

        [Fact]
        public void Constructor_WithValidParameters_SetsPropertiesCorrectly()
        {
            // Arrange
            const string assemblyPath = "/path/to/test.dll";

            // Act
            var report = new DataConflictReport(assemblyPath, _sampleConflicts, _sampleDependencies);

            // Assert
            report.AssemblyPath.Should().Be(assemblyPath);
            report.Conflicts.Should().BeEquivalentTo(_sampleConflicts);
            report.Dependencies.Should().BeEquivalentTo(_sampleDependencies);
            report.GeneratedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Constructor_WithNullAssemblyPath_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => new DataConflictReport(null!, _sampleConflicts, _sampleDependencies);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("assemblyPath");
        }

        [Fact]
        public void Constructor_WithNullConflicts_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => new DataConflictReport("assembly.dll", null!, _sampleDependencies);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("conflicts");
        }

        [Fact]
        public void Constructor_WithNullDependencies_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => new DataConflictReport("assembly.dll", _sampleConflicts, null!);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("dependencies");
        }

        [Fact]
        public void HighSeverityConflictCount_ReturnsCorrectCount()
        {
            // Act
            var report = new DataConflictReport("assembly.dll", _sampleConflicts, _sampleDependencies);

            // Assert
            // SharedData and ExclusiveResource have High severity
            report.HighSeverityConflictCount.Should().Be(2);
        }

        [Fact]
        public void ParallelBlockingConflictCount_ReturnsCorrectCount()
        {
            // Act
            var report = new DataConflictReport("assembly.dll", _sampleConflicts, _sampleDependencies);

            // Assert
            // SharedData and ExclusiveResource prevent parallel execution
            report.ParallelBlockingConflictCount.Should().Be(2);
        }

        [Theory]
        [InlineData(ConflictSeverity.High, 2)]
        [InlineData(ConflictSeverity.Medium, 0)]
        [InlineData(ConflictSeverity.Low, 1)]
        public void GetConflictsBySeverity_ReturnsCorrectConflicts(ConflictSeverity severity, int expectedCount)
        {
            // Arrange
            var report = new DataConflictReport("assembly.dll", _sampleConflicts, _sampleDependencies);

            // Act
            var conflicts = report.GetConflictsBySeverity(severity).ToList();

            // Assert
            conflicts.Should().HaveCount(expectedCount);
            if (expectedCount > 0)
            {
                conflicts.Should().OnlyContain(c => c.Severity == severity);
            }
        }

        [Fact]
        public void GetTestMethodsWithDependencies_ReturnsUniqueTestMethods()
        {
            // Arrange
            var report = new DataConflictReport("assembly.dll", _sampleConflicts, _sampleDependencies);

            // Act
            var testMethods = report.GetTestMethodsWithDependencies().ToList();

            // Assert
            testMethods.Should().HaveCount(3);
            testMethods.Should().BeEquivalentTo(new[] { "TestMethodA", "TestMethodB", "TestMethodC" });
        }

        [Fact]
        public void GetTestMethodsWithDependencies_WithNoDependencies_ReturnsEmptyCollection()
        {
            // Arrange
            var report = new DataConflictReport("assembly.dll", _sampleConflicts, Array.Empty<DataDependency>());

            // Act
            var testMethods = report.GetTestMethodsWithDependencies().ToList();

            // Assert
            testMethods.Should().BeEmpty();
        }

        [Fact]
        public void ToString_ReturnsExpectedFormat()
        {
            // Arrange
            var report = new DataConflictReport("assembly.dll", _sampleConflicts, _sampleDependencies);

            // Act
            var result = report.ToString();

            // Assert
            result.Should().Be("Data conflict report: 3 conflicts, 3 dependencies");
        }

        [Fact]
        public void Properties_AreImmutable()
        {
            // Arrange
            var report = new DataConflictReport("assembly.dll", _sampleConflicts, _sampleDependencies);

            // Assert - All properties should only have getters
            typeof(DataConflictReport).GetProperty(nameof(DataConflictReport.AssemblyPath))!
                .CanWrite.Should().BeFalse();
            typeof(DataConflictReport).GetProperty(nameof(DataConflictReport.Conflicts))!
                .CanWrite.Should().BeFalse();
            typeof(DataConflictReport).GetProperty(nameof(DataConflictReport.Dependencies))!
                .CanWrite.Should().BeFalse();
            typeof(DataConflictReport).GetProperty(nameof(DataConflictReport.GeneratedAt))!
                .CanWrite.Should().BeFalse();
        }

        [Fact]
        public void Collections_AreReadOnly()
        {
            // Arrange
            var report = new DataConflictReport("assembly.dll", _sampleConflicts, _sampleDependencies);

            // Assert
            report.Conflicts.Should().BeAssignableTo<IReadOnlyList<DataConflict>>();
            report.Dependencies.Should().BeAssignableTo<IReadOnlyList<DataDependency>>();
        }

        [Fact]
        public void GeneratedAt_IsSetToCurrentTime()
        {
            // Arrange
            var beforeCreation = DateTimeOffset.UtcNow;

            // Act
            var report = new DataConflictReport("assembly.dll", _sampleConflicts, _sampleDependencies);

            var afterCreation = DateTimeOffset.UtcNow;

            // Assert
            report.GeneratedAt.Should().BeOnOrAfter(beforeCreation);
            report.GeneratedAt.Should().BeOnOrBefore(afterCreation);
        }

        [Fact]
        public void Constructor_WithEmptyCollections_AllowsEmptyCollections()
        {
            // Act
            var report = new DataConflictReport("assembly.dll", Array.Empty<DataConflict>(), Array.Empty<DataDependency>());

            // Assert
            report.Conflicts.Should().BeEmpty();
            report.Dependencies.Should().BeEmpty();
            report.HighSeverityConflictCount.Should().Be(0);
            report.ParallelBlockingConflictCount.Should().Be(0);
        }

        [Fact]
        public void GetConflictsBySeverity_WithNoMatchingConflicts_ReturnsEmptyCollection()
        {
            // Arrange
            var highSeverityConflicts = new List<DataConflict>
            {
                new("TestA", "TestB", ConflictType.SharedData, "reason", Array.Empty<DataDependency>())
            };
            var report = new DataConflictReport("assembly.dll", highSeverityConflicts, _sampleDependencies);

            // Act
            var mediumSeverityConflicts = report.GetConflictsBySeverity(ConflictSeverity.Medium).ToList();

            // Assert
            mediumSeverityConflicts.Should().BeEmpty();
        }
    }
}