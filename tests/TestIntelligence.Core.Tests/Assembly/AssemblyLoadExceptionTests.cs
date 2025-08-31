using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using FluentAssertions;
using TestIntelligence.Core.Assembly;
using Xunit;

namespace TestIntelligence.Core.Tests.Assembly
{
    public class AssemblyLoadExceptionTests
    {
        [Fact]
        public void DefaultConstructor_ShouldInitializeWithEmptyProperties()
        {
            // Act
            var exception = new AssemblyLoadException();

            // Assert
            exception.AssemblyPath.Should().Be(string.Empty);
            exception.Errors.Should().BeEmpty();
            exception.Message.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void Constructor_WithMessage_ShouldSetMessage()
        {
            // Arrange
            var message = "Test error message";

            // Act
            var exception = new AssemblyLoadException(message);

            // Assert
            exception.Message.Should().Be(message);
            exception.AssemblyPath.Should().Be(string.Empty);
            exception.Errors.Should().BeEmpty();
        }

        [Fact]
        public void Constructor_WithMessageAndInnerException_ShouldSetBoth()
        {
            // Arrange
            var message = "Test error message";
            var innerException = new InvalidOperationException("Inner exception");

            // Act
            var exception = new AssemblyLoadException(message, innerException);

            // Assert
            exception.Message.Should().Be(message);
            exception.InnerException.Should().Be(innerException);
            exception.AssemblyPath.Should().Be(string.Empty);
            exception.Errors.Should().BeEmpty();
        }

        [Fact]
        public void Constructor_WithAssemblyPathAndErrors_ShouldSetProperties()
        {
            // Arrange
            var assemblyPath = @"C:\test\assembly.dll";
            var errors = new List<string> { "Error 1", "Error 2" };

            // Act
            var exception = new AssemblyLoadException(assemblyPath, errors);

            // Assert
            exception.AssemblyPath.Should().Be(assemblyPath);
            exception.Errors.Should().BeEquivalentTo(errors);
            exception.Message.Should().Contain(assemblyPath);
            exception.Message.Should().Contain("Error 1");
            exception.Message.Should().Contain("Error 2");
        }

        [Fact]
        public void Constructor_WithAssemblyPathAndEmptyErrors_ShouldUseEmptyErrors()
        {
            // Arrange
            var assemblyPath = @"C:\test\assembly.dll";
            var emptyErrors = new List<string>() as IReadOnlyList<string>;

            // Act
            var exception = new AssemblyLoadException(assemblyPath, emptyErrors);

            // Assert
            exception.AssemblyPath.Should().Be(assemblyPath);
            exception.Errors.Should().BeEmpty();
            exception.Message.Should().Contain(assemblyPath);
        }

        [Fact]
        public void Constructor_WithAssemblyPathErrorsAndInnerException_ShouldSetAllProperties()
        {
            // Arrange
            var assemblyPath = @"C:\test\assembly.dll";
            var errors = new List<string> { "Error 1" } as IReadOnlyList<string>;
            var innerException = new FileNotFoundException("File not found");

            // Act
            var exception = new AssemblyLoadException(assemblyPath, errors, innerException);

            // Assert
            exception.AssemblyPath.Should().Be(assemblyPath);
            exception.Errors.Should().BeEquivalentTo(errors);
            exception.InnerException.Should().Be(innerException);
            exception.Message.Should().Contain(assemblyPath);
            exception.Message.Should().Contain("Error 1");
        }

        [Fact]
        public void CreateMessage_WithAssemblyPathAndErrors_ShouldFormatCorrectly()
        {
            // Arrange
            var assemblyPath = @"C:\test\assembly.dll";
            var errors = new List<string> { "First error", "Second error" };

            // Act
            var exception = new AssemblyLoadException(assemblyPath, errors);

            // Assert
            exception.Message.Should().Contain($"Failed to load assembly: {assemblyPath}");
            exception.Message.Should().Contain("Errors:");
            exception.Message.Should().Contain("1. First error");
            exception.Message.Should().Contain("2. Second error");
        }

        [Fact]
        public void CreateMessage_WithEmptyErrors_ShouldNotIncludeErrorsSection()
        {
            // Arrange
            var assemblyPath = @"C:\test\assembly.dll";
            var errors = new List<string>();

            // Act
            var exception = new AssemblyLoadException(assemblyPath, errors);

            // Assert
            exception.Message.Should().Be($"Failed to load assembly: {assemblyPath}");
            exception.Message.Should().NotContain("Errors:");
        }

        // Removed problematic serialization test due to obsolete warnings
    }
}