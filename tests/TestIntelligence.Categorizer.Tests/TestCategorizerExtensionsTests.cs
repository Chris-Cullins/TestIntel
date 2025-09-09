using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TestIntelligence.Categorizer;
using TestIntelligence.Core.Models;
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
            public Task<TestCategory> CategorizeAsync(
                TestCategorizationInfo testInfo, 
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(TestCategory.Unit);
            }

            public Task<IReadOnlyDictionary<string, TestCategory>> CategorizeAsync(
                IEnumerable<TestCategorizationInfo> tests, 
                CancellationToken cancellationToken = default)
            {
                var result = tests.ToDictionary(t => t.MethodName, _ => TestCategory.Unit);
                return Task.FromResult<IReadOnlyDictionary<string, TestCategory>>(result);
            }
        }
    }
}