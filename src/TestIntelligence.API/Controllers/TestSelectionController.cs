using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TestIntelligence.SelectionEngine.Interfaces;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.ImpactAnalyzer.Models;
using TestIntelligence.ImpactAnalyzer.Services;
using TestIntelligence.API.Models;

namespace TestIntelligence.API.Controllers;

/// <summary>
/// API controller for intelligent test selection operations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class TestSelectionController : ControllerBase
{
    private readonly ITestSelectionEngine _selectionEngine;
    private readonly ISimplifiedDiffImpactAnalyzer _impactAnalyzer;
    private readonly ILogger<TestSelectionController> _logger;

    public TestSelectionController(
        ITestSelectionEngine selectionEngine,
        ISimplifiedDiffImpactAnalyzer impactAnalyzer,
        ILogger<TestSelectionController> logger)
    {
        _selectionEngine = selectionEngine ?? throw new ArgumentNullException(nameof(selectionEngine));
        _impactAnalyzer = impactAnalyzer ?? throw new ArgumentNullException(nameof(impactAnalyzer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get optimal test selection plan based on code changes and confidence level.
    /// </summary>
    [HttpPost("plan")]
    public async Task<ActionResult<TestExecutionPlan>> GetTestPlan(
        [FromBody] TestPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Received test plan request for {ChangeCount} changes with {ConfidenceLevel} confidence",
                request.CodeChanges?.Changes.Count ?? 0, request.ConfidenceLevel);

            TestExecutionPlan plan;
            
            var options = new TestSelectionOptions
            {
                MaxTestCount = request.MaxTests,
                MaxExecutionTime = request.MaxExecutionTime,
                ExcludedCategories = request.ExcludedCategories?.ToHashSet(),
                IncludedCategories = request.IncludedCategories?.ToHashSet()
            };

            if (request.CodeChanges != null && request.CodeChanges.Changes.Any())
            {
                plan = await _selectionEngine.GetOptimalTestPlanAsync(
                    request.CodeChanges, 
                    request.ConfidenceLevel, 
                    options,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                plan = await _selectionEngine.GetTestPlanAsync(
                    request.ConfidenceLevel, 
                    options, 
                    cancellationToken).ConfigureAwait(false);
            }

            return Ok(plan);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating test plan");
            return BadRequest(new { error = "Failed to create test plan", details = ex.Message });
        }
    }

    /// <summary>
    /// Analyze git diff and get impacted test recommendations.
    /// </summary>
    [HttpPost("analyze-diff")]
    public async Task<ActionResult<DiffAnalysisResult>> AnalyzeDiff(
        [FromBody] DiffAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Analyzing diff with {LineCount} lines", 
                request.DiffContent?.Length ?? 0);

            var impactResult = await _impactAnalyzer.AnalyzeDiffImpactAsync(
                request.DiffContent ?? "",
                request.SolutionPath).ConfigureAwait(false);

            var testPlan = await _selectionEngine.GetOptimalTestPlanAsync(
                impactResult.CodeChanges,
                request.ConfidenceLevel,
                null,
                cancellationToken).ConfigureAwait(false);

            var result = new DiffAnalysisResult
            {
                ChangeSet = impactResult.CodeChanges,
                RecommendedTests = testPlan,
                AnalysisTimestamp = DateTimeOffset.UtcNow,
                TotalChanges = impactResult.CodeChanges.Changes.Count,
                ImpactScore = 0.5 // Mock score
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing diff");
            return BadRequest(new { error = "Failed to analyze diff", details = ex.Message });
        }
    }

    /// <summary>
    /// Update test execution history for machine learning improvements.
    /// </summary>
    [HttpPost("execution-results")]
    public async Task<ActionResult> UpdateExecutionHistory(
        [FromBody] List<TestExecutionResult> results,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Updating execution history for {ResultCount} test results", results.Count);

            await _selectionEngine.UpdateTestExecutionHistoryAsync(results, cancellationToken).ConfigureAwait(false);

            return Ok(new { message = $"Updated execution history for {results.Count} tests" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating execution history");
            return BadRequest(new { error = "Failed to update execution history", details = ex.Message });
        }
    }

    /// <summary>
    /// Get test execution history and statistics.
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<TestInfo>>> GetTestHistory(
        [FromQuery] string? filter = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var history = await _selectionEngine.GetTestHistoryAsync(filter, cancellationToken).ConfigureAwait(false);
            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving test history");
            return BadRequest(new { error = "Failed to retrieve test history", details = ex.Message });
        }
    }

    private static double CalculateOverallImpactScore(CodeChangeSet changeSet)
    {
        if (!changeSet.Changes.Any())
            return 0.0;

        var totalMethods = changeSet.Changes.Sum(c => c.ChangedMethods.Count);
        var totalTypes = changeSet.Changes.Sum(c => c.ChangedTypes.Count);
        
        // Simple scoring: more changes = higher impact
        return Math.Min(1.0, (totalMethods * 0.1 + totalTypes * 0.2) / changeSet.Changes.Count);
    }
}