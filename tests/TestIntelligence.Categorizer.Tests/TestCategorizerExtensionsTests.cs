using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TestIntelligence.Categorizer;
using Xunit;

namespace TestIntelligence.Categorizer.Tests
{
    public class TestCategorizerExtensionsTests
    {
        [Fact]
        public void AddTestCategorizer_RegistersDefaultImplementation()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddTestCategorizer();
            var serviceProvider = services.BuildServiceProvider();

            // Assert
            var categorizer = serviceProvider.GetService<ITestCategorizer>();
            categorizer.Should().NotBeNull();
            categorizer.Should().BeOfType<DefaultTestCategorizer>();
        }

        [Fact]
        public void AddTestCategorizer_Generic_RegistersCustomImplementation()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddTestCategorizer<CustomTestCategorizer>();
            var serviceProvider = services.BuildServiceProvider();

            // Assert
            var categorizer = serviceProvider.GetService<ITestCategorizer>();
            categorizer.Should().NotBeNull();
            categorizer.Should().BeOfType<CustomTestCategorizer>();
        }

        [Fact]
        public void AddTestCategorizer_RegistersAsSingleton()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddTestCategorizer();
            var serviceProvider = services.BuildServiceProvider();

            // Assert
            var categorizer1 = serviceProvider.GetService<ITestCategorizer>();
            var categorizer2 = serviceProvider.GetService<ITestCategorizer>();
            
            categorizer1.Should().BeSameAs(categorizer2);
        }

        private class CustomTestCategorizer : ITestCategorizer
        {
            public Task<SelectionEngine.Models.TestCategory> CategorizeAsync(
                SelectionEngine.Models.TestInfo testInfo, 
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(SelectionEngine.Models.TestCategory.Unit);
            }

            public Task<IReadOnlyDictionary<string, SelectionEngine.Models.TestCategory>> CategorizeAsync(
                IEnumerable<SelectionEngine.Models.TestInfo> tests, 
                CancellationToken cancellationToken = default)
            {
                var result = tests.ToDictionary(t => t.TestMethod.MethodInfo.Name, _ => SelectionEngine.Models.TestCategory.Unit);
                return Task.FromResult<IReadOnlyDictionary<string, SelectionEngine.Models.TestCategory>>(result);
            }
        }
    }
}