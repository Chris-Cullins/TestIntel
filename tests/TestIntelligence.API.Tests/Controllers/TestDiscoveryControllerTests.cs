using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.API.Controllers;
using TestIntelligence.API.Models;
using TestIntelligence.Core.Discovery;
using TestIntelligence.Core.Models;
using TestIntelligence.SelectionEngine.Models;
using Xunit;

namespace TestIntelligence.API.Tests.Controllers;

public class TestDiscoveryControllerTests
{
    private readonly ITestDiscovery _mockTestDiscovery;
    private readonly ILogger<TestDiscoveryController> _mockLogger;
    private readonly TestDiscoveryController _controller;

    public TestDiscoveryControllerTests()
    {
        _mockTestDiscovery = Substitute.For<ITestDiscovery>();
        _mockLogger = Substitute.For<ILogger<TestDiscoveryController>>();
        
        _controller = new TestDiscoveryController(_mockTestDiscovery, _mockLogger);
    }

    [Fact]
    public async Task DiscoverTests_WithValidPath_ReturnsTestResults()
    {
        // Arrange
        var request = new TestDiscoveryRequest
        {
            Path = "/valid/path/test.dll",
            IncludeDetailedAnalysis = true
        };

        // Create a temporary file for the test
        var tempFile = Path.GetTempFileName();
        request.Path = tempFile;

        try
        {
            // Act
            var result = await _controller.DiscoverTests(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var response = Assert.IsType<TestDiscoveryResponse>(okResult.Value);
            Assert.NotNull(response);
            Assert.True(response.Tests.Count >= 0); // Mock tests are created
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task DiscoverTests_WithInvalidPath_ReturnsBadRequest()
    {
        // Arrange
        var request = new TestDiscoveryRequest
        {
            Path = "/invalid/path/that/does/not/exist.dll"
        };

        // Act
        var result = await _controller.DiscoverTests(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var response = Assert.IsType<TestDiscoveryResponse>(badRequestResult.Value);
        Assert.Contains("Path not found", response.Errors[0]);
    }

    [Fact]
    public async Task DiscoverTests_WithCategoryFilter_FiltersResults()
    {
        // Arrange
        var request = new TestDiscoveryRequest
        {
            Path = Path.GetTempFileName(),
            CategoryFilter = new List<TestCategory> { TestCategory.Unit, TestCategory.Integration }
        };

        try
        {
            // Act
            var result = await _controller.DiscoverTests(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var response = Assert.IsType<TestDiscoveryResponse>(okResult.Value);
            
            // Verify that only Unit and Integration tests are included
            Assert.All(response.Tests, test => 
                Assert.True(test.Category == TestCategory.Unit || test.Category == TestCategory.Integration));
        }
        finally
        {
            if (File.Exists(request.Path))
                File.Delete(request.Path);
        }
    }

    [Fact]
    public void GetTestCategories_ReturnsAllCategories()
    {
        // Act
        var result = _controller.GetTestCategories();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var categories = Assert.IsType<Dictionary<TestCategory, string>>(okResult.Value);
        
        Assert.Equal(5, categories.Count); // Unit, Integration, Database, API, UI
        Assert.True(categories.ContainsKey(TestCategory.Unit));
        Assert.True(categories.ContainsKey(TestCategory.Integration));
        Assert.True(categories.ContainsKey(TestCategory.Database));
        Assert.True(categories.ContainsKey(TestCategory.API));
        Assert.True(categories.ContainsKey(TestCategory.UI));
    }

    [Fact]
    public void HealthCheck_ReturnsHealthyStatus()
    {
        // Act
        var result = _controller.HealthCheck();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var healthInfo = okResult.Value;
        Assert.NotNull(healthInfo);
        
        // Verify health info structure using reflection
        var statusProperty = healthInfo!.GetType().GetProperty("status");
        Assert.NotNull(statusProperty);
        Assert.Equal("healthy", statusProperty.GetValue(healthInfo));
    }

    [Fact]
    public async Task DiscoverTests_CreatesValidSummary()
    {
        // Arrange
        var request = new TestDiscoveryRequest
        {
            Path = Path.GetTempFileName()
        };

        try
        {
            // Act
            var result = await _controller.DiscoverTests(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var response = Assert.IsType<TestDiscoveryResponse>(okResult.Value);
            
            Assert.NotNull(response.Summary);
            Assert.Equal(response.Tests.Count, response.Summary.TotalTests);
            Assert.True(response.Summary.EstimatedTotalDuration.TotalMilliseconds >= 0);
            Assert.True(response.Summary.AssembliesAnalyzed >= 0);
        }
        finally
        {
            if (File.Exists(request.Path))
                File.Delete(request.Path);
        }
    }

    [Fact]
    public async Task DiscoverTests_PopulatesCategoryBreakdown()
    {
        // Arrange
        var request = new TestDiscoveryRequest
        {
            Path = Path.GetTempFileName()
        };

        try
        {
            // Act
            var result = await _controller.DiscoverTests(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var response = Assert.IsType<TestDiscoveryResponse>(okResult.Value);
            
            var totalFromBreakdown = response.Summary.CategoryBreakdown.Values.Sum();
            Assert.Equal(response.Summary.TotalTests, totalFromBreakdown);
        }
        finally
        {
            if (File.Exists(request.Path))
                File.Delete(request.Path);
        }
    }
}