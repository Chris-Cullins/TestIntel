using System;
using System.IO;
using FluentAssertions;
using TestIntelligence.Core.Assembly;
using Xunit;

namespace TestIntelligence.Core.Tests.Assembly
{
    public class ConsoleAssemblyLoadLoggerTests
    {
        [Fact]
        public void LogInformation_ShouldWriteToConsole()
        {
            // Arrange
            var logger = new ConsoleAssemblyLoadLogger();
            var originalOut = Console.Out;
            
            try
            {
                using var writer = new StringWriter();
                Console.SetOut(writer);

                // Act
                logger.LogInformation("Test information message");

                // Assert
                var output = writer.ToString();
                output.Should().Contain("[INFO]");
                output.Should().Contain("[AssemblyLoader]");
                output.Should().Contain("Test information message");
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void LogInformation_WithArguments_ShouldFormatMessage()
        {
            // Arrange
            var logger = new ConsoleAssemblyLoadLogger();
            var originalOut = Console.Out;
            
            try
            {
                using var writer = new StringWriter();
                Console.SetOut(writer);

                // Act
                logger.LogInformation("Test message with {0} and {1}", "arg1", 42);

                // Assert
                var output = writer.ToString();
                output.Should().Contain("[INFO]");
                output.Should().Contain("Test message with arg1 and 42");
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void LogWarning_ShouldWriteToConsole()
        {
            // Arrange
            var logger = new ConsoleAssemblyLoadLogger();
            var originalOut = Console.Out;
            
            try
            {
                using var writer = new StringWriter();
                Console.SetOut(writer);

                // Act
                logger.LogWarning("Test warning message");

                // Assert
                var output = writer.ToString();
                output.Should().Contain("[WARN]");
                output.Should().Contain("[AssemblyLoader]");
                output.Should().Contain("Test warning message");
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void LogError_ShouldWriteToConsole()
        {
            // Arrange
            var logger = new ConsoleAssemblyLoadLogger();
            var originalOut = Console.Out;
            
            try
            {
                using var writer = new StringWriter();
                Console.SetOut(writer);

                // Act
                logger.LogError("Test error message");

                // Assert
                var output = writer.ToString();
                output.Should().Contain("[ERROR]");
                output.Should().Contain("[AssemblyLoader]");
                output.Should().Contain("Test error message");
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void LogError_WithException_ShouldWriteErrorAndException()
        {
            // Arrange
            var logger = new ConsoleAssemblyLoadLogger();
            var exception = new InvalidOperationException("Test exception");
            var originalOut = Console.Out;
            
            try
            {
                using var writer = new StringWriter();
                Console.SetOut(writer);

                // Act
                logger.LogError(exception, "Test error message");

                // Assert
                var output = writer.ToString();
                output.Should().Contain("[ERROR]");
                output.Should().Contain("Test error message");
                output.Should().Contain("Exception:");
                output.Should().Contain("Test exception");
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void LogDebug_InDebugBuild_ShouldWriteToConsole()
        {
            // Arrange
            var logger = new ConsoleAssemblyLoadLogger();
            var originalOut = Console.Out;
            
            try
            {
                using var writer = new StringWriter();
                Console.SetOut(writer);

                // Act
                logger.LogDebug("Test debug message");

                // Assert
                var output = writer.ToString();
#if DEBUG
                output.Should().Contain("[DEBUG]");
                output.Should().Contain("Test debug message");
#else
                output.Should().BeEmpty();
#endif
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void WriteLog_ShouldIncludeTimestamp()
        {
            // Arrange
            var logger = new ConsoleAssemblyLoadLogger();
            var originalOut = Console.Out;
            
            try
            {
                using var writer = new StringWriter();
                Console.SetOut(writer);
                var beforeTime = DateTime.Now;

                // Act
                logger.LogInformation("Test message");

                // Assert
                var output = writer.ToString();
                var afterTime = DateTime.Now;
                
                output.Should().MatchRegex(@"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\]");
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }

    public class NullAssemblyLoadLoggerTests
    {
        [Fact]
        public void Instance_ShouldReturnSameSingletonInstance()
        {
            // Act
            var instance1 = NullAssemblyLoadLogger.Instance;
            var instance2 = NullAssemblyLoadLogger.Instance;

            // Assert
            instance1.Should().NotBeNull();
            instance1.Should().BeSameAs(instance2);
        }

        [Fact]
        public void LogInformation_ShouldNotThrow()
        {
            // Arrange
            var logger = NullAssemblyLoadLogger.Instance;

            // Act & Assert
            var action = () => logger.LogInformation("Test message");
            action.Should().NotThrow();
        }

        [Fact]
        public void LogInformation_WithArguments_ShouldNotThrow()
        {
            // Arrange
            var logger = NullAssemblyLoadLogger.Instance;

            // Act & Assert
            var action = () => logger.LogInformation("Test message {0}", "arg");
            action.Should().NotThrow();
        }

        [Fact]
        public void LogWarning_ShouldNotThrow()
        {
            // Arrange
            var logger = NullAssemblyLoadLogger.Instance;

            // Act & Assert
            var action = () => logger.LogWarning("Test warning");
            action.Should().NotThrow();
        }

        [Fact]
        public void LogError_ShouldNotThrow()
        {
            // Arrange
            var logger = NullAssemblyLoadLogger.Instance;

            // Act & Assert
            var action = () => logger.LogError("Test error");
            action.Should().NotThrow();
        }

        [Fact]
        public void LogError_WithException_ShouldNotThrow()
        {
            // Arrange
            var logger = NullAssemblyLoadLogger.Instance;
            var exception = new Exception("Test exception");

            // Act & Assert
            var action = () => logger.LogError(exception, "Test error");
            action.Should().NotThrow();
        }

        [Fact]
        public void LogDebug_ShouldNotThrow()
        {
            // Arrange
            var logger = NullAssemblyLoadLogger.Instance;

            // Act & Assert
            var action = () => logger.LogDebug("Test debug");
            action.Should().NotThrow();
        }
    }
}