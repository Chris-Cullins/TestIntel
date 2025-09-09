using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.Core.Interfaces;
using TestIntelligence.Core.Models;
using TestIntelligence.CLI;
using TestIntelligence.CLI.Commands;
using TestIntelligence.CLI.Services;
using Xunit;

namespace TestIntelligence.CLI.Tests.Commands
{
    public class TraceExecutionCommandTests : IDisposable
    {
        private readonly StringWriter _output;
        private readonly StringWriter _error;

        public TraceExecutionCommandTests()
        {
            _output = new StringWriter();
            _error = new StringWriter();
            Console.SetOut(_output);
            Console.SetError(_error);
        }

        [Fact]
        public async Task TraceExecutionCommand_WithValidArguments_ExecutesSuccessfully()
        {
            // Arrange
            var testMethodId = "MyApp.Tests.SampleTests.ShouldWork";
            var solutionPath = "/path/to/solution.sln";
            var mockTracer = Substitute.For<ITestExecutionTracer>();
            var expectedTrace = CreateSampleExecutionTrace(testMethodId);

            mockTracer.TraceTestExecutionAsync(testMethodId, solutionPath, Arg.Any<CancellationToken>())
                .Returns(expectedTrace);

            var args = new[] {
                "trace-execution",
                "--test", testMethodId,
                "--solution", solutionPath
            };

            // Act
            var exitCode = await RunCommandWithMockServices(args, services =>
            {
                services.AddSingleton(mockTracer);
            });

            // Assert
            exitCode.Should().Be(0);
            var output = _output.ToString();
            output.Should().Contain($"Tracing execution for test method: {testMethodId}");
            output.Should().Contain($"Solution path: {solutionPath}");
            output.Should().Contain("Found 2 method(s) in execution trace");
        }

        [Fact]
        public async Task TraceExecutionCommand_WithJsonFormat_OutputsJson()
        {
            // Arrange
            var testMethodId = "MyApp.Tests.SampleTests.ShouldWork";
            var solutionPath = "/path/to/solution.sln";
            var mockTracer = Substitute.For<ITestExecutionTracer>();
            var expectedTrace = CreateSampleExecutionTrace(testMethodId);

            mockTracer.TraceTestExecutionAsync(testMethodId, solutionPath, Arg.Any<CancellationToken>())
                .Returns(expectedTrace);

            var args = new[] {
                "trace-execution",
                "--test", testMethodId,
                "--solution", solutionPath,
                "--format", "json"
            };

            // Act
            var exitCode = await RunCommandWithMockServices(args, services =>
            {
                services.AddSingleton(mockTracer);
            });

            // Assert
            exitCode.Should().Be(0);
            var output = _output.ToString();
            output.Should().Contain("testMethodId");
            output.Should().Contain("executedMethods");
        }

        [Fact]
        public async Task TraceExecutionCommand_WithVerboseFlag_OutputsDetailedInfo()
        {
            // Arrange
            var testMethodId = "MyApp.Tests.SampleTests.ShouldWork";
            var solutionPath = "/path/to/solution.sln";
            var mockTracer = Substitute.For<ITestExecutionTracer>();
            var expectedTrace = CreateSampleExecutionTrace(testMethodId);

            mockTracer.TraceTestExecutionAsync(testMethodId, solutionPath, Arg.Any<CancellationToken>())
                .Returns(expectedTrace);

            var args = new[] {
                "trace-execution",
                "--test", testMethodId,
                "--solution", solutionPath,
                "--verbose"
            };

            // Act
            var exitCode = await RunCommandWithMockServices(args, services =>
            {
                services.AddSingleton(mockTracer);
            });

            // Assert
            exitCode.Should().Be(0);
            var output = _output.ToString();
            output.Should().Contain("Call Path:");
            output.Should().Contain("PRODUCTION CODE");
        }

        [Fact]
        public async Task TraceExecutionCommand_WithMissingTestArgument_ShowsError()
        {
            // Arrange
            var args = new[] {
                "trace-execution",
                "--solution", "/path/to/solution.sln"
            };

            // Act
            var exitCode = await RunCommandWithMockServices(args, _ => { });

            // Assert
            exitCode.Should().NotBe(0);
            var error = _error.ToString();
            error.Should().Contain("required");
        }

        [Fact]
        public async Task TraceExecutionCommand_WithMissingSolutionArgument_ShowsError()
        {
            // Arrange
            var args = new[] {
                "trace-execution",
                "--test", "TestMethod1"
            };

            // Act
            var exitCode = await RunCommandWithMockServices(args, _ => { });

            // Assert
            exitCode.Should().NotBe(0);
            var error = _error.ToString();
            error.Should().Contain("required");
        }

        [Fact]
        public async Task TraceExecutionCommand_WithCustomMaxDepth_PassesParameterCorrectly()
        {
            // Arrange
            var testMethodId = "MyApp.Tests.SampleTests.ShouldWork";
            var solutionPath = "/path/to/solution.sln";
            var customMaxDepth = 15;
            var mockTracer = Substitute.For<ITestExecutionTracer>();
            var expectedTrace = CreateSampleExecutionTrace(testMethodId);

            mockTracer.TraceTestExecutionAsync(testMethodId, solutionPath, Arg.Any<CancellationToken>())
                .Returns(expectedTrace);

            var args = new[] {
                "trace-execution",
                "--test", testMethodId,
                "--solution", solutionPath,
                "--max-depth", customMaxDepth.ToString()
            };

            // Act
            var exitCode = await RunCommandWithMockServices(args, services =>
            {
                services.AddSingleton(mockTracer);
            });

            // Assert
            exitCode.Should().Be(0);
            var output = _output.ToString();
            output.Should().Contain($"Max depth: {customMaxDepth}");
        }

        private async Task<int> RunCommandWithMockServices(string[] args, Action<IServiceCollection> configureServices)
        {
            var hostBuilder = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
                    
                    // Add all command handlers
                    services.AddTransient<AnalyzeCommandHandler>();
                    services.AddTransient<CategorizeCommandHandler>();
                    services.AddTransient<SelectCommandHandler>();
                    services.AddTransient<DiffCommandHandler>();
                    services.AddTransient<CallGraphCommandHandler>();
                    services.AddTransient<FindTestsCommandHandler>();
                    services.AddTransient<TraceExecutionCommandHandler>();
                    services.AddTransient<AnalyzeCoverageCommandHandler>();
                    services.AddTransient<ConfigCommandHandler>();
                    services.AddTransient<CacheCommandHandler>();
                    services.AddTransient<VersionCommandHandler>();
                    services.AddTransient<ICommandFactory, CommandFactory>();
                    
                    // Add default mock services if not provided
                    services.AddSingleton(Substitute.For<ITestExecutionTracer>());
                    services.AddSingleton(Substitute.For<IOutputFormatter>());
                    
                    configureServices?.Invoke(services);
                });

            var host = hostBuilder.Build();

            // Use CommandFactory to create all commands (same as Program.cs)
            var commandFactory = host.Services.GetRequiredService<ICommandFactory>();
            var rootCommand = commandFactory.CreateRootCommand(host);

            return await rootCommand.InvokeAsync(args);
        }

        // Command creation is now handled by CommandFactory and TraceExecutionCommandHandler

        private static ExecutionTrace CreateSampleExecutionTrace(string testMethodId)
        {
            return new ExecutionTrace(
                testMethodId,
                "ShouldWork", 
                "MyApp.Tests.SampleTests")
            {
                ExecutedMethods = new List<ExecutedMethod>
                {
                    new ExecutedMethod(
                        "ProductionMethod1",
                        "DoWork",
                        "MyApp.Services.WorkerService",
                        true)
                    {
                        FilePath = "/src/WorkerService.cs",
                        LineNumber = 25,
                        CallPath = new[] { testMethodId, "ProductionMethod1" },
                        CallDepth = 1,
                        Category = MethodCategory.BusinessLogic
                    },
                    new ExecutedMethod(
                        "ProductionMethod2",
                        "ValidateInput", 
                        "MyApp.Services.ValidationService",
                        true)
                    {
                        FilePath = "/src/ValidationService.cs",
                        LineNumber = 42,
                        CallPath = new[] { testMethodId, "ProductionMethod1", "ProductionMethod2" },
                        CallDepth = 2,
                        Category = MethodCategory.BusinessLogic
                    }
                },
                TotalMethodsCalled = 2,
                ProductionMethodsCalled = 2,
                EstimatedExecutionComplexity = TimeSpan.FromMilliseconds(20),
                TraceTimestamp = DateTime.UtcNow
            };
        }

        public void Dispose()
        {
            _output?.Dispose();
            _error?.Dispose();
            
            // Clean up any configuration files created during tests
            var configPath = Path.Combine(Environment.CurrentDirectory, "testintel.config");
            if (File.Exists(configPath))
            {
                try
                {
                    File.Delete(configPath);
                }
                catch
                {
                    // Ignore cleanup failures
                }
            }
        }
    }
}