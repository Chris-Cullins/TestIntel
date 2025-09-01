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
using Xunit;

namespace TestIntelligence.CLI.Tests.Commands
{
    public class TraceExecutionCommandTests
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
                    
                    // Add default mock services if not provided
                    services.AddSingleton(Substitute.For<ITestExecutionTracer>());
                    
                    configureServices?.Invoke(services);
                });

            var host = hostBuilder.Build();

            // Create a root command with the trace-execution command
            var rootCommand = new RootCommand("TestIntelligence CLI")
            {
                CreateTraceExecutionCommand(host)
            };

            return await rootCommand.InvokeAsync(args);
        }

        private static Command CreateTraceExecutionCommand(IHost host)
        {
            var testOption = new Option<string>(
                name: "--test",
                description: "Test method identifier to trace execution for")
            {
                IsRequired = true
            };

            var solutionOption = new Option<string>(
                name: "--solution",
                description: "Path to solution file or directory")
            {
                IsRequired = true
            };

            var formatOption = new Option<string>(
                name: "--format",
                description: "Output format: json, text",
                getDefaultValue: () => "text");

            var verboseOption = new Option<bool>(
                name: "--verbose",
                description: "Enable verbose output");

            var maxDepthOption = new Option<int>(
                name: "--max-depth",
                description: "Maximum call depth to trace",
                getDefaultValue: () => 20);

            var command = new Command("trace-execution", "Trace all production code executed by a test method")
            {
                testOption,
                solutionOption,
                formatOption,
                verboseOption,
                maxDepthOption
            };

            command.SetHandler(async (string test, string solution, string format, bool verbose, int maxDepth) =>
            {
                var tracer = host.Services.GetRequiredService<ITestExecutionTracer>();

                try
                {
                    Console.WriteLine($"Tracing execution for test method: {test}");
                    Console.WriteLine($"Solution path: {solution}");
                    Console.WriteLine($"Max depth: {maxDepth}");
                    Console.WriteLine();

                    var trace = await tracer.TraceTestExecutionAsync(test, solution);

                    Console.WriteLine($"Found {trace.TotalMethodsCalled} method(s) in execution trace:");
                    Console.WriteLine($"Production methods: {trace.ProductionMethodsCalled}");

                    if (format == "json")
                    {
                        var json = System.Text.Json.JsonSerializer.Serialize(trace, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        Console.WriteLine(json);
                    }
                    else
                    {
                        var productionMethods = trace.ExecutedMethods.Where(em => em.IsProductionCode).ToList();
                        if (productionMethods.Any())
                        {
                            Console.WriteLine("=== PRODUCTION CODE ===");
                            Console.WriteLine();
                            
                            foreach (var method in productionMethods.OrderBy(em => em.CallDepth))
                            {
                                Console.WriteLine($"• {method.ContainingType}.{method.MethodName}");
                                Console.WriteLine($"  Category: {method.Category}");
                                Console.WriteLine($"  Call Depth: {method.CallDepth}");
                                
                                if (verbose)
                                {
                                    Console.WriteLine($"  Call Path: {string.Join(" → ", method.CallPath)}");
                                }
                                
                                Console.WriteLine();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                }
            }, testOption, solutionOption, formatOption, verboseOption, maxDepthOption);

            return command;
        }

        private static ExecutionTrace CreateSampleExecutionTrace(string testMethodId)
        {
            return new ExecutionTrace
            {
                TestMethodId = testMethodId,
                TestMethodName = "ShouldWork",
                TestClassName = "MyApp.Tests.SampleTests",
                ExecutedMethods = new List<ExecutedMethod>
                {
                    new ExecutedMethod
                    {
                        MethodId = "ProductionMethod1",
                        MethodName = "DoWork",
                        ContainingType = "MyApp.Services.WorkerService",
                        FilePath = "/src/WorkerService.cs",
                        LineNumber = 25,
                        CallPath = new[] { testMethodId, "ProductionMethod1" },
                        CallDepth = 1,
                        IsProductionCode = true,
                        Category = MethodCategory.BusinessLogic
                    },
                    new ExecutedMethod
                    {
                        MethodId = "ProductionMethod2",
                        MethodName = "ValidateInput",
                        ContainingType = "MyApp.Services.ValidationService",
                        FilePath = "/src/ValidationService.cs",
                        LineNumber = 42,
                        CallPath = new[] { testMethodId, "ProductionMethod1", "ProductionMethod2" },
                        CallDepth = 2,
                        IsProductionCode = true,
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
        }
    }
}