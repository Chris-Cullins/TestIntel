using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.ImpactAnalyzer.Models;

namespace TestIntelligence.SelectionEngine.Services
{
    /// <summary>
    /// Service responsible for creating test execution plans from scored tests.
    /// </summary>
    public interface ITestPlanService
    {
        /// <summary>
        /// Creates an optimal test execution plan based on code changes and confidence level.
        /// </summary>
        Task<TestExecutionPlan> CreateOptimalTestPlanAsync(
            CodeChangeSet changes, 
            ConfidenceLevel confidenceLevel, 
            TestSelectionOptions? options = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a test execution plan based on confidence level (without specific changes).
        /// </summary>
        Task<TestExecutionPlan> CreateTestPlanAsync(
            ConfidenceLevel confidenceLevel, 
            TestSelectionOptions? options = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Selects tests for a plan based on scored tests and constraints.
        /// </summary>
        Task<List<TestInfo>> SelectTestsForPlanAsync(
            List<TestInfo> scoredTests,
            ConfidenceLevel confidenceLevel,
            TestSelectionOptions options,
            CancellationToken cancellationToken = default);
    }
}