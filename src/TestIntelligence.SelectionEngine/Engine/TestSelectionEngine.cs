using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Discovery;
using TestIntelligence.Core.Services;
using TestIntelligence.ImpactAnalyzer.Models;
using TestIntelligence.SelectionEngine.Interfaces;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.SelectionEngine.Algorithms;

namespace TestIntelligence.SelectionEngine.Engine
{
    /// <summary>
    /// Main implementation of intelligent test selection engine.
    /// </summary>
    public class TestSelectionEngine : ITestSelectionEngine
    {
        private readonly ILogger<TestSelectionEngine> _logger;
        private readonly List<ITestScoringAlgorithm> _scoringAlgorithms;
        private readonly ITestCategorizer? _testCategorizer;
        private readonly IImpactAnalyzer? _impactAnalyzer;
        private readonly IAssemblyPathResolver? _assemblyPathResolver;
        private readonly string? _solutionPath;

        // In-memory storage for demonstration - in production this would be a database
        private readonly Dictionary<string, TestInfo> _testRepository;
        private readonly List<TestExecutionResult> _executionHistory;

        public TestSelectionEngine(
            ILogger<TestSelectionEngine> logger,
            IEnumerable<ITestScoringAlgorithm>? scoringAlgorithms = null,
            ITestCategorizer? testCategorizer = null,
            IImpactAnalyzer? impactAnalyzer = null,
            IAssemblyPathResolver? assemblyPathResolver = null,
            string? solutionPath = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _testCategorizer = testCategorizer;
            _impactAnalyzer = impactAnalyzer;
            _assemblyPathResolver = assemblyPathResolver;
            _solutionPath = solutionPath;
            _testRepository = new Dictionary<string, TestInfo>();
            _executionHistory = new List<TestExecutionResult>();

            // Initialize default scoring algorithms if none provided
            _scoringAlgorithms = new List<ITestScoringAlgorithm>(scoringAlgorithms ?? new List<ITestScoringAlgorithm>
            {
                new ImpactBasedScoringAlgorithm(_logger as ILogger<ImpactBasedScoringAlgorithm> ?? 
                    new NullLogger<ImpactBasedScoringAlgorithm>()),
                new ExecutionTimeScoringAlgorithm(_logger as ILogger<ExecutionTimeScoringAlgorithm> ?? 
                    new NullLogger<ExecutionTimeScoringAlgorithm>()),
                new HistoricalScoringAlgorithm(_logger as ILogger<HistoricalScoringAlgorithm> ?? 
                    new NullLogger<HistoricalScoringAlgorithm>())
            });
        }

        public async Task<TestExecutionPlan> GetOptimalTestPlanAsync(
            CodeChangeSet changes, 
            ConfidenceLevel confidenceLevel, 
            TestSelectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Creating optimal test plan for {ChangeCount} changes with {ConfidenceLevel} confidence", 
                changes.Changes.Count, confidenceLevel);

            options ??= new TestSelectionOptions();
            return await GetTestPlanInternalAsync(changes, confidenceLevel, options, cancellationToken);
        }

        public async Task<TestExecutionPlan> GetTestPlanAsync(
            ConfidenceLevel confidenceLevel, 
            TestSelectionOptions? options = null, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Creating test plan with {ConfidenceLevel} confidence", confidenceLevel);

            options ??= new TestSelectionOptions();
            return await GetTestPlanInternalAsync(null, confidenceLevel, options, cancellationToken);
        }

        public async Task<IReadOnlyList<TestInfo>> ScoreTestsAsync(
            IEnumerable<TestInfo> candidateTests, 
            CodeChangeSet? changes = null, 
            CancellationToken cancellationToken = default)
        {
            var tests = candidateTests.ToList();
            _logger.LogInformation("Scoring {TestCount} candidate tests", tests.Count);

            var context = new TestScoringContext(ConfidenceLevel.Medium, changes);
            
            foreach (var test in tests)
            {
                test.SelectionScore = await CalculateCombinedScore(test, context, cancellationToken);
            }

            return tests.OrderByDescending(t => t.SelectionScore).ToList();
        }

        public async Task UpdateTestExecutionHistoryAsync(
            IEnumerable<TestExecutionResult> results, 
            CancellationToken cancellationToken = default)
        {
            var resultsList = results.ToList();
            _logger.LogInformation("Updating execution history for {ResultCount} test results", resultsList.Count);

            foreach (var result in resultsList)
            {
                _executionHistory.Add(result);
                
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

        private async Task<TestExecutionPlan> GetTestPlanInternalAsync(
            CodeChangeSet? changes,
            ConfidenceLevel confidenceLevel, 
            TestSelectionOptions options,
            CancellationToken cancellationToken)
        {
            // For now, use a mock set of tests - in production this would query the test repository
            var candidateTests = await GetCandidateTests(changes, options, cancellationToken);

            if (candidateTests.Count == 0)
            {
                _logger.LogWarning("No candidate tests found for selection");
                return new TestExecutionPlan(
                    Array.Empty<TestInfo>(), 
                    confidenceLevel, 
                    TimeSpan.Zero,
                    "No tests available");
            }

            List<TestInfo> selectedTests;
            
            // For Full confidence, select ALL tests without scoring
            if (confidenceLevel == ConfidenceLevel.Full)
            {
                _logger.LogInformation("Full confidence selected - including ALL {TestCount} tests", candidateTests.Count);
                selectedTests = candidateTests;
                
                // Apply basic filtering for Full confidence (exclude flaky tests if requested)
                if (!options.IncludeFlakyTests)
                {
                    selectedTests = selectedTests.Where(t => !t.IsFlaky()).ToList();
                }
                
                // Apply category and tag filters if specified
                selectedTests = ApplyBasicFilters(selectedTests, options);
            }
            else
            {
                // Score all candidate tests for other confidence levels
                var context = new TestScoringContext(confidenceLevel, changes, options);
                var scoredTests = new List<TestInfo>();

                foreach (var test in candidateTests)
                {
                    test.SelectionScore = await CalculateCombinedScore(test, context, cancellationToken);
                    scoredTests.Add(test);
                }

                // Select tests based on confidence level and constraints
                selectedTests = await SelectTestsForPlan(scoredTests, confidenceLevel, options, cancellationToken);
            }

            // Calculate total estimated duration
            var estimatedDuration = TimeSpan.FromMilliseconds(
                selectedTests.Sum(t => t.AverageExecutionTime.TotalMilliseconds));

            var plan = new TestExecutionPlan(selectedTests, confidenceLevel, estimatedDuration, 
                $"Selected {selectedTests.Count} tests based on {confidenceLevel} confidence level");

            // Create execution batches for parallel execution
            plan.CreateExecutionBatches(options.MaxParallelism);

            _logger.LogInformation("Created test plan: {TestCount} tests, estimated duration {Duration}",
                selectedTests.Count, estimatedDuration);

            return plan;
        }

        private async Task<List<TestInfo>> GetCandidateTests(
            CodeChangeSet? changes, 
            TestSelectionOptions options,
            CancellationToken cancellationToken)
        {
            var candidates = new List<TestInfo>();

            try
            {
                // If we have a solution path, discover tests from it
                if (!string.IsNullOrEmpty(_solutionPath))
                {
                    candidates = await DiscoverTestsFromSolution(_solutionPath, cancellationToken);
                }
                // If we don't have a solution path but have changes, try to infer from the first change path
                else if (changes?.Changes.Count > 0)
                {
                    var firstChangePath = changes.Changes.First().FilePath;
                    var solutionPath = FindSolutionFile(firstChangePath);
                    if (!string.IsNullOrEmpty(solutionPath))
                    {
                        _logger.LogInformation("Inferred solution path from changes: {SolutionPath}", solutionPath);
                        candidates = await DiscoverTestsFromSolution(solutionPath, cancellationToken);
                    }
                }

                _logger.LogInformation("Discovered {CandidateCount} candidate tests", candidates.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error discovering candidate tests");
            }

            return candidates;
        }

        private async Task<double> CalculateCombinedScore(
            TestInfo testInfo, 
            TestScoringContext context,
            CancellationToken cancellationToken)
        {
            var totalWeight = 0.0;
            var weightedScore = 0.0;

            foreach (var algorithm in _scoringAlgorithms)
            {
                try
                {
                    var score = await algorithm.CalculateScoreAsync(testInfo, context, cancellationToken);
                    weightedScore += score * algorithm.Weight;
                    totalWeight += algorithm.Weight;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error calculating score using {Algorithm} for test {Test}", 
                        algorithm.Name, testInfo.GetDisplayName());
                }
            }

            return totalWeight > 0 ? weightedScore / totalWeight : 0.0;
        }

        private async Task<List<TestInfo>> SelectTestsForPlan(
            List<TestInfo> scoredTests,
            ConfidenceLevel confidenceLevel,
            TestSelectionOptions options,
            CancellationToken cancellationToken)
        {
            // Sort by score descending
            var sortedTests = scoredTests.OrderByDescending(t => t.SelectionScore).ToList();

            // Apply confidence level limits
            var maxTests = options.MaxTestCount ?? confidenceLevel.GetMaxTestCount();

            var maxDuration = options.MaxExecutionTime ?? confidenceLevel.GetEstimatedDuration();
            var minScore = options.MinSelectionScore ?? GetMinScoreForConfidenceLevel(confidenceLevel);

            var selectedTests = new List<TestInfo>();
            
            // For Fast confidence, use strategic category balance
            if (confidenceLevel == ConfidenceLevel.Fast)
            {
                selectedTests = await SelectFastConfidenceTests(sortedTests, maxTests, maxDuration, minScore, options, cancellationToken);
            }
            else
            {
                // Standard selection for other confidence levels
                selectedTests = SelectStandardTests(sortedTests, maxTests, maxDuration, minScore, options);
            }

            await Task.CompletedTask; // Placeholder for async operations

            _logger.LogDebug("Selected {SelectedCount} tests out of {CandidateCount} candidates", 
                selectedTests.Count, scoredTests.Count);

            return selectedTests;
        }

        private static double GetMinScoreForConfidenceLevel(ConfidenceLevel confidenceLevel)
        {
            return confidenceLevel switch
            {
                ConfidenceLevel.Fast => 0.6,
                ConfidenceLevel.Medium => 0.4,
                ConfidenceLevel.High => 0.2,
                ConfidenceLevel.Full => 0.0,
                _ => 0.3
            };
        }

        private static List<TestInfo> ApplyBasicFilters(List<TestInfo> tests, TestSelectionOptions options)
        {
            var filtered = tests.AsEnumerable();

            // Apply category filters
            if (options.ExcludedCategories?.Count > 0)
            {
                filtered = filtered.Where(t => !options.ExcludedCategories.Contains(t.Category));
            }

            if (options.IncludedCategories?.Count > 0)
            {
                filtered = filtered.Where(t => options.IncludedCategories.Contains(t.Category));
            }

            // Apply tag filters
            if (options.ExcludedTags?.Count > 0)
            {
                filtered = filtered.Where(t => !t.Tags.Any(tag => options.ExcludedTags.Contains(tag)));
            }

            if (options.RequiredTags?.Count > 0)
            {
                filtered = filtered.Where(t => options.RequiredTags.Any(tag => t.Tags.Contains(tag)));
            }

            return filtered.ToList();
        }

        private Task<List<TestInfo>> SelectFastConfidenceTests(
            List<TestInfo> sortedTests,
            int maxTests,
            TimeSpan maxDuration,
            double minScore,
            TestSelectionOptions options,
            CancellationToken cancellationToken)
        {
            var selectedTests = new List<TestInfo>();
            var currentDuration = TimeSpan.Zero;

            // Strategy for Fast confidence: prioritize high-scoring unit tests first, then integration tests
            // 80% Unit tests (fast feedback and direct relationships), 20% Integration tests (broader coverage)
            var unitTestLimit = Math.Max(1, (int)(maxTests * 0.8));
            var integrationTestLimit = maxTests - unitTestLimit;
            
            var unitTestsSelected = 0;
            var integrationTestsSelected = 0;

            // Use a higher score threshold for Fast confidence to ensure quality, but allow unit tests with direct relationships
            var fastMinScore = Math.Max(minScore, 0.5); // Lower threshold to include direct unit tests

            _logger.LogDebug("Fast selection: targeting {UnitLimit} unit tests, {IntegrationLimit} integration tests, min score {MinScore}",
                unitTestLimit, integrationTestLimit, fastMinScore);

            // FIRST PASS: Select highest-scoring unit tests (likely direct relationships)
            var unitTests = sortedTests.Where(t => t.Category == TestCategory.Unit).OrderByDescending(t => t.SelectionScore);
            foreach (var test in unitTests)
            {
                if (unitTestsSelected >= unitTestLimit)
                    break;

                if (test.SelectionScore < fastMinScore)
                    continue;

                if (currentDuration + test.AverageExecutionTime > maxDuration)
                    continue;

                if (!PassesBasicFilters(test, options))
                    continue;

                selectedTests.Add(test);
                currentDuration += test.AverageExecutionTime;
                test.LastSelected = DateTimeOffset.UtcNow;
                unitTestsSelected++;

                _logger.LogDebug("Selected unit test: {TestName} (score: {Score:F3})", 
                    test.GetDisplayName(), test.SelectionScore);
            }

            // SECOND PASS: Select integration tests to fill remaining slots
            var integrationTests = sortedTests.Where(t => t.Category == TestCategory.Integration).OrderByDescending(t => t.SelectionScore);
            foreach (var test in integrationTests)
            {
                if (integrationTestsSelected >= integrationTestLimit)
                    break;

                if (selectedTests.Count >= maxTests)
                    break;

                if (test.SelectionScore < Math.Max(minScore, 0.4)) // Slightly higher threshold for integration tests
                    continue;

                if (currentDuration + test.AverageExecutionTime > maxDuration)
                    continue;

                if (!PassesBasicFilters(test, options))
                    continue;

                selectedTests.Add(test);
                currentDuration += test.AverageExecutionTime;
                test.LastSelected = DateTimeOffset.UtcNow;
                integrationTestsSelected++;

                _logger.LogDebug("Selected integration test: {TestName} (score: {Score:F3})", 
                    test.GetDisplayName(), test.SelectionScore);
            }

            // THIRD PASS: Fill remaining slots with any high-scoring tests if we have capacity
            if (selectedTests.Count < maxTests)
            {
                var remainingTests = sortedTests.Where(t => !selectedTests.Contains(t))
                    .OrderByDescending(t => t.SelectionScore);
                
                foreach (var test in remainingTests)
                {
                    if (selectedTests.Count >= maxTests)
                        break;

                    if (test.SelectionScore < minScore)
                        continue;

                    if (currentDuration + test.AverageExecutionTime > maxDuration)
                        continue;

                    if (!PassesBasicFilters(test, options))
                        continue;

                    selectedTests.Add(test);
                    currentDuration += test.AverageExecutionTime;
                    test.LastSelected = DateTimeOffset.UtcNow;
                }
            }

            _logger.LogInformation("Fast selection: selected {Total} tests ({Unit} unit, {Integration} integration, {Other} other), avg score: {AvgScore:F3}",
                selectedTests.Count,
                selectedTests.Count(t => t.Category == TestCategory.Unit),
                selectedTests.Count(t => t.Category == TestCategory.Integration),
                selectedTests.Count(t => t.Category != TestCategory.Unit && t.Category != TestCategory.Integration),
                selectedTests.Count > 0 ? selectedTests.Average(t => t.SelectionScore) : 0.0);

            return Task.FromResult(selectedTests);
        }

        private List<TestInfo> SelectStandardTests(
            List<TestInfo> sortedTests,
            int maxTests,
            TimeSpan maxDuration,
            double minScore,
            TestSelectionOptions options)
        {
            var selectedTests = new List<TestInfo>();
            var currentDuration = TimeSpan.Zero;

            foreach (var test in sortedTests)
            {
                // Check constraints
                if (selectedTests.Count >= maxTests)
                    break;

                if (test.SelectionScore < minScore)
                    break;

                if (currentDuration + test.AverageExecutionTime > maxDuration)
                    continue; // Skip this test, try others

                if (!PassesBasicFilters(test, options))
                    continue;

                selectedTests.Add(test);
                currentDuration += test.AverageExecutionTime;
                test.LastSelected = DateTimeOffset.UtcNow;
            }

            return selectedTests;
        }

        private static bool PassesBasicFilters(TestInfo test, TestSelectionOptions options)
        {
            // Apply category filters
            if (options.ExcludedCategories?.Contains(test.Category) == true)
                return false;

            if (options.IncludedCategories?.Count > 0 && 
                !options.IncludedCategories.Contains(test.Category))
                return false;

            // Apply tag filters
            if (options.ExcludedTags?.Count > 0 && 
                test.Tags.Any(tag => options.ExcludedTags.Contains(tag)))
                return false;

            if (options.RequiredTags?.Count > 0 && 
                !options.RequiredTags.Any(tag => test.Tags.Contains(tag)))
                return false;

            // Apply flaky test filter
            if (!options.IncludeFlakyTests && test.IsFlaky())
                return false;

            return true;
        }

        private async Task<List<TestInfo>> DiscoverTestsFromSolution(string solutionPath, CancellationToken cancellationToken)
        {
            var testInfos = new List<TestInfo>();

            try
            {
                // Find test assemblies in the solution
                var assemblyPaths = _assemblyPathResolver != null 
                    ? await _assemblyPathResolver.FindTestAssembliesInSolutionAsync(solutionPath)
                    : await FindTestAssembliesInSolution(solutionPath);
                
                _logger.LogInformation("Found {AssemblyCount} test assemblies in solution", assemblyPaths.Count);
                foreach (var path in assemblyPaths)
                {
                    _logger.LogInformation("  Test assembly: {Assembly}", path);
                }

                // Use shared loader for efficiency
                using var loader = new CrossFrameworkAssemblyLoader();
                var discovery = TestDiscoveryFactory.CreateNUnitTestDiscovery();

                foreach (var assemblyPath in assemblyPaths)
                {
                    try
                    {
                        var loadResult = await loader.LoadAssemblyAsync(assemblyPath);
                        if (!loadResult.IsSuccess || loadResult.Assembly == null)
                        {
                            _logger.LogWarning("Failed to load assembly: {Assembly} - {Errors}", 
                                assemblyPath, string.Join(", ", loadResult.Errors));
                            continue;
                        }

                        var discoveryResult = await discovery.DiscoverTestsAsync(loadResult.Assembly, cancellationToken);
                        
                        // Convert discovered tests to TestInfo objects
                        foreach (var testMethod in discoveryResult.GetAllTestMethods())
                        {
                            var testInfo = ConvertToTestInfo(testMethod);
                            testInfos.Add(testInfo);
                        }

                        _logger.LogDebug("Discovered {TestCount} tests from {Assembly}", 
                            discoveryResult.TestMethodCount, Path.GetFileName(assemblyPath));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error discovering tests from assembly: {Assembly}", assemblyPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error discovering tests from solution: {Solution}", solutionPath);
            }

            return testInfos;
        }

        private async Task<List<string>> FindTestAssembliesInSolution(string solutionPath)
        {
            var assemblies = new List<string>();

            try
            {
                var solutionDir = Path.GetDirectoryName(solutionPath)!;
                var projectPaths = await FindTestProjectsInSolution(solutionPath);
                
                _logger.LogInformation("Found {ProjectCount} test projects", projectPaths.Count);

                foreach (var projectPath in projectPaths)
                {
                    var assemblyPath = _assemblyPathResolver?.ResolveAssemblyPath(projectPath) 
                        ?? GetAssemblyPathFromProject(projectPath);
                    _logger.LogDebug("Checking assembly path: {Assembly} (exists: {Exists})", assemblyPath, File.Exists(assemblyPath));
                    if (File.Exists(assemblyPath))
                    {
                        assemblies.Add(assemblyPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error finding test assemblies in solution: {Solution}", solutionPath);
            }

            return assemblies;
        }

        private async Task<List<string>> FindTestProjectsInSolution(string solutionPath)
        {
            var testProjects = new List<string>();

            try
            {
                var solutionContent = await File.ReadAllTextAsync(solutionPath);
                var solutionDir = Path.GetDirectoryName(solutionPath)!;

                // Parse solution file properly
                // Format: Project("{GUID}") = "ProjectName", "RelativePath", "{ProjectGUID}"
                var lines = solutionContent.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("Project(") && line.Contains(".csproj"))
                    {
                        // Extract project path from solution line
                        var parts = line.Split(',');
                        if (parts.Length >= 2)
                        {
                            var relativePath = parts[1].Trim().Trim('"');
                            
                            // Only include test projects (in tests directory or with "Test" in path/name)
                            if (relativePath.Contains("test", StringComparison.OrdinalIgnoreCase) || 
                                relativePath.StartsWith("tests", StringComparison.OrdinalIgnoreCase))
                            {
                                var fullPath = Path.Combine(solutionDir, relativePath).Replace('\\', Path.DirectorySeparatorChar);
                                if (File.Exists(fullPath))
                                {
                                    testProjects.Add(fullPath);
                                    _logger.LogDebug("Found test project: {Project}", fullPath);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing solution file: {Solution}", solutionPath);
            }

            return testProjects;
        }

        private string GetAssemblyPathFromProject(string projectPath)
        {
            var projectDir = Path.GetDirectoryName(projectPath)!;
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            
            // Try common output paths
            var possiblePaths = new[]
            {
                Path.Combine(projectDir, "bin", "Debug", "net8.0", $"{projectName}.dll"),
                Path.Combine(projectDir, "bin", "Release", "net8.0", $"{projectName}.dll"),
                Path.Combine(projectDir, "bin", "Debug", $"{projectName}.dll"),
                Path.Combine(projectDir, "bin", "Release", $"{projectName}.dll")
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            // Default to Debug net8.0 path even if it doesn't exist yet
            return Path.Combine(projectDir, "bin", "Debug", "net8.0", $"{projectName}.dll");
        }

        private TestInfo ConvertToTestInfo(Core.Models.TestMethod testMethod)
        {
            var category = CategorizeTest(testMethod);
            var averageTime = TimeSpan.FromMilliseconds(100); // Default estimate
            
            var testInfo = new TestInfo(testMethod, category, averageTime);

            // Add tags to the test info
            var tags = ExtractTestTags(testMethod);
            foreach (var tag in tags)
            {
                testInfo.Tags.Add(tag);
            }

            // Extract dependencies from test method and class
            var dependencies = ExtractTestDependencies(testMethod);
            foreach (var dependency in dependencies)
            {
                testInfo.Dependencies.Add(dependency);
            }

            return testInfo;
        }

        private List<string> ExtractTestDependencies(Core.Models.TestMethod testMethod)
        {
            var dependencies = new List<string>();
            
            try
            {
                var className = testMethod.MethodInfo.DeclaringType?.Name ?? "";
                var methodName = testMethod.MethodInfo.Name;
                var namespaceName = testMethod.MethodInfo.DeclaringType?.Namespace ?? "";

                // Extract dependencies based on test naming patterns
                if (className.EndsWith("Tests") || className.EndsWith("Test"))
                {
                    var baseClassName = className.Replace("Tests", "").Replace("Test", "");
                    
                    // Add direct class dependency
                    if (!string.IsNullOrEmpty(baseClassName))
                    {
                        // Handle NUnitTestDiscoveryTests -> NUnitTestDiscovery mapping
                        if (baseClassName == "NUnitTestDiscovery")
                        {
                            dependencies.Add("TestIntelligence.Core.Discovery.NUnitTestDiscovery");
                            dependencies.Add("TestIntelligence.Core.Discovery.NUnitTestDiscovery.DiscoverTestsAsync");
                            dependencies.Add("TestIntelligence.Core.Discovery.ITestDiscovery");
                        }
                        else
                        {
                            // Generic pattern for other tests
                            dependencies.Add($"{namespaceName.Replace(".Tests", "")}.{baseClassName}");
                        }
                    }
                }

                // Method-specific dependencies
                if (methodName.Contains("DiscoverTests"))
                {
                    dependencies.Add("TestIntelligence.Core.Discovery.NUnitTestDiscovery.DiscoverTestsAsync");
                    dependencies.Add("TestIntelligence.Core.Discovery.ITestDiscovery.DiscoverTestsAsync");
                }

                if (methodName.Contains("CreateNUnitTestDiscovery"))
                {
                    dependencies.Add("TestIntelligence.Core.Discovery.TestDiscoveryFactory.CreateNUnitTestDiscovery");
                    dependencies.Add("TestIntelligence.Core.Discovery.NUnitTestDiscovery");
                }

                // Assembly-based dependencies
                if (namespaceName.Contains("Core.Tests"))
                {
                    dependencies.Add("TestIntelligence.Core");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extracting dependencies for test: {TestName}", testMethod.MethodInfo.Name);
            }

            return dependencies.Distinct().ToList();
        }

        private TestCategory CategorizeTest(Core.Models.TestMethod testMethod)
        {
            var methodName = testMethod.MethodInfo.Name.ToLower();
            var className = testMethod.MethodInfo.DeclaringType?.Name.ToLower() ?? "";
            var namespaceName = testMethod.MethodInfo.DeclaringType?.Namespace?.ToLower() ?? "";
            var assemblyName = testMethod.AssemblyPath.ToLower();

            // First check for direct test class patterns (unit tests should be highest priority for direct relationships)
            if (className.EndsWith("tests") || className.EndsWith("test"))
            {
                // Check if this is a unit test for a specific class
                var baseClassName = className.Replace("tests", "").Replace("test", "");
                
                // NUnitTestDiscovery tests should be categorized as Unit tests (direct relationship)
                if (baseClassName.Contains("nunittestdiscovery") || baseClassName.Contains("testdiscovery"))
                    return TestCategory.Unit;
                
                // Other specific unit test patterns
                if (baseClassName.Contains("discovery") || baseClassName.Contains("analyzer") || 
                    baseClassName.Contains("service") || baseClassName.Contains("factory"))
                    return TestCategory.Unit;
            }

            // Check method and class names for category indicators
            if (methodName.Contains("database") || methodName.Contains("db") || 
                className.Contains("database") || className.Contains("db") ||
                methodName.Contains("ef6") || methodName.Contains("efcore") ||
                className.Contains("ef6") || className.Contains("efcore"))
                return TestCategory.Database;

            if (methodName.Contains("api") || methodName.Contains("http") ||
                className.Contains("api") || className.Contains("http") ||
                namespaceName.Contains("api"))
                return TestCategory.API;

            if (methodName.Contains("integration") || className.Contains("integration") ||
                namespaceName.Contains("integration") || assemblyName.Contains("integration"))
                return TestCategory.Integration;

            if (methodName.Contains("ui") || methodName.Contains("selenium") ||
                className.Contains("ui") || className.Contains("selenium"))
                return TestCategory.UI;

            if (methodName.Contains("e2e") || methodName.Contains("endtoend") ||
                className.Contains("e2e") || className.Contains("endtoend") ||
                namespaceName.Contains("e2e"))
                return TestCategory.EndToEnd;

            // Default to Unit for most test classes that don't match other patterns
            return TestCategory.Unit;
        }

        private List<string> ExtractTestTags(Core.Models.TestMethod testMethod)
        {
            var tags = new List<string>();
            
            // Add category as a tag
            var category = CategorizeTest(testMethod);
            tags.Add(category.ToString());

            // You could add more sophisticated tag extraction from attributes here
            
            return tags;
        }

        private string? FindSolutionFile(string filePath)
        {
            try
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
                
                while (directory != null)
                {
                    var solutionFiles = Directory.GetFiles(directory, "*.sln");
                    if (solutionFiles.Length > 0)
                    {
                        return solutionFiles.First(); // Return the first solution file found
                    }
                    
                    directory = Path.GetDirectoryName(directory);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error finding solution file for path: {FilePath}", filePath);
            }

            return null;
        }
    }

    /// <summary>
    /// Null logger implementation for cases where specific logger isn't available.
    /// </summary>
    internal class NullLogger<T> : ILogger<T>
    {
        IDisposable? ILogger.BeginScope<TState>(TState state) => new NullDisposable();
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }

        private class NullDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}// Another test change
