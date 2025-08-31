using System;
using FluentAssertions;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Assembly.Loaders;
using Xunit;

namespace TestIntelligence.Core.Tests.Assembly
{
    public class AssemblyLoaderFactoryTests
    {
        [Fact]
        public void CreateDefault_ShouldReturnCrossFrameworkAssemblyLoader()
        {
            // Act
            var loader = AssemblyLoaderFactory.CreateDefault();

            // Assert
            loader.Should().NotBeNull();
            loader.Should().BeOfType<CrossFrameworkAssemblyLoader>();
        }

        [Fact]
        public void CreateWithConsoleLogging_ShouldReturnCrossFrameworkAssemblyLoaderWithConsoleLogger()
        {
            // Act
            var loader = AssemblyLoaderFactory.CreateWithConsoleLogging();

            // Assert
            loader.Should().NotBeNull();
            loader.Should().BeOfType<CrossFrameworkAssemblyLoader>();
        }

        [Fact]
        public void CreateWithLogger_ShouldReturnCrossFrameworkAssemblyLoaderWithCustomLogger()
        {
            // Arrange
            var customLogger = NullAssemblyLoadLogger.Instance;

            // Act
            var loader = AssemblyLoaderFactory.CreateWithLogger(customLogger);

            // Assert
            loader.Should().NotBeNull();
            loader.Should().BeOfType<CrossFrameworkAssemblyLoader>();
        }

        [Fact]
        public void CreateSilent_ShouldReturnCrossFrameworkAssemblyLoaderWithNullLogger()
        {
            // Act
            var loader = AssemblyLoaderFactory.CreateSilent();

            // Assert
            loader.Should().NotBeNull();
            loader.Should().BeOfType<CrossFrameworkAssemblyLoader>();
        }

        [Theory]
        [InlineData(FrameworkVersion.NetFramework48)]
        [InlineData(FrameworkVersion.NetCore)]
        [InlineData(FrameworkVersion.Net5Plus)]
        [InlineData(FrameworkVersion.NetStandard)]
        public void CreateFrameworkLoader_WithSupportedFramework_ShouldReturnCorrectLoader(FrameworkVersion frameworkVersion)
        {
            // Act
            var loader = AssemblyLoaderFactory.CreateFrameworkLoader(frameworkVersion);

            // Assert
            loader.Should().NotBeNull();
            
            switch (frameworkVersion)
            {
                case FrameworkVersion.NetFramework48:
                    loader.Should().BeOfType<Framework48LoaderCompatible>();
                    break;
                case FrameworkVersion.NetCore:
                    loader.Should().BeOfType<NetCoreLoaderCompatible>();
                    break;
                case FrameworkVersion.Net5Plus:
                    loader.Should().BeOfType<Net5PlusLoaderCompatible>();
                    break;
                case FrameworkVersion.NetStandard:
                    loader.Should().BeOfType<StandardLoader>();
                    break;
            }
        }

        [Fact]
        public void CreateFrameworkLoader_WithUnsupportedFramework_ShouldThrowNotSupportedException()
        {
            // Arrange
            var unsupportedFramework = (FrameworkVersion)999;

            // Act & Assert
            var exception = Assert.Throws<NotSupportedException>(() => 
                AssemblyLoaderFactory.CreateFrameworkLoader(unsupportedFramework));
            
            exception.Message.Should().Contain("Framework version");
            exception.Message.Should().Contain("is not supported");
        }

        [Theory]
        [InlineData(FrameworkVersion.NetFramework48)]
        [InlineData(FrameworkVersion.NetCore)]
        [InlineData(FrameworkVersion.Net5Plus)]
        [InlineData(FrameworkVersion.NetStandard)]
        public void TryCreateFrameworkLoader_WithSupportedFramework_ShouldReturnTrueAndLoader(FrameworkVersion frameworkVersion)
        {
            // Act
            var result = AssemblyLoaderFactory.TryCreateFrameworkLoader(frameworkVersion, out var loader);

            // Assert
            result.Should().BeTrue();
            loader.Should().NotBeNull();
        }

        [Fact]
        public void TryCreateFrameworkLoader_WithUnsupportedFramework_ShouldReturnFalseAndNullLoader()
        {
            // Arrange
            var unsupportedFramework = (FrameworkVersion)999;

            // Act
            var result = AssemblyLoaderFactory.TryCreateFrameworkLoader(unsupportedFramework, out var loader);

            // Assert
            result.Should().BeFalse();
            loader.Should().BeNull();
        }
    }
}