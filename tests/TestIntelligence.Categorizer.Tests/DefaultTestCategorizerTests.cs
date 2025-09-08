using System;
using System.Reflection;
using FluentAssertions;
using TestIntelligence.Categorizer;
using TestIntelligence.Core.Models;
using TestIntelligence.SelectionEngine.Models;
using Xunit;

namespace TestIntelligence.Categorizer.Tests
{
    public class DefaultTestCategorizerTests
    {
        private readonly DefaultTestCategorizer _categorizer;

        public DefaultTestCategorizerTests()
        {
            _categorizer = new DefaultTestCategorizer();
        }

        [Theory]
        [InlineData("TestMethod", "UserServiceTests", "MyApp.Services.Tests", "MyApp.Tests.dll", TestCategory.Unit)]
        [InlineData("CanCreateUser", "UserTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.Unit)]
        [InlineData("ShouldValidateInput", "ValidationTest", "MyApp.Validation", "MyApp.Tests.dll", TestCategory.Unit)]
        public async Task CategorizeAsync_WithUnitTestPatterns_ReturnsUnitCategory(
            string methodName, string className, string namespaceName, string assemblyName, TestCategory expectedCategory)
        {
            // Arrange
            var testInfo = CreateTestInfo(methodName, className, namespaceName, assemblyName);

            // Act
            var result = await _categorizer.CategorizeAsync(testInfo);

            // Assert
            result.Should().Be(expectedCategory);
        }

        [Theory]
        [InlineData("TestDatabaseConnection", "UserRepositoryTests", "MyApp.Data.Tests", "MyApp.Tests.dll", TestCategory.Database)]
        [InlineData("CanSaveToDb", "UserTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.Database)]
        [InlineData("EfCoreIntegration", "EntityTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.Database)]
        [InlineData("Ef6Migration", "MigrationTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.Database)]
        public async Task CategorizeAsync_WithDatabasePatterns_ReturnsDatabaseCategory(
            string methodName, string className, string namespaceName, string assemblyName, TestCategory expectedCategory)
        {
            // Arrange
            var testInfo = CreateTestInfo(methodName, className, namespaceName, assemblyName);

            // Act
            var result = await _categorizer.CategorizeAsync(testInfo);

            // Assert
            result.Should().Be(expectedCategory);
        }

        [Theory]
        [InlineData("TestApiEndpoint", "UserControllerTests", "MyApp.Controllers.Tests", "MyApp.Tests.dll", TestCategory.API)]
        [InlineData("HttpGetRequest", "ApiTests", "MyApp.Api.Tests", "MyApp.Tests.dll", TestCategory.API)]
        [InlineData("RestEndpoint", "WebTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.API)]
        public async Task CategorizeAsync_WithApiPatterns_ReturnsApiCategory(
            string methodName, string className, string namespaceName, string assemblyName, TestCategory expectedCategory)
        {
            // Arrange
            var testInfo = CreateTestInfo(methodName, className, namespaceName, assemblyName);

            // Act
            var result = await _categorizer.CategorizeAsync(testInfo);

            // Assert
            result.Should().Be(expectedCategory);
        }

        [Theory]
        [InlineData("IntegrationTest", "SystemTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.Integration)]
        [InlineData("TestServiceIntegration", "ServiceTests", "MyApp.Integration.Tests", "MyApp.Integration.Tests.dll", TestCategory.Integration)]
        public async Task CategorizeAsync_WithIntegrationPatterns_ReturnsIntegrationCategory(
            string methodName, string className, string namespaceName, string assemblyName, TestCategory expectedCategory)
        {
            // Arrange
            var testInfo = CreateTestInfo(methodName, className, namespaceName, assemblyName);

            // Act
            var result = await _categorizer.CategorizeAsync(testInfo);

            // Assert
            result.Should().Be(expectedCategory);
        }

        [Theory]
        [InlineData("UiTest", "LoginPageTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.UI)]
        [InlineData("SeleniumTest", "WebTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.UI)]
        [InlineData("BrowserTest", "UiTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.UI)]
        public async Task CategorizeAsync_WithUiPatterns_ReturnsUiCategory(
            string methodName, string className, string namespaceName, string assemblyName, TestCategory expectedCategory)
        {
            // Arrange
            var testInfo = CreateTestInfo(methodName, className, namespaceName, assemblyName);

            // Act
            var result = await _categorizer.CategorizeAsync(testInfo);

            // Assert
            result.Should().Be(expectedCategory);
        }

        [Theory]
        [InlineData("E2eWorkflowTest", "WorkflowTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.EndToEnd)]
        [InlineData("EndToEndScenario", "ScenarioTests", "MyApp.E2E.Tests", "MyApp.Tests.dll", TestCategory.EndToEnd)]
        public async Task CategorizeAsync_WithEndToEndPatterns_ReturnsEndToEndCategory(
            string methodName, string className, string namespaceName, string assemblyName, TestCategory expectedCategory)
        {
            // Arrange
            var testInfo = CreateTestInfo(methodName, className, namespaceName, assemblyName);

            // Act
            var result = await _categorizer.CategorizeAsync(testInfo);

            // Assert
            result.Should().Be(expectedCategory);
        }

        [Theory]
        [InlineData("PerformanceTest", "LoadTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.Performance)]
        [InlineData("LoadTest", "StressTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.Performance)]
        [InlineData("BenchmarkTest", "PerfTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.Performance)]
        public async Task CategorizeAsync_WithPerformancePatterns_ReturnsPerformanceCategory(
            string methodName, string className, string namespaceName, string assemblyName, TestCategory expectedCategory)
        {
            // Arrange
            var testInfo = CreateTestInfo(methodName, className, namespaceName, assemblyName);

            // Act
            var result = await _categorizer.CategorizeAsync(testInfo);

            // Assert
            result.Should().Be(expectedCategory);
        }

        [Theory]
        [InlineData("SecurityTest", "AuthTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.Security)]
        [InlineData("AuthenticationTest", "LoginTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.Security)]
        [InlineData("AuthorizationTest", "PermissionTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.Security)]
        public async Task CategorizeAsync_WithSecurityPatterns_ReturnsSecurityCategory(
            string methodName, string className, string namespaceName, string assemblyName, TestCategory expectedCategory)
        {
            // Arrange
            var testInfo = CreateTestInfo(methodName, className, namespaceName, assemblyName);

            // Act
            var result = await _categorizer.CategorizeAsync(testInfo);

            // Assert
            result.Should().Be(expectedCategory);
        }

        [Theory]
        [InlineData("TestNunitTestDiscovery", "NunitTestDiscoveryTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.Unit)]
        [InlineData("TestServiceMethod", "UserServiceTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.Unit)]
        [InlineData("TestFactoryMethod", "UserFactoryTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.Unit)]
        [InlineData("TestAnalyzerLogic", "CodeAnalyzerTests", "MyApp.Tests", "MyApp.Tests.dll", TestCategory.Unit)]
        public async Task CategorizeAsync_WithSpecificUnitTestPatterns_ReturnsUnitCategory(
            string methodName, string className, string namespaceName, string assemblyName, TestCategory expectedCategory)
        {
            // Arrange
            var testInfo = CreateTestInfo(methodName, className, namespaceName, assemblyName);

            // Act
            var result = await _categorizer.CategorizeAsync(testInfo);

            // Assert
            result.Should().Be(expectedCategory);
        }

        [Fact]
        public async Task CategorizeAsync_WithMultipleTests_ReturnsCorrectCategories()
        {
            // Arrange
            var tests = new[]
            {
                CreateTestInfo("TestMethod", "UserTests", "MyApp.Tests", "MyApp.Tests.dll"),
                CreateTestInfo("TestDatabaseQuery", "DataTests", "MyApp.Tests", "MyApp.Tests.dll"),
                CreateTestInfo("TestApiCall", "WebTests", "MyApp.Tests", "MyApp.Tests.dll")
            };

            // Act
            var results = await _categorizer.CategorizeAsync(tests);

            // Assert
            results.Should().HaveCount(3);
            results.Should().ContainValue(TestCategory.Unit);
            results.Should().ContainValue(TestCategory.Database);
            results.Should().ContainValue(TestCategory.API);
        }

        [Fact]
        public async Task CategorizeAsync_WithEmptyInput_ReturnsEmptyResult()
        {
            // Arrange
            var tests = Array.Empty<TestCategorizationInfo>();

            // Act
            var results = await _categorizer.CategorizeAsync(tests);

            // Assert
            results.Should().BeEmpty();
        }

        [Theory]
        [InlineData("", "", "", "", TestCategory.Unit)] // Empty strings should default to Unit
        [InlineData("SomeRandomMethod", "RandomClass", "Random.Namespace", "Random.dll", TestCategory.Unit)]
        public async Task CategorizeAsync_WithUnrecognizedPatterns_ReturnsUnitCategory(
            string methodName, string className, string namespaceName, string assemblyName, TestCategory expectedCategory)
        {
            // Arrange
            var testInfo = CreateTestInfo(methodName, className, namespaceName, assemblyName);

            // Act
            var result = await _categorizer.CategorizeAsync(testInfo);

            // Assert
            result.Should().Be(expectedCategory);
        }

        private TestCategorizationInfo CreateTestInfo(string methodName, string className, string namespaceName, string assemblyName)
        {
            return new TestCategorizationInfo(methodName, className, namespaceName, assemblyName);
        }
    }
}