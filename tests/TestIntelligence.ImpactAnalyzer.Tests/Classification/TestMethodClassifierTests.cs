using System;
using System.Linq;
using TestIntelligence.Core.Models;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Classification;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Classification
{
    public class TestMethodClassifierTests
    {
        private readonly TestMethodClassifier _classifier;

        public TestMethodClassifierTests()
        {
            _classifier = new TestMethodClassifier();
        }

        [Theory]
        [InlineData("TestSomething")]
        [InlineData("TestCanDoSomething")]
        [InlineData("ShouldReturnCorrectValue")]
        [InlineData("WhenConditionIsMet")]
        [InlineData("GivenValidInput")]
        [InlineData("ScenarioWithValidData")]
        [InlineData("ExampleOfCorrectUsage")]
        [InlineData("VerifyCorrectBehavior")]
        [InlineData("CheckValidation")]
        [InlineData("EnsureCorrectOutput")]
        [InlineData("CalculateTests")]
        [InlineData("DoSomethingTest")]
        public void IsTestMethod_WithTestMethodNames_ReturnsTrue(string methodName)
        {
            // Arrange
            var methodInfo = CreateMethodInfo(methodName, "SomeClass", "/path/to/tests/SomeTests.cs");

            // Act
            var result = _classifier.IsTestMethod(methodInfo);

            // Assert
            Assert.True(result, $"Method '{methodName}' should be classified as a test method");
        }

        [Theory]
        [InlineData("CalculateTotalAmount")]
        [InlineData("ProcessPayment")]
        [InlineData("ValidateInput")]
        [InlineData("GetUserDetails")]
        [InlineData("SaveToDatabase")]
        public void IsTestMethod_WithProductionMethodNames_InProductionCode_ReturnsFalse(string methodName)
        {
            // Arrange
            var methodInfo = CreateMethodInfo(methodName, "BusinessLogic", "/src/MyApp/BusinessLogic.cs");

            // Act
            var result = _classifier.IsTestMethod(methodInfo);

            // Assert
            Assert.False(result, $"Method '{methodName}' in production code should not be classified as a test method");
        }

        [Theory]
        [InlineData("/tests/UnitTests/SomeTests.cs")]
        [InlineData("/src/tests/IntegrationTests/DatabaseTests.cs")]
        [InlineData("/MyApp.Tests/ServiceTests.cs")]
        [InlineData("/MyApp.UnitTests/CalculatorTests.cs")]
        public void IsTestMethod_WithTestMethodNamesInTestPaths_ReturnsTrue(string filePath)
        {
            // Arrange
            var methodInfo = CreateMethodInfo("ShouldCalculateCorrectly", "CalculatorTests", filePath);

            // Act
            var result = _classifier.IsTestMethod(methodInfo);

            // Assert
            Assert.True(result, $"Test method in path '{filePath}' should be classified as a test method");
        }

        [Theory]
        [InlineData("/tests/UnitTests/CalculatorTests.cs", TestType.Unit)]
        [InlineData("/tests/IntegrationTests/DatabaseTests.cs", TestType.Integration)]
        [InlineData("/tests/E2E/UserJourneyTests.cs", TestType.End2End)]
        [InlineData("/tests/Performance/LoadTests.cs", TestType.Performance)]
        [InlineData("/tests/Security/AuthenticationTests.cs", TestType.Security)]
        public void ClassifyTestType_WithSpecificTestPaths_ReturnsCorrectType(string filePath, TestType expectedType)
        {
            // Arrange
            var methodInfo = CreateMethodInfo("TestSomething", "SomeTests", filePath);

            // Act
            var result = _classifier.ClassifyTestType(methodInfo);

            // Assert
            Assert.Equal(expectedType, result);
        }

        [Theory]
        [InlineData("TestIntegrationWithDatabase", TestType.Integration)]
        [InlineData("TestE2EUserFlow", TestType.End2End)]
        [InlineData("TestEndToEndScenario", TestType.End2End)]
        [InlineData("BenchmarkPerformance", TestType.Performance)]
        [InlineData("TestSecurityValidation", TestType.Security)]
        [InlineData("TestAuthenticationFlow", TestType.Security)]
        public void ClassifyTestType_WithSpecificMethodNames_ReturnsCorrectType(string methodName, TestType expectedType)
        {
            // Arrange
            var methodInfo = CreateMethodInfo(methodName, "SomeTests", "/tests/SomeTests.cs");

            // Act
            var result = _classifier.ClassifyTestType(methodInfo);

            // Assert
            Assert.Equal(expectedType, result);
        }

        [Fact]
        public void ClassifyTestType_WithRegularTestMethod_ReturnsUnit()
        {
            // Arrange
            var methodInfo = CreateMethodInfo("TestCalculation", "CalculatorTests", "/tests/CalculatorTests.cs");

            // Act
            var result = _classifier.ClassifyTestType(methodInfo);

            // Assert
            Assert.Equal(TestType.Unit, result);
        }

        [Fact]
        public void CalculateTestConfidence_WithTestNameInTestPath_ReturnsHighConfidence()
        {
            // Arrange
            var methodInfo = CreateMethodInfo("TestSomething", "SomeTests", "/tests/SomeTests.cs");

            // Act
            var confidence = _classifier.CalculateTestConfidence(methodInfo);

            // Assert
            Assert.True(confidence >= 0.9, $"Expected high confidence (>= 0.9) but got {confidence}");
        }

        [Fact]
        public void CalculateTestConfidence_WithNonTestMethod_ReturnsLowConfidence()
        {
            // Arrange
            var methodInfo = CreateMethodInfo("ProcessPayment", "PaymentProcessor", "/src/BusinessLogic.cs");

            // Act
            var confidence = _classifier.CalculateTestConfidence(methodInfo);

            // Assert
            Assert.True(confidence <= 0.3, $"Expected low confidence (<= 0.3) but got {confidence}");
        }

        [Fact]
        public void GetTestMethods_WithMixedMethods_ReturnsOnlyTestMethods()
        {
            // Arrange
            var methods = new[]
            {
                CreateMethodInfo("TestSomething", "Tests", "/tests/SomeTests.cs"),
                CreateMethodInfo("ProcessPayment", "Service", "/src/PaymentService.cs"),
                CreateMethodInfo("ShouldValidate", "Tests", "/tests/ValidationTests.cs"),
                CreateMethodInfo("CalculateTotal", "Calculator", "/src/Calculator.cs")
            };

            // Act
            var testMethods = _classifier.GetTestMethods(methods);

            // Assert
            Assert.Equal(2, testMethods.Count);
            Assert.Contains(testMethods, m => m.Name == "TestSomething");
            Assert.Contains(testMethods, m => m.Name == "ShouldValidate");
        }

        [Fact]
        public void GroupTestMethodsByType_WithVariousTestTypes_GroupsCorrectly()
        {
            // Arrange
            var testMethods = new[]
            {
                CreateMethodInfo("TestUnit", "UnitTests", "/tests/UnitTests.cs"),
                CreateMethodInfo("TestIntegration", "IntegrationTests", "/tests/IntegrationTests.cs"),
                CreateMethodInfo("TestE2E", "E2ETests", "/tests/E2ETests.cs"),
                CreateMethodInfo("TestAnotherUnit", "MoreUnitTests", "/tests/MoreUnitTests.cs")
            };

            // Act
            var grouped = _classifier.GroupTestMethodsByType(testMethods);

            // Assert
            Assert.Equal(3, grouped.Count); // Unit, Integration, E2E
            Assert.Equal(2, grouped[TestType.Unit].Count);
            Assert.Single(grouped[TestType.Integration]);
            Assert.Single(grouped[TestType.End2End]);
        }

        [Fact]
        public void IsTestMethod_WithNullMethodInfo_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _classifier.IsTestMethod(null!));
        }

        [Fact]
        public void ClassifyTestType_WithNullMethodInfo_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _classifier.ClassifyTestType(null!));
        }

        [Fact]
        public void CalculateTestConfidence_WithNullMethodInfo_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _classifier.CalculateTestConfidence(null!));
        }

        private MethodInfo CreateMethodInfo(string methodName, string containingType, string filePath)
        {
            return new MethodInfo(
                id: $"{containingType}.{methodName}()",
                name: methodName,
                containingType: containingType,
                filePath: filePath,
                lineNumber: 1);
        }
    }
}