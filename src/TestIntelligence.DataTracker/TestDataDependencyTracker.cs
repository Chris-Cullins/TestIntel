using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Models;
using TestIntelligence.DataTracker.Analysis;
using TestIntelligence.DataTracker.Models;

namespace TestIntelligence.DataTracker
{
    /// <summary>
    /// Main service for tracking test data dependencies and detecting conflicts.
    /// </summary>
    public class TestDataDependencyTracker
    {
        private readonly List<IDatabasePatternDetector> _patternDetectors;
        private readonly ILogger<TestDataDependencyTracker> _logger;

        public TestDataDependencyTracker(ILogger<TestDataDependencyTracker>? logger = null)
        {
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TestDataDependencyTracker>.Instance;
            _patternDetectors = new List<IDatabasePatternDetector>
            {
                new EF6PatternDetector(),
                new EFCorePatternDetector()
            };

            _logger.LogInformation("TestDataDependencyTracker initialized with {DetectorCount} pattern detectors", 
                _patternDetectors.Count);
        }

        /// <summary>
        /// Finds data conflicts in the given test assembly.
        /// </summary>
        /// <param name="testAssembly">The test assembly to analyze.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Report containing all detected conflicts and dependencies.</returns>
        public async Task<DataConflictReport> FindDataConflictsAsync(
            ITestAssembly testAssembly, 
            CancellationToken cancellationToken = default)
        {
            if (testAssembly == null)
                throw new ArgumentNullException(nameof(testAssembly));

            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("Starting data conflict analysis for assembly: {AssemblyName}", 
                testAssembly.AssemblyName);

            var allDependencies = new List<DataDependency>();
            var conflicts = new List<DataConflict>();

            try
            {
                // Get all test methods from the assembly
                var testMethods = await GetTestMethodsAsync(testAssembly, cancellationToken);
                
                _logger.LogDebug("Found {TestMethodCount} test methods to analyze", testMethods.Count);

                // Detect dependencies for each test method
                foreach (var testMethod in testMethods)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var dependencies = await DetectDependenciesForTestMethod(testMethod, cancellationToken);
                    allDependencies.AddRange(dependencies);
                }

                _logger.LogInformation("Detected {DependencyCount} total dependencies", allDependencies.Count);

                // Group dependencies by test method for conflict analysis
                var dependencyGroups = allDependencies.GroupBy(d => d.TestMethodId).ToList();

                // Check for conflicts between test methods
                for (int i = 0; i < dependencyGroups.Count; i++)
                {
                    for (int j = i + 1; j < dependencyGroups.Count; j++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var groupA = dependencyGroups[i];
                        var groupB = dependencyGroups[j];

                        var testMethodA = testMethods.First(t => t.GetUniqueId() == groupA.Key);
                        var testMethodB = testMethods.First(t => t.GetUniqueId() == groupB.Key);

                        var conflictsBetweenTests = DetectConflictsBetweenTests(
                            testMethodA, testMethodB, 
                            groupA.ToList(), groupB.ToList());

                        conflicts.AddRange(conflictsBetweenTests);
                    }
                }

                _logger.LogInformation("Detected {ConflictCount} conflicts", conflicts.Count);

                return new DataConflictReport(testAssembly.AssemblyPath, conflicts, allDependencies);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during data conflict analysis for assembly: {AssemblyName}", 
                    testAssembly.AssemblyName);
                throw;
            }
        }

        /// <summary>
        /// Checks if two test methods can run in parallel based on their data dependencies.
        /// </summary>
        /// <param name="testA">First test method.</param>
        /// <param name="testB">Second test method.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if tests can run in parallel.</returns>
        public async Task<bool> CanRunInParallelAsync(
            TestMethod testA, 
            TestMethod testB, 
            CancellationToken cancellationToken = default)
        {
            if (testA == null) throw new ArgumentNullException(nameof(testA));
            if (testB == null) throw new ArgumentNullException(nameof(testB));

            try
            {
                // Get dependencies for both tests
                var dependenciesA = await DetectDependenciesForTestMethod(testA, cancellationToken);
                var dependenciesB = await DetectDependenciesForTestMethod(testB, cancellationToken);

                // Check for conflicts
                var conflicts = DetectConflictsBetweenTests(testA, testB, dependenciesA, dependenciesB);
                
                // If any conflict prevents parallel execution, return false
                return !conflicts.Any(c => c.PreventsParallelExecution);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking parallel execution compatibility between {TestA} and {TestB}", 
                    testA.GetUniqueId(), testB.GetUniqueId());
                
                // Conservative approach - assume they can't run in parallel if we can't determine
                return false;
            }
        }

        /// <summary>
        /// Gets parallel execution recommendations for a set of test methods.
        /// </summary>
        /// <param name="testMethods">Test methods to analyze.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Recommendations for parallel execution.</returns>
        public async Task<ParallelExecutionRecommendations> GetParallelExecutionRecommendationsAsync(
            IReadOnlyList<TestMethod> testMethods,
            CancellationToken cancellationToken = default)
        {
            if (testMethods == null)
                throw new ArgumentNullException(nameof(testMethods));

            var canRunInParallel = new List<(TestMethod, TestMethod)>();
            var mustRunSequentially = new List<(TestMethod, TestMethod, string)>();

            for (int i = 0; i < testMethods.Count; i++)
            {
                for (int j = i + 1; j < testMethods.Count; j++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var testA = testMethods[i];
                    var testB = testMethods[j];

                    var canRun = await CanRunInParallelAsync(testA, testB, cancellationToken);
                    
                    if (canRun)
                    {
                        canRunInParallel.Add((testA, testB));
                    }
                    else
                    {
                        var reason = await GetConflictReason(testA, testB, cancellationToken);
                        mustRunSequentially.Add((testA, testB, reason));
                    }
                }
            }

            return new ParallelExecutionRecommendations(canRunInParallel, mustRunSequentially);
        }

        private Task<List<TestMethod>> GetTestMethodsAsync(
            ITestAssembly testAssembly, 
            CancellationToken cancellationToken)
        {
            var testMethods = new List<TestMethod>();

            try
            {
                var types = testAssembly.GetTypes();

                foreach (var type in types)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                    
                    foreach (var method in methods)
                    {
                        // Check if method has test attributes
                        if (IsTestMethod(method))
                        {
                            var testMethod = new TestMethod(method, type, 
                                testAssembly.AssemblyPath, testAssembly.FrameworkVersion);
                            testMethods.Add(testMethod);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting test methods from assembly: {AssemblyName}", 
                    testAssembly.AssemblyName);
            }

            return Task.FromResult(testMethods);
        }

        private bool IsTestMethod(MethodInfo method)
        {
            var attributes = method.GetCustomAttributes(false);
            return attributes.Any(attr => 
                attr.GetType().Name.Contains("Test") && 
                !attr.GetType().Name.Contains("SetUp") && 
                !attr.GetType().Name.Contains("TearDown"));
        }

        private async Task<List<DataDependency>> DetectDependenciesForTestMethod(
            TestMethod testMethod, 
            CancellationToken cancellationToken)
        {
            var dependencies = new List<DataDependency>();

            foreach (var detector in _patternDetectors)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var detectedDependencies = await detector.DetectDatabaseOperationsAsync(testMethod, cancellationToken);
                    dependencies.AddRange(detectedDependencies);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Detector {DetectorType} failed to analyze test method {TestMethod}", 
                        detector.GetType().Name, testMethod.GetUniqueId());
                }
            }

            return dependencies;
        }

        private List<DataConflict> DetectConflictsBetweenTests(
            TestMethod testA, 
            TestMethod testB,
            List<DataDependency> dependenciesA, 
            List<DataDependency> dependenciesB)
        {
            var conflicts = new List<DataConflict>();

            try
            {
                // Check for shared data dependencies
                foreach (var depA in dependenciesA)
                {
                    foreach (var depB in dependenciesB)
                    {
                        if (SharesDataDependency(depA, depB))
                        {
                            var conflictType = DetermineConflictType(depA, depB);
                            var reason = GetConflictReason(depA, depB);
                            
                            var conflict = new DataConflict(
                                testA.GetUniqueId(),
                                testB.GetUniqueId(),
                                conflictType,
                                reason,
                                new[] { depA, depB });

                            conflicts.Add(conflict);
                        }
                    }
                }

                // Check for shared test fixtures
                if (SharesTestFixture(testA, testB))
                {
                    var conflict = new DataConflict(
                        testA.GetUniqueId(),
                        testB.GetUniqueId(),
                        ConflictType.SharedFixture,
                        $"Both tests use the same test fixture: {testA.ClassName}",
                        Array.Empty<DataDependency>());

                    conflicts.Add(conflict);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error detecting conflicts between {TestA} and {TestB}", 
                    testA.GetUniqueId(), testB.GetUniqueId());
            }

            return conflicts;
        }

        private bool SharesDataDependency(DataDependency depA, DataDependency depB)
        {
            // Same resource identifier
            if (depA.ResourceIdentifier == depB.ResourceIdentifier)
                return true;

            // Shared entity types
            if (depA.EntityTypes.Intersect(depB.EntityTypes).Any())
                return true;

            // Both modify data in the same type of resource
            if (depA.DependencyType == depB.DependencyType &&
                (depA.AccessType == DataAccessType.Write || depA.AccessType == DataAccessType.ReadWrite) &&
                (depB.AccessType == DataAccessType.Write || depB.AccessType == DataAccessType.ReadWrite))
                return true;

            return false;
        }

        private ConflictType DetermineConflictType(DataDependency depA, DataDependency depB)
        {
            // If both write to the same resource, it's shared data
            if (depA.ResourceIdentifier == depB.ResourceIdentifier &&
                ((depA.AccessType == DataAccessType.Write || depA.AccessType == DataAccessType.ReadWrite) ||
                 (depB.AccessType == DataAccessType.Write || depB.AccessType == DataAccessType.ReadWrite)))
            {
                return ConflictType.SharedData;
            }

            // If they access the same entity types
            if (depA.EntityTypes.Intersect(depB.EntityTypes).Any())
            {
                return ConflictType.ResourceContention;
            }

            return ConflictType.PotentialRaceCondition;
        }

        private string GetConflictReason(DataDependency depA, DataDependency depB)
        {
            if (depA.ResourceIdentifier == depB.ResourceIdentifier)
            {
                return $"Both tests access the same resource: {depA.ResourceIdentifier}";
            }

            var sharedEntities = depA.EntityTypes.Intersect(depB.EntityTypes).ToList();
            if (sharedEntities.Any())
            {
                return $"Both tests access shared entities: {string.Join(", ", sharedEntities)}";
            }

            return $"Potential conflict between {depA.DependencyType} operations";
        }

        private bool SharesTestFixture(TestMethod testA, TestMethod testB)
        {
            return testA.ClassName == testB.ClassName;
        }

        private async Task<string> GetConflictReason(TestMethod testA, TestMethod testB, CancellationToken cancellationToken)
        {
            try
            {
                var dependenciesA = await DetectDependenciesForTestMethod(testA, cancellationToken);
                var dependenciesB = await DetectDependenciesForTestMethod(testB, cancellationToken);

                var conflicts = DetectConflictsBetweenTests(testA, testB, dependenciesA, dependenciesB);
                
                if (conflicts.Any())
                {
                    return conflicts.First().ConflictReason;
                }

                return "Unknown conflict reason";
            }
            catch
            {
                return "Error determining conflict reason";
            }
        }
    }

    /// <summary>
    /// Recommendations for parallel test execution.
    /// </summary>
    public class ParallelExecutionRecommendations
    {
        public ParallelExecutionRecommendations(
            List<(TestMethod, TestMethod)> canRunInParallel,
            List<(TestMethod, TestMethod, string)> mustRunSequentially)
        {
            CanRunInParallel = canRunInParallel ?? throw new ArgumentNullException(nameof(canRunInParallel));
            MustRunSequentially = mustRunSequentially ?? throw new ArgumentNullException(nameof(mustRunSequentially));
        }

        public List<(TestMethod TestA, TestMethod TestB)> CanRunInParallel { get; }
        public List<(TestMethod TestA, TestMethod TestB, string Reason)> MustRunSequentially { get; }

        public int TotalTestPairs => CanRunInParallel.Count + MustRunSequentially.Count;
        public double ParallelExecutionRatio => TotalTestPairs > 0 ? (double)CanRunInParallel.Count / TotalTestPairs : 0;
        
        /// <summary>
        /// Test method to validate coverage analysis functionality.
        /// </summary>
        public bool HasConflicts() => MustRunSequentially.Count > 0;
    }
}// Coverage test modification
