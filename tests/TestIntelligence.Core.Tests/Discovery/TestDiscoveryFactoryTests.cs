using FluentAssertions;
using TestIntelligence.Core.Discovery;
using Xunit;

namespace TestIntelligence.Core.Tests.Discovery
{
    public class TestDiscoveryFactoryTests
    {
        [Fact]
        public void CreateNUnitTestDiscovery_ShouldReturnNUnitTestDiscoveryInstance()
        {
            // Act
            var discovery = TestDiscoveryFactory.CreateNUnitTestDiscovery();

            // Assert
            discovery.Should().NotBeNull();
            discovery.Should().BeOfType<NUnitTestDiscovery>();
            discovery.Should().BeAssignableTo<ITestDiscovery>();
        }
    }
}