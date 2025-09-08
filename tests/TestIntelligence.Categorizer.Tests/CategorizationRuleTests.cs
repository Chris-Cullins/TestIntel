using FluentAssertions;
using TestIntelligence.Categorizer.Models;
using TestIntelligence.SelectionEngine.Models;
using Xunit;

namespace TestIntelligence.Categorizer.Tests
{
    public class CategorizationRuleTests
    {
        [Fact]
        public void CreateMethodNameRule_WithMatchingPattern_ReturnsTrue()
        {
            // Arrange
            var rule = CategorizationRule.CreateMethodNameRule(
                TestCategory.Database, 
                100, 
                "Database method patterns",
                "database", "db");
            var context = new TestMethodContext("TestDatabaseConnection", "UserTests", "MyApp.Tests", "MyApp.dll");

            // Act
            var matches = rule.Matcher(context);

            // Assert
            matches.Should().BeTrue();
            rule.Category.Should().Be(TestCategory.Database);
            rule.Priority.Should().Be(100);
        }

        [Fact]
        public void CreateMethodNameRule_WithNonMatchingPattern_ReturnsFalse()
        {
            // Arrange
            var rule = CategorizationRule.CreateMethodNameRule(
                TestCategory.Database, 
                100, 
                "Database method patterns",
                "database", "db");
            var context = new TestMethodContext("TestApiEndpoint", "UserTests", "MyApp.Tests", "MyApp.dll");

            // Act
            var matches = rule.Matcher(context);

            // Assert
            matches.Should().BeFalse();
        }

        [Fact]
        public void CreateClassNameRule_WithMatchingPattern_ReturnsTrue()
        {
            // Arrange
            var rule = CategorizationRule.CreateClassNameRule(
                TestCategory.API, 
                90, 
                "API class patterns",
                "controller", "api");
            var context = new TestMethodContext("TestMethod", "UserControllerTests", "MyApp.Tests", "MyApp.dll");

            // Act
            var matches = rule.Matcher(context);

            // Assert
            matches.Should().BeTrue();
        }

        [Fact]
        public void CreateNamespaceRule_WithMatchingPattern_ReturnsTrue()
        {
            // Arrange
            var rule = CategorizationRule.CreateNamespaceRule(
                TestCategory.Integration, 
                80, 
                "Integration namespace patterns",
                "integration");
            var context = new TestMethodContext("TestMethod", "UserTests", "MyApp.Integration.Tests", "MyApp.dll");

            // Act
            var matches = rule.Matcher(context);

            // Assert
            matches.Should().BeTrue();
        }

        [Fact]
        public void CreateAssemblyRule_WithMatchingPattern_ReturnsTrue()
        {
            // Arrange
            var rule = CategorizationRule.CreateAssemblyRule(
                TestCategory.EndToEnd, 
                70, 
                "E2E assembly patterns",
                "e2e", "endtoend");
            var context = new TestMethodContext("TestMethod", "UserTests", "MyApp.Tests", "MyApp.E2E.Tests.dll");

            // Act
            var matches = rule.Matcher(context);

            // Assert
            matches.Should().BeTrue();
        }

        [Fact]
        public void CreateCompositeRule_WithMatchingMethodPattern_ReturnsTrue()
        {
            // Arrange
            var rule = CategorizationRule.CreateCompositeRule(
                TestCategory.UI,
                85,
                "UI composite patterns",
                methodPatterns: new[] { "ui", "selenium" },
                classPatterns: new[] { "page", "browser" });
            var context = new TestMethodContext("TestUiElement", "DataTests", "MyApp.Tests", "MyApp.dll");

            // Act
            var matches = rule.Matcher(context);

            // Assert
            matches.Should().BeTrue();
        }

        [Fact]
        public void CreateCompositeRule_WithMatchingClassPattern_ReturnsTrue()
        {
            // Arrange
            var rule = CategorizationRule.CreateCompositeRule(
                TestCategory.UI,
                85,
                "UI composite patterns",
                methodPatterns: new[] { "ui", "selenium" },
                classPatterns: new[] { "page", "browser" });
            var context = new TestMethodContext("TestMethod", "LoginPageTests", "MyApp.Tests", "MyApp.dll");

            // Act
            var matches = rule.Matcher(context);

            // Assert
            matches.Should().BeTrue();
        }

        [Fact]
        public void CreateCompositeRule_WithNoMatchingPatterns_ReturnsFalse()
        {
            // Arrange
            var rule = CategorizationRule.CreateCompositeRule(
                TestCategory.UI,
                85,
                "UI composite patterns",
                methodPatterns: new[] { "ui", "selenium" },
                classPatterns: new[] { "page", "browser" });
            var context = new TestMethodContext("TestMethod", "UserTests", "MyApp.Tests", "MyApp.dll");

            // Act
            var matches = rule.Matcher(context);

            // Assert
            matches.Should().BeFalse();
        }

        [Fact]
        public void CategorizationRule_WithCustomMatcher_WorksCorrectly()
        {
            // Arrange
            var rule = new CategorizationRule(
                TestCategory.Security,
                95,
                "Custom security rule",
                context => context.MethodName.Contains("auth", StringComparison.OrdinalIgnoreCase) &&
                          context.ClassName.EndsWith("tests", StringComparison.OrdinalIgnoreCase));

            var matchingContext = new TestMethodContext("TestAuthentication", "SecurityTests", "MyApp.Tests", "MyApp.dll");
            var nonMatchingContext = new TestMethodContext("TestMethod", "UserTests", "MyApp.Tests", "MyApp.dll");

            // Act & Assert
            rule.Matcher(matchingContext).Should().BeTrue();
            rule.Matcher(nonMatchingContext).Should().BeFalse();
        }

        [Theory]
        [InlineData("DATABASE", "database")]
        [InlineData("Database", "database")]
        [InlineData("db", "db")]
        [InlineData("DB", "db")]
        public void CategorizationRules_AreCaseInsensitive(string input, string pattern)
        {
            // Arrange
            var rule = CategorizationRule.CreateMethodNameRule(
                TestCategory.Database, 
                100, 
                "Database patterns",
                pattern);
            var context = new TestMethodContext($"Test{input}Method", "Tests", "MyApp", "MyApp.dll");

            // Act
            var matches = rule.Matcher(context);

            // Assert
            matches.Should().BeTrue();
        }

        [Fact]
        public void TestMethodContext_HandlesNullValues_Gracefully()
        {
            // Arrange & Act
            var context = new TestMethodContext(null!, null!, null!, null!);

            // Assert
            context.MethodName.Should().Be(string.Empty);
            context.ClassName.Should().Be(string.Empty);
            context.NamespaceName.Should().Be(string.Empty);
            context.AssemblyName.Should().Be(string.Empty);
        }
    }
}