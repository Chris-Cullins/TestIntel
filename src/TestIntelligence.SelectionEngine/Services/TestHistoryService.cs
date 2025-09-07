using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.SelectionEngine.Models;

namespace TestIntelligence.SelectionEngine.Services
{
    /// <summary>
    /// Service responsible for managing test execution history and repository.
    /// </summary>
    public class TestHistoryService : ITestHistoryService
    {
        private readonly ILogger<TestHistoryService> _logger;
        
        // In-memory storage for demonstration - in production this would be a database
        private readonly ConcurrentDictionary<string, TestInfo> _testRepository = new();
        private readonly List<TestExecutionResult> _executionHistory = new();

        public TestHistoryService(ILogger<TestHistoryService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task UpdateTestExecutionHistoryAsync(
            IEnumerable<TestExecutionResult> results, 
            CancellationToken cancellationToken = default)
        {
            var resultsList = results.ToList();
            _logger.LogInformation("Updating execution history for {ResultCount} test results", resultsList.Count);

            foreach (var result in resultsList)
            {
                lock (_executionHistory)
                {
                    _executionHistory.Add(result);
                }
                
                // Update test info if it exists in repository
                // Note: This is simplified - in production we'd need better test identification
                var testInfo = _testRepository.Values.FirstOrDefault(t => 
                    t.GetDisplayName().Contains("TestMethod")); // Simplified matching
                
                if (testInfo != null)
                {
                    testInfo.ExecutionHistory.Add(result);
                    testInfo.LastExecuted = result.ExecutedAt;
                    
                    // Update average execution time
                    if (result.Passed) // Only use successful runs for timing
                    {
                        var successfulRuns = testInfo.ExecutionHistory.Where(r => r.Passed).ToList();
                        if (successfulRuns.Count > 0)
                        {
                            var avgMs = successfulRuns.Average(r => r.Duration.TotalMilliseconds);
                            testInfo.AverageExecutionTime = TimeSpan.FromMilliseconds(avgMs);
                        }
                    }
                }
            }

            await Task.CompletedTask; // Placeholder for async database operations
        }

        public async Task<IReadOnlyList<TestInfo>> GetTestHistoryAsync(
            string? testFilter = null, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Retrieving test history with filter: {Filter}", testFilter ?? "none");

            var tests = _testRepository.Values.AsEnumerable();

            if (!string.IsNullOrEmpty(testFilter))
            {
                tests = tests.Where(t => 
                    t.GetDisplayName().IndexOf(testFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.GetUniqueId().IndexOf(testFilter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return await Task.FromResult(tests.ToList());
        }

        public IReadOnlyDictionary<string, TestInfo> GetAllTests()
        {
            return _testRepository.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public async Task AddOrUpdateTestAsync(TestInfo testInfo, CancellationToken cancellationToken = default)
        {
            var key = testInfo.GetUniqueId();
            _testRepository.AddOrUpdate(key, testInfo, (k, existing) => testInfo);
            
            _logger.LogDebug("Added/updated test in repository: {TestId}", key);
            
            await Task.CompletedTask; // Placeholder for async database operations
        }
    }
}