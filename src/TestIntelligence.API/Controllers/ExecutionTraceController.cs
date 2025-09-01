using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Interfaces;
using TestIntelligence.Core.Models;
using TestIntelligence.API.Models;

namespace TestIntelligence.API.Controllers;

/// <summary>
/// API controller for test execution tracing operations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ExecutionTraceController : ControllerBase
{
    private readonly ITestExecutionTracer _testExecutionTracer;
    private readonly ILogger<ExecutionTraceController> _logger;

    public ExecutionTraceController(
        ITestExecutionTracer testExecutionTracer,
        ILogger<ExecutionTraceController> logger)
    {
        _testExecutionTracer = testExecutionTracer ?? throw new ArgumentNullException(nameof(testExecutionTracer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Trace all production code executed by a specific test method.
    /// </summary>
    /// <param name="testMethodId">Unique identifier for the test method</param>
    /// <param name="request">Request containing solution path and optional parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Execution trace showing all production code called by the test</returns>
    [HttpGet("test/{testMethodId}")]
    public async Task<ActionResult<TestExecutionTraceResponse>> TraceTestExecution(
        string testMethodId,
        [FromQuery] TestExecutionTraceRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(testMethodId))
            {
                return BadRequest("Test method ID is required");
            }

            if (string.IsNullOrWhiteSpace(request.SolutionPath))
            {
                return BadRequest("Solution path is required");
            }

            _logger.LogInformation("Tracing execution for test method: {TestMethodId} in solution: {SolutionPath}", 
                testMethodId, request.SolutionPath);

            var executionTrace = await _testExecutionTracer.TraceTestExecutionAsync(
                testMethodId, 
                request.SolutionPath, 
                cancellationToken);

            var response = new TestExecutionTraceResponse
            {
                TestMethodId = testMethodId,
                SolutionPath = request.SolutionPath,
                ExecutionTrace = executionTrace,
                Timestamp = DateTime.UtcNow
            };

            _logger.LogInformation("Found {MethodCount} methods in execution trace for test {TestMethodId} ({ProductionCount} production methods)", 
                executionTrace.TotalMethodsCalled, testMethodId, executionTrace.ProductionMethodsCalled);

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Invalid request for test method {TestMethodId}: {Error}", testMethodId, ex.Message);
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tracing execution for test method {TestMethodId}", testMethodId);
            return StatusCode(500, new { error = "An error occurred while tracing test execution" });
        }
    }

    /// <summary>
    /// Trace execution for multiple test methods in a single request.
    /// </summary>
    /// <param name="request">Bulk test execution tracing request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping test method IDs to their execution traces</returns>
    [HttpPost("bulk")]
    public async Task<ActionResult<BulkTestExecutionTraceResponse>> TraceMultipleTestsExecution(
        [FromBody] BulkTestExecutionTraceRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (request.TestMethodIds == null || !request.TestMethodIds.Any())
            {
                return BadRequest("At least one test method ID is required");
            }

            if (string.IsNullOrWhiteSpace(request.SolutionPath))
            {
                return BadRequest("Solution path is required");
            }

            _logger.LogInformation("Tracing execution for {TestCount} test methods in solution: {SolutionPath}", 
                request.TestMethodIds.Count(), request.SolutionPath);

            var traces = await _testExecutionTracer.TraceMultipleTestsAsync(
                request.TestMethodIds, 
                request.SolutionPath, 
                cancellationToken);

            var response = new BulkTestExecutionTraceResponse
            {
                SolutionPath = request.SolutionPath,
                Results = traces.ToDictionary(
                    trace => trace.TestMethodId,
                    trace => new TestExecutionTraceResponse
                    {
                        TestMethodId = trace.TestMethodId,
                        SolutionPath = request.SolutionPath,
                        ExecutionTrace = trace,
                        Timestamp = DateTime.UtcNow
                    }),
                TotalTestMethods = request.TestMethodIds.Count(),
                Timestamp = DateTime.UtcNow
            };

            _logger.LogInformation("Completed bulk execution tracing for {TestCount} test methods", 
                request.TestMethodIds.Count());

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Invalid bulk execution trace request: {Error}", ex.Message);
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in bulk execution trace analysis");
            return StatusCode(500, new { error = "An error occurred while tracing test execution" });
        }
    }

    /// <summary>
    /// Generate a comprehensive execution coverage report for all tests in a solution.
    /// </summary>
    /// <param name="request">Coverage report request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Complete execution coverage report showing which production code is exercised by tests</returns>
    [HttpPost("coverage-report")]
    public async Task<ActionResult<ExecutionCoverageReportResponse>> GenerateCoverageReport(
        [FromBody] ExecutionCoverageReportRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.SolutionPath))
            {
                return BadRequest("Solution path is required");
            }

            _logger.LogInformation("Generating execution coverage report for solution: {SolutionPath}", request.SolutionPath);

            var coverageReport = await _testExecutionTracer.GenerateCoverageReportAsync(
                request.SolutionPath, 
                cancellationToken);

            var response = new ExecutionCoverageReportResponse
            {
                SolutionPath = request.SolutionPath,
                CoverageReport = coverageReport,
                Timestamp = DateTime.UtcNow
            };

            _logger.LogInformation("Generated execution coverage report: {CoveredMethods}/{TotalMethods} production methods ({CoveragePercentage:F2}%) covered by {TestCount} tests", 
                coverageReport.Statistics.CoveredProductionMethods, 
                coverageReport.Statistics.TotalProductionMethods, 
                coverageReport.Statistics.CoveragePercentage,
                coverageReport.Statistics.TotalTestMethods);

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Invalid coverage report request: {Error}", ex.Message);
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating execution coverage report");
            return StatusCode(500, new { error = "An error occurred while generating execution coverage report" });
        }
    }

    /// <summary>
    /// Get execution statistics for a specific test method.
    /// </summary>
    /// <param name="testMethodId">Unique identifier for the test method</param>
    /// <param name="request">Request containing solution path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Execution statistics including method counts and complexity metrics</returns>
    [HttpGet("test/{testMethodId}/statistics")]
    public async Task<ActionResult<object>> GetTestExecutionStatistics(
        string testMethodId,
        [FromQuery] TestExecutionTraceRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(testMethodId))
            {
                return BadRequest("Test method ID is required");
            }

            if (string.IsNullOrWhiteSpace(request.SolutionPath))
            {
                return BadRequest("Solution path is required");
            }

            _logger.LogInformation("Getting execution statistics for test method: {TestMethodId}", testMethodId);

            var executionTrace = await _testExecutionTracer.TraceTestExecutionAsync(
                testMethodId, 
                request.SolutionPath, 
                cancellationToken);

            var categoryBreakdown = executionTrace.ExecutedMethods
                .GroupBy(em => em.Category)
                .ToDictionary(g => g.Key.ToString(), g => g.Count());

            var depthBreakdown = executionTrace.ExecutedMethods
                .GroupBy(em => em.CallDepth)
                .ToDictionary(g => g.Key, g => g.Count());

            var statistics = new
            {
                TestMethodId = testMethodId,
                SolutionPath = request.SolutionPath,
                TotalMethodsCalled = executionTrace.TotalMethodsCalled,
                ProductionMethodsCalled = executionTrace.ProductionMethodsCalled,
                TestUtilityMethodsCalled = executionTrace.ExecutedMethods.Count(em => !em.IsProductionCode),
                EstimatedComplexity = executionTrace.EstimatedExecutionComplexity,
                MaxCallDepth = executionTrace.ExecutedMethods.Any() ? executionTrace.ExecutedMethods.Max(em => em.CallDepth) : 0,
                AverageCallDepth = executionTrace.ExecutedMethods.Any() ? executionTrace.ExecutedMethods.Average(em => em.CallDepth) : 0,
                CategoryBreakdown = categoryBreakdown,
                DepthBreakdown = depthBreakdown,
                Timestamp = DateTime.UtcNow
            };

            _logger.LogInformation("Generated execution statistics for test method {TestMethodId}: {ProductionCount} production methods, max depth {MaxDepth}", 
                testMethodId, executionTrace.ProductionMethodsCalled, statistics.MaxCallDepth);

            return Ok(statistics);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Invalid statistics request for test method {TestMethodId}: {Error}", testMethodId, ex.Message);
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting execution statistics for test method {TestMethodId}", testMethodId);
            return StatusCode(500, new { error = "An error occurred while getting execution statistics" });
        }
    }
}