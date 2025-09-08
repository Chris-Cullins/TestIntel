using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TestIntelligence.API.Models;
using TestIntelligence.ImpactAnalyzer.Services;
using TestIntelligence.ImpactAnalyzer.Models;
using TestIntelligence.SelectionEngine.Interfaces;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.Core.Models;

namespace TestIntelligence.API.Controllers;

/// <summary>
/// API controller for impact analysis operations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ImpactAnalysisController : ControllerBase
{
    private readonly ISimplifiedDiffImpactAnalyzer _impactAnalyzer;
    private readonly ITestSelectionEngine _selectionEngine;
    private readonly ILogger<ImpactAnalysisController> _logger;

    public ImpactAnalysisController(
        ISimplifiedDiffImpactAnalyzer impactAnalyzer,
        ITestSelectionEngine selectionEngine,
        ILogger<ImpactAnalysisController> logger)
    {
        _impactAnalyzer = impactAnalyzer ?? throw new ArgumentNullException(nameof(impactAnalyzer));
        _selectionEngine = selectionEngine ?? throw new ArgumentNullException(nameof(selectionEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Analyze the impact of code changes based on a diff.
    /// </summary>
    /// <param name="request">Request containing diff content and solution path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Analysis result showing affected tests and code changes</returns>
    [HttpPost("analyze-diff")]
    public async Task<ActionResult<DiffAnalysisResult>> AnalyzeDiff(
        [FromBody] DiffAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (request == null)
            {
                return BadRequest("Request is required");
            }

            if (string.IsNullOrWhiteSpace(request.SolutionPath))
            {
                return BadRequest("Solution path is required");
            }

            if (string.IsNullOrWhiteSpace(request.DiffContent))
            {
                return BadRequest("Diff content is required");
            }

            _logger.LogInformation("Analyzing diff impact for solution: {SolutionPath}", request.SolutionPath);

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
                TotalChanges = impactResult.CodeChanges.Changes.Count
            };

            _logger.LogInformation("Completed diff analysis for solution {SolutionPath}: {ChangeCount} changes, {TestCount} recommended tests", 
                request.SolutionPath, result.ChangeSet?.Changes?.Count ?? 0, result.RecommendedTests?.Tests?.Count() ?? 0);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Invalid diff analysis request: {Error}", ex.Message);
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing diff impact");
            return StatusCode(500, new { error = "An error occurred while analyzing diff impact" });
        }
    }
}