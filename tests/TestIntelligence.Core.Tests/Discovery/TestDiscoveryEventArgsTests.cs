using System;
using System.Collections.Generic;
using FluentAssertions;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Discovery;
using TestIntelligence.Core.Models;
using Xunit;

namespace TestIntelligence.Core.Tests.Discovery
{
    public class TestDiscoveryStartedEventArgsTests
    {
        [Fact]
        public void Constructor_ShouldSetPropertiesCorrectly()
        {
            // Arrange
            var assemblyPath = @"C:\test\assembly.dll";
            var beforeTime = DateTimeOffset.UtcNow;

            // Act
            var eventArgs = new TestDiscoveryStartedEventArgs(assemblyPath);

            // Assert
            var afterTime = DateTimeOffset.UtcNow;
            
            eventArgs.AssemblyPath.Should().Be(assemblyPath);
            eventArgs.StartedAt.Should().BeOnOrAfter(beforeTime);
            eventArgs.StartedAt.Should().BeOnOrBefore(afterTime);
        }

        [Fact]
        public void Constructor_WithNullAssemblyPath_ShouldSetNullAssemblyPath()
        {
            // Act
            var eventArgs = new TestDiscoveryStartedEventArgs(null!);

            // Assert
            eventArgs.AssemblyPath.Should().BeNull();
            eventArgs.StartedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Constructor_WithEmptyAssemblyPath_ShouldSetEmptyAssemblyPath()
        {
            // Act
            var eventArgs = new TestDiscoveryStartedEventArgs(string.Empty);

            // Assert
            eventArgs.AssemblyPath.Should().BeEmpty();
            eventArgs.StartedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        }
    }

    public class TestDiscoveryCompletedEventArgsTests
    {
        [Fact]
        public void Constructor_ShouldSetPropertiesCorrectly()
        {
            // Arrange
            var assemblyPath = @"C:\test\assembly.dll";
            var testFixtures = new List<TestFixture>();
            var errors = new List<string>();
            var result = new TestDiscoveryResult(assemblyPath, FrameworkVersion.Net5Plus, testFixtures, errors);
            var beforeTime = DateTimeOffset.UtcNow;

            // Act
            var eventArgs = new TestDiscoveryCompletedEventArgs(assemblyPath, result);

            // Assert
            var afterTime = DateTimeOffset.UtcNow;
            
            eventArgs.AssemblyPath.Should().Be(assemblyPath);
            eventArgs.Result.Should().Be(result);
            eventArgs.CompletedAt.Should().BeOnOrAfter(beforeTime);
            eventArgs.CompletedAt.Should().BeOnOrBefore(afterTime);
        }

        [Fact]
        public void Constructor_WithNullAssemblyPath_ShouldSetNullAssemblyPath()
        {
            // Arrange
            var assemblyPath = @"C:\test\assembly.dll";
            var testFixtures = new List<TestFixture>();
            var errors = new List<string>();
            var result = new TestDiscoveryResult(assemblyPath, FrameworkVersion.Net5Plus, testFixtures, errors);

            // Act
            var eventArgs = new TestDiscoveryCompletedEventArgs(null!, result);

            // Assert
            eventArgs.AssemblyPath.Should().BeNull();
            eventArgs.Result.Should().Be(result);
            eventArgs.CompletedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Constructor_WithNullResult_ShouldSetNullResult()
        {
            // Arrange
            var assemblyPath = @"C:\test\assembly.dll";

            // Act
            var eventArgs = new TestDiscoveryCompletedEventArgs(assemblyPath, null!);

            // Assert
            eventArgs.AssemblyPath.Should().Be(assemblyPath);
            eventArgs.Result.Should().BeNull();
            eventArgs.CompletedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        }
    }

    public class TestDiscoveryErrorEventArgsTests
    {
        [Fact]
        public void Constructor_ShouldSetPropertiesCorrectly()
        {
            // Arrange
            var assemblyPath = @"C:\test\assembly.dll";
            var exception = new InvalidOperationException("Test exception");
            var beforeTime = DateTimeOffset.UtcNow;

            // Act
            var eventArgs = new TestDiscoveryErrorEventArgs(assemblyPath, exception);

            // Assert
            var afterTime = DateTimeOffset.UtcNow;
            
            eventArgs.AssemblyPath.Should().Be(assemblyPath);
            eventArgs.Exception.Should().Be(exception);
            eventArgs.ErrorAt.Should().BeOnOrAfter(beforeTime);
            eventArgs.ErrorAt.Should().BeOnOrBefore(afterTime);
        }

        [Fact]
        public void Constructor_WithNullAssemblyPath_ShouldSetNullAssemblyPath()
        {
            // Arrange
            var exception = new InvalidOperationException("Test exception");

            // Act
            var eventArgs = new TestDiscoveryErrorEventArgs(null!, exception);

            // Assert
            eventArgs.AssemblyPath.Should().BeNull();
            eventArgs.Exception.Should().Be(exception);
            eventArgs.ErrorAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Constructor_WithNullException_ShouldSetNullException()
        {
            // Arrange
            var assemblyPath = @"C:\test\assembly.dll";

            // Act
            var eventArgs = new TestDiscoveryErrorEventArgs(assemblyPath, null!);

            // Assert
            eventArgs.AssemblyPath.Should().Be(assemblyPath);
            eventArgs.Exception.Should().BeNull();
            eventArgs.ErrorAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        }
    }
}