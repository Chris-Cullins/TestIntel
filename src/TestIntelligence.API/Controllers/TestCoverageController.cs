using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Services;
using TestIntelligence.Core.Models;
using TestIntelligence.API.Models;

namespace TestIntelligence.API.Controllers;

/// <summary>
/// API controller for test coverage analysis operations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class TestCoverageController : ControllerBase
{
    private readonly ITestCoverageAnalyzer _testCoverageAnalyzer;
    private readonly ILogger<TestCoverageController> _logger;

    public TestCoverageController(
        ITestCoverageAnalyzer testCoverageAnalyzer,
        ILogger<TestCoverageController> logger)
    {
        _testCoverageAnalyzer = testCoverageAnalyzer ?? throw new ArgumentNullException(nameof(testCoverageAnalyzer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Find all tests that exercise a specific method.
    /// </summary>
    /// <param name="methodId">Unique identifier for the method</param>
    /// <param name="request">Request containing solution path and optional parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of tests exercising the method</returns>
    [HttpGet("method/{methodId}")]
    public async Task<ActionResult<TestCoverageResponse>> GetTestsForMethod(
        string methodId,
        [FromQuery] TestCoverageRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(methodId))
            {
                return BadRequest("Method ID is required");
            }

            if (string.IsNullOrWhiteSpace(request.SolutionPath))
            {
                return BadRequest("Solution path is required");
            }

            _logger.LogInformation("Finding tests exercising method: {MethodId} in solution: {SolutionPath}", 
                methodId, request.SolutionPath);

            var tests = await _testCoverageAnalyzer.FindTestsExercisingMethodAsync(
                methodId, 
                request.SolutionPath, 
                cancellationToken);

            var response = new TestCoverageResponse
            {
                MethodId = methodId,
                SolutionPath = request.SolutionPath,
                Tests = tests.ToList(),
                TotalCount = tests.Count,
                Timestamp = DateTime.UtcNow
            };

            _logger.LogInformation("Found {TestCount} tests exercising method {MethodId}", 
                tests.Count, methodId);

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Invalid request for method {MethodId}: {Error}", methodId, ex.Message);
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding tests for method {MethodId}", methodId);
            return StatusCode(500, new { error = "An error occurred while analyzing test coverage" });
        }
    }

    /// <summary>
    /// Find tests exercising multiple methods in a single request.
    /// </summary>
    /// <param name="request">Bulk test coverage request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping method IDs to their test coverage</returns>
    [HttpPost("bulk")]
    public async Task<ActionResult<BulkTestCoverageResponse>> GetTestsForMethods(
        [FromBody] BulkTestCoverageRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (request.MethodIds == null || !request.MethodIds.Any())
            {
                return BadRequest("At least one method ID is required");
            }

            if (string.IsNullOrWhiteSpace(request.SolutionPath))
            {
                return BadRequest("Solution path is required");
            }

            _logger.LogInformation("Finding tests for {MethodCount} methods in solution: {SolutionPath}", 
                request.MethodIds.Count(), request.SolutionPath);

            var results = await _testCoverageAnalyzer.FindTestsExercisingMethodsAsync(
                request.MethodIds, 
                request.SolutionPath, 
                cancellationToken);

            var response = new BulkTestCoverageResponse
            {
                SolutionPath = request.SolutionPath,
                Results = results.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new TestCoverageResponse
                    {
                        MethodId = kvp.Key,
                        SolutionPath = request.SolutionPath,
                        Tests = kvp.Value.ToList(),
                        TotalCount = kvp.Value.Count,
                        Timestamp = DateTime.UtcNow
                    }),
                TotalMethods = request.MethodIds.Count(),
                Timestamp = DateTime.UtcNow
            };

            _logger.LogInformation("Completed bulk analysis for {MethodCount} methods", 
                request.MethodIds.Count());

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Invalid bulk request: {Error}", ex.Message);
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in bulk test coverage analysis");
            return StatusCode(500, new { error = "An error occurred while analyzing test coverage" });
        }
    }

    /// <summary>
    /// Build a complete test coverage map for a solution.
    /// </summary>
    /// <param name="request">Coverage map request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Complete test coverage map</returns>
    [HttpPost("coverage-map")]
    public async Task<ActionResult<TestCoverageMapResponse>> BuildCoverageMap(
        [FromBody] TestCoverageMapRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.SolutionPath))
            {
                return BadRequest("Solution path is required");
            }

            _logger.LogInformation("Building coverage map for solution: {SolutionPath}", request.SolutionPath);

            var coverageMap = await _testCoverageAnalyzer.BuildTestCoverageMapAsync(
                request.SolutionPath, 
                cancellationToken);

            var response = new TestCoverageMapResponse
            {
                SolutionPath = coverageMap.SolutionPath,
                BuildTimestamp = coverageMap.BuildTimestamp,
                CoveredMethodCount = coverageMap.CoveredMethodCount,
                TotalCoverageRelationships = coverageMap.TotalCoverageRelationships,
                CoveredMethods = coverageMap.GetCoveredMethods().ToList(),
                // Note: Not including the full MethodToTests dictionary in the response
                // as it could be very large. Clients should use the individual endpoints.
                Timestamp = DateTime.UtcNow
            };

            _logger.LogInformation("Built coverage map with {CoveredMethods} methods having coverage", 
                coverageMap.CoveredMethodCount);

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Invalid coverage map request: {Error}", ex.Message);
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building coverage map");
            return StatusCode(500, new { error = "An error occurred while building test coverage map" });
        }
    }

    /// <summary>
    /// Get test coverage statistics for a solution.
    /// </summary>
    /// <param name="request">Coverage statistics request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Test coverage statistics</returns>
    [HttpPost("statistics")]
    public async Task<ActionResult<TestCoverageStatisticsResponse>> GetCoverageStatistics(
        [FromBody] TestCoverageStatisticsRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.SolutionPath))
            {
                return BadRequest("Solution path is required");
            }

            _logger.LogInformation("Calculating coverage statistics for solution: {SolutionPath}", 
                request.SolutionPath);

            var statistics = await _testCoverageAnalyzer.GetCoverageStatisticsAsync(
                request.SolutionPath, 
                cancellationToken);

            var response = new TestCoverageStatisticsResponse
            {
                SolutionPath = request.SolutionPath,
                Statistics = statistics,
                Timestamp = DateTime.UtcNow
            };

            _logger.LogInformation("Coverage statistics: {CoveredMethods}/{TotalMethods} methods ({CoveragePercentage:F2}%)", 
                statistics.CoveredMethods, statistics.TotalMethods, statistics.CoveragePercentage);

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Invalid statistics request: {Error}", ex.Message);
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating coverage statistics");
            return StatusCode(500, new { error = "An error occurred while calculating coverage statistics" });
        }
    }
}