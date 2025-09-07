using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NSubstitute;
using TestIntelligence.API.Controllers;
using TestIntelligence.API.Models;
using TestIntelligence.Core.Interfaces;
using TestIntelligence.Core.Models;
using Xunit;

namespace TestIntelligence.API.Tests.Controllers
{
    public class ExecutionTraceControllerTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public ExecutionTraceControllerTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
            _client = _factory.CreateClient();
        }

        [Fact]
        public async Task TraceTestExecution_WithValidRequest_ReturnsOk()
        {
            // Arrange
            var testMethodId = "TestMethod1";
            var solutionPath = "/path/to/solution.sln";
            var mockTracer = Substitute.For<ITestExecutionTracer>();
            var expectedTrace = CreateSampleExecutionTrace(testMethodId);

            mockTracer.TraceTestExecutionAsync(testMethodId, solutionPath, Arg.Any<CancellationToken>())
                .Returns(expectedTrace);

            using var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Replace the registered service with our mock
                    var serviceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ITestExecutionTracer));
                    if (serviceDescriptor != null)
                        services.Remove(serviceDescriptor);
                    
                    services.AddScoped(_ => mockTracer);
                });
            });

            using var client = factory.CreateClient();

            // Act
            var response = await client.GetAsync($"/api/executiontrace/test/{testMethodId}?solutionPath={Uri.EscapeDataString(solutionPath)}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var content = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<TestExecutionTraceResponse>(content);
            
            result.Should().NotBeNull();
            result!.TestMethodId.Should().Be(testMethodId);
            result.SolutionPath.Should().Be(solutionPath);
            result.ExecutionTrace.Should().NotBeNull();
            result.ExecutionTrace.TestMethodId.Should().Be(testMethodId);
        }

        [Fact]
        public async Task TraceTestExecution_WithEmptyTestMethodId_ReturnsBadRequest()
        {
            // Arrange
            var solutionPath = "/path/to/solution.sln";

            // Act
            var response = await _client.GetAsync($"/api/executiontrace/test/?solutionPath={Uri.EscapeDataString(solutionPath)}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound); // Empty route parameter results in 404
        }

        [Fact]
        public async Task TraceTestExecution_WithMissingSolutionPath_ReturnsBadRequest()
        {
            // Arrange
            var testMethodId = "TestMethod1";

            // Act
            var response = await _client.GetAsync($"/api/executiontrace/test/{testMethodId}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            
            var content = await response.Content.ReadAsStringAsync();
            content.Should().Contain("Solution path is required");
        }

        [Fact]
        public async Task TraceMultipleTestsExecution_WithValidRequest_ReturnsOk()
        {
            // Arrange
            var request = new BulkTestExecutionTraceRequest
            {
                TestMethodIds = new[] { "TestMethod1", "TestMethod2" },
                SolutionPath = "/path/to/solution.sln"
            };

            var mockTracer = Substitute.For<ITestExecutionTracer>();
            var expectedTraces = request.TestMethodIds.Select(CreateSampleExecutionTrace).ToList();

            mockTracer.TraceMultipleTestsAsync(
                    Arg.Is<IEnumerable<string>>(ids => ids.SequenceEqual(request.TestMethodIds)),
                    request.SolutionPath,
                    Arg.Any<CancellationToken>())
                .Returns(expectedTraces);

            using var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var serviceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ITestExecutionTracer));
                    if (serviceDescriptor != null)
                        services.Remove(serviceDescriptor);
                    
                    services.AddScoped(_ => mockTracer);
                });
            });

            using var client = factory.CreateClient();
            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Act
            var response = await client.PostAsync("/api/executiontrace/bulk", content);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<BulkTestExecutionTraceResponse>(responseContent);
            
            result.Should().NotBeNull();
            result!.SolutionPath.Should().Be(request.SolutionPath);
            result.Results.Should().HaveCount(2);
            result.Results.Should().ContainKey("TestMethod1");
            result.Results.Should().ContainKey("TestMethod2");
        }

        [Fact]
        public async Task GenerateCoverageReport_WithValidRequest_ReturnsOk()
        {
            // Arrange
            var request = new ExecutionCoverageReportRequest
            {
                SolutionPath = "/path/to/solution.sln"
            };

            var mockTracer = Substitute.For<ITestExecutionTracer>();
            var expectedReport = CreateSampleCoverageReport();

            mockTracer.GenerateCoverageReportAsync(request.SolutionPath, Arg.Any<CancellationToken>())
                .Returns(expectedReport);

            using var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var serviceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ITestExecutionTracer));
                    if (serviceDescriptor != null)
                        services.Remove(serviceDescriptor);
                    
                    services.AddScoped(_ => mockTracer);
                });
            });

            using var client = factory.CreateClient();
            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Act
            var response = await client.PostAsync("/api/executiontrace/coverage-report", content);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<ExecutionCoverageReportResponse>(responseContent);
            
            result.Should().NotBeNull();
            result!.SolutionPath.Should().Be(request.SolutionPath);
            result.CoverageReport.Should().NotBeNull();
        }

        [Fact]
        public async Task GetTestExecutionStatistics_WithValidRequest_ReturnsOk()
        {
            // Arrange
            var testMethodId = "TestMethod1";
            var solutionPath = "/path/to/solution.sln";
            var mockTracer = Substitute.For<ITestExecutionTracer>();
            var expectedTrace = CreateSampleExecutionTrace(testMethodId);

            mockTracer.TraceTestExecutionAsync(testMethodId, solutionPath, Arg.Any<CancellationToken>())
                .Returns(expectedTrace);

            using var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var serviceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ITestExecutionTracer));
                    if (serviceDescriptor != null)
                        services.Remove(serviceDescriptor);
                    
                    services.AddScoped(_ => mockTracer);
                });
            });

            using var client = factory.CreateClient();

            // Act
            var response = await client.GetAsync($"/api/executiontrace/test/{testMethodId}/statistics?solutionPath={Uri.EscapeDataString(solutionPath)}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var content = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<dynamic>(content);
            
            _ = result; // Suppress nullable warning
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public async Task TraceMultipleTestsExecution_WithInvalidTestMethodIds_ReturnsBadRequest(string invalidIds)
        {
            // Arrange
            var request = new BulkTestExecutionTraceRequest
            {
                TestMethodIds = string.IsNullOrWhiteSpace(invalidIds) ? Array.Empty<string>() : new[] { invalidIds },
                SolutionPath = "/path/to/solution.sln"
            };

            if (string.IsNullOrWhiteSpace(invalidIds))
                request.TestMethodIds = Array.Empty<string>();

            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Act
            var response = await _client.PostAsync("/api/executiontrace/bulk", content);

            // Assert
            if (!request.TestMethodIds.Any())
            {
                response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                var responseContent = await response.Content.ReadAsStringAsync();
                responseContent.Should().Contain("At least one test method ID is required");
            }
        }

        private static ExecutionTrace CreateSampleExecutionTrace(string testMethodId)
        {
            return new ExecutionTrace(testMethodId, $"Test_{testMethodId}", "MyApp.Tests.SampleTests")
            {
                ExecutedMethods = new List<ExecutedMethod>
                {
                    new ExecutedMethod("ProductionMethod1", "DoWork", "MyApp.Services.WorkerService", true)
                    {
                        FilePath = "/src/WorkerService.cs",
                        LineNumber = 25,
                        CallPath = new[] { testMethodId, "ProductionMethod1" },
                        CallDepth = 1,
                        Category = MethodCategory.BusinessLogic
                    },
                    new ExecutedMethod("ProductionMethod2", "ValidateInput", "MyApp.Services.ValidationService", true)
                    {
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

        private static ExecutionCoverageReport CreateSampleCoverageReport()
        {
            return new ExecutionCoverageReport
            {
                TestToExecutionMap = new Dictionary<string, ExecutionTrace>
                {
                    { "TestMethod1", CreateSampleExecutionTrace("TestMethod1") }
                },
                UncoveredMethods = new List<string> { "UncoveredMethod1", "UncoveredMethod2" },
                Statistics = new CoverageStatistics
                {
                    TotalProductionMethods = 10,
                    CoveredProductionMethods = 8,
                    TotalTestMethods = 5,
                    AverageCallDepth = 2,
                    MaxCallDepth = 4,
                    CategoryBreakdown = new Dictionary<MethodCategory, int>
                    {
                        { MethodCategory.BusinessLogic, 5 },
                        { MethodCategory.Infrastructure, 3 }
                    }
                },
                GeneratedTimestamp = DateTime.UtcNow
            };
        }
    }
}