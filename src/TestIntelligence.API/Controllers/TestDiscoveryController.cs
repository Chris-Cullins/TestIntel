using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Discovery;
using TestIntelligence.Core.Models;
using TestIntelligence.Core.Assembly;
using TestIntelligence.API.Models;
using TestIntelligence.SelectionEngine.Models;

namespace TestIntelligence.API.Controllers;

/// <summary>
/// API controller for test discovery operations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class TestDiscoveryController : ControllerBase
{
    private readonly ITestDiscovery _testDiscovery;
    private readonly ILogger<TestDiscoveryController> _logger;

    public TestDiscoveryController(
        ITestDiscovery testDiscovery,
        ILogger<TestDiscoveryController> logger)
    {
        _testDiscovery = testDiscovery ?? throw new ArgumentNullException(nameof(testDiscovery));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Discover tests in a solution or assembly.
    /// </summary>
    [HttpPost("discover")]
    public async Task<ActionResult<TestDiscoveryResponse>> DiscoverTests(
        [FromBody] TestDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Discovering tests in: {Path}", request.Path);

            var response = new TestDiscoveryResponse
            {
                Tests = new List<TestInfo>(),
                Summary = new TestDiscoverySummary(),
                Errors = new List<string>()
            };

            // Check if path exists
            if (!System.IO.File.Exists(request.Path) && !Directory.Exists(request.Path))
            {
                response.Errors.Add($"Path not found: {request.Path}");
                return BadRequest(response);
            }

            try
            {
                // For now, we'll create mock test info since the discovery service
                // in production would integrate with the actual test assemblies
                var mockTests = await CreateMockTestsAsync(request.Path, cancellationToken);
                
                // Apply category filter if specified
                if (request.CategoryFilter?.Any() == true)
                {
                    mockTests = mockTests.Where(t => request.CategoryFilter.Contains(t.Category)).ToList();
                }

                response.Tests = mockTests;
                response.Summary = CreateSummary(mockTests);

                _logger.LogInformation("Discovered {TestCount} tests", mockTests.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during test discovery");
                response.Errors.Add($"Discovery error: {ex.Message}");
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in test discovery endpoint");
            return BadRequest(new { error = "Test discovery failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Get test categories and their definitions.
    /// </summary>
    [HttpGet("categories")]
    public ActionResult<Dictionary<TestCategory, string>> GetTestCategories()
    {
        var categories = new Dictionary<TestCategory, string>
        {
            [TestCategory.Unit] = "Fast, isolated tests that test individual units of code without external dependencies",
            [TestCategory.Integration] = "Tests that verify interactions between multiple components or services",
            [TestCategory.Database] = "Tests that interact with database systems and verify data operations",
            [TestCategory.API] = "Tests that verify API endpoints and HTTP interactions",
            [TestCategory.UI] = "Tests that interact with user interfaces, including web UI and desktop applications"
        };

        return Ok(categories);
    }

    /// <summary>
    /// Health check endpoint for the API.
    /// </summary>
    [HttpGet("health")]
    public ActionResult<object> HealthCheck()
    {
        return Ok(new
        {
            status = "healthy",
            timestamp = DateTimeOffset.UtcNow,
            version = "1.0.0",
            features = new[]
            {
                "test-discovery",
                "test-selection",
                "diff-analysis",
                "confidence-scoring"
            }
        });
    }

    private async Task<List<TestInfo>> CreateMockTestsAsync(string path, CancellationToken cancellationToken)
    {
        // In production, this would use the actual test discovery service
        // For demonstration, create mock tests based on the path
        await Task.Delay(100, cancellationToken); // Simulate analysis time

        var tests = new List<TestInfo>();
        var random = new Random(path.GetHashCode()); // Consistent mock data

        var testNames = new[]
        {
            "UserServiceTests.CreateUser_ValidInput_ReturnsSuccess",
            "UserServiceTests.CreateUser_InvalidEmail_ThrowsException",
            "OrderProcessingTests.ProcessOrder_ValidOrder_UpdatesInventory",
            "DatabaseTests.SaveUser_ValidUser_PersistsToDatabase",
            "ApiIntegrationTests.PostOrder_ValidData_Returns201",
            "UITests.LoginPage_ValidCredentials_RedirectsToDashboard"
        };

        var categories = new[] 
        { 
            TestCategory.Unit, TestCategory.Integration, 
            TestCategory.Database, TestCategory.API, TestCategory.UI 
        };

        for (int i = 0; i < testNames.Length; i++)
        {
            var category = categories[i % categories.Length];
            var executionTime = category switch
            {
                TestCategory.Unit => TimeSpan.FromMilliseconds(random.Next(10, 100)),
                TestCategory.Integration => TimeSpan.FromMilliseconds(random.Next(500, 2000)),
                TestCategory.Database => TimeSpan.FromMilliseconds(random.Next(1000, 5000)),
                TestCategory.API => TimeSpan.FromMilliseconds(random.Next(2000, 8000)),
                TestCategory.UI => TimeSpan.FromMilliseconds(random.Next(5000, 20000)),
                _ => TimeSpan.FromMilliseconds(1000)
            };

            // Create a mock TestMethod for API demonstration
            var mockMethodInfo = typeof(object).GetMethod("ToString")!;
            var mockType = typeof(object);
            var testMethod = new TestMethod(mockMethodInfo, mockType, "MockAssembly.dll", FrameworkVersion.NetCore);
            var testInfo = new TestInfo(testMethod, category, executionTime, random.NextDouble())
            {
                LastExecuted = DateTimeOffset.UtcNow.AddDays(-random.Next(1, 30))
            };

            testInfo.Tags.Add($"{category}Test");
            testInfo.Tags.Add("MockTest");

            tests.Add(testInfo);
        }

        return tests;
    }

    private static TestDiscoverySummary CreateSummary(List<TestInfo> tests)
    {
        var summary = new TestDiscoverySummary
        {
            TotalTests = tests.Count,
            EstimatedTotalDuration = TimeSpan.FromMilliseconds(
                tests.Sum(t => t.AverageExecutionTime.TotalMilliseconds)),
            AssembliesAnalyzed = tests.Select(t => t.TestMethod.ClassName).Distinct().Count()
        };

        foreach (var category in Enum.GetValues<TestCategory>())
        {
            var count = tests.Count(t => t.Category == category);
            if (count > 0)
            {
                summary.CategoryBreakdown[category] = count;
            }
        }

        return summary;
    }
}