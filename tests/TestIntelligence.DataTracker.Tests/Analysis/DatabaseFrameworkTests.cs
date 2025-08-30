using FluentAssertions;
using TestIntelligence.DataTracker.Analysis;
using Xunit;

namespace TestIntelligence.DataTracker.Tests.Analysis
{
    public class DatabaseFrameworkTests
    {
        [Fact]
        public void DatabaseFramework_EnumValues_AreCorrect()
        {
            // Assert - Verify all enum values exist and have expected values
            ((int)DatabaseFramework.EntityFramework6).Should().Be(0);
            ((int)DatabaseFramework.EntityFrameworkCore).Should().Be(1);
            ((int)DatabaseFramework.ADONet).Should().Be(2);
            ((int)DatabaseFramework.Dapper).Should().Be(3);
            ((int)DatabaseFramework.NHibernate).Should().Be(4);
        }

        [Theory]
        [InlineData(DatabaseFramework.EntityFramework6, "EntityFramework6")]
        [InlineData(DatabaseFramework.EntityFrameworkCore, "EntityFrameworkCore")]
        [InlineData(DatabaseFramework.ADONet, "ADONet")]
        [InlineData(DatabaseFramework.Dapper, "Dapper")]
        [InlineData(DatabaseFramework.NHibernate, "NHibernate")]
        public void DatabaseFramework_ToString_ReturnsCorrectName(DatabaseFramework framework, string expectedName)
        {
            // Act
            var result = framework.ToString();

            // Assert
            result.Should().Be(expectedName);
        }

        [Fact]
        public void DatabaseFramework_AllValuesAreDefined()
        {
            // Arrange
            var definedValues = System.Enum.GetValues<DatabaseFramework>();

            // Assert
            definedValues.Should().HaveCount(5);
            definedValues.Should().Contain(DatabaseFramework.EntityFramework6);
            definedValues.Should().Contain(DatabaseFramework.EntityFrameworkCore);
            definedValues.Should().Contain(DatabaseFramework.ADONet);
            definedValues.Should().Contain(DatabaseFramework.Dapper);
            definedValues.Should().Contain(DatabaseFramework.NHibernate);
        }
    }
}