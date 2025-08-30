using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Models;

namespace TestIntelligence.Core.Discovery
{
    /// <summary>
    /// Defines the contract for discovering tests in assemblies.
    /// </summary>
    public interface ITestDiscovery
    {
        /// <summary>
        /// Discovers all test fixtures and methods in the specified assembly.
        /// </summary>
        /// <param name="testAssembly">The test assembly to analyze.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Discovery result containing all found test fixtures.</returns>
        Task<TestDiscoveryResult> DiscoverTestsAsync(ITestAssembly testAssembly, CancellationToken cancellationToken = default);

        /// <summary>
        /// Discovers all test fixtures and methods in the specified assemblies.
        /// </summary>
        /// <param name="testAssemblies">The test assemblies to analyze.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Discovery results for all assemblies.</returns>
        Task<IReadOnlyDictionary<string, TestDiscoveryResult>> DiscoverTestsAsync(
            IEnumerable<ITestAssembly> testAssemblies, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a type appears to be a test fixture based on attributes and conventions.
        /// </summary>
        /// <param name="type">The type to check.</param>
        /// <returns>True if the type is likely a test fixture.</returns>
        bool IsTestFixture(Type type);

        /// <summary>
        /// Checks if a method appears to be a test method based on attributes.
        /// </summary>
        /// <param name="method">The method to check.</param>
        /// <returns>True if the method is likely a test method.</returns>
        bool IsTestMethod(System.Reflection.MethodInfo method);

        /// <summary>
        /// Event raised when test discovery starts for an assembly.
        /// </summary>
        event EventHandler<TestDiscoveryStartedEventArgs>? DiscoveryStarted;

        /// <summary>
        /// Event raised when test discovery completes for an assembly.
        /// </summary>
        event EventHandler<TestDiscoveryCompletedEventArgs>? DiscoveryCompleted;

        /// <summary>
        /// Event raised when test discovery encounters an error.
        /// </summary>
        event EventHandler<TestDiscoveryErrorEventArgs>? DiscoveryError;
    }

    /// <summary>
    /// Result of test discovery for a single assembly.
    /// </summary>
    public class TestDiscoveryResult
    {
        public TestDiscoveryResult(
            string assemblyPath,
            FrameworkVersion frameworkVersion,
            IReadOnlyList<TestFixture> testFixtures,
            IReadOnlyList<string> errors)
        {
            AssemblyPath = assemblyPath ?? throw new ArgumentNullException(nameof(assemblyPath));
            FrameworkVersion = frameworkVersion;
            TestFixtures = testFixtures ?? throw new ArgumentNullException(nameof(testFixtures));
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
            DiscoveredAt = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// The path to the assembly that was analyzed.
        /// </summary>
        public string AssemblyPath { get; }

        /// <summary>
        /// The framework version of the analyzed assembly.
        /// </summary>
        public FrameworkVersion FrameworkVersion { get; }

        /// <summary>
        /// All test fixtures discovered in the assembly.
        /// </summary>
        public IReadOnlyList<TestFixture> TestFixtures { get; }

        /// <summary>
        /// Any errors encountered during discovery.
        /// </summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>
        /// When the discovery was performed.
        /// </summary>
        public DateTimeOffset DiscoveredAt { get; }

        /// <summary>
        /// Whether the discovery completed successfully.
        /// </summary>
        public bool IsSuccessful => Errors.Count == 0;

        /// <summary>
        /// Total number of test fixtures found.
        /// </summary>
        public int FixtureCount => TestFixtures.Count;

        /// <summary>
        /// Total number of test methods found.
        /// </summary>
        public int TestMethodCount => TestFixtures.Sum(f => f.TestMethods.Count(m => m.IsExecutableTest()));

        /// <summary>
        /// Gets all executable test methods across all fixtures.
        /// </summary>
        public IEnumerable<TestMethod> GetAllTestMethods()
        {
            return TestFixtures.SelectMany(f => f.GetExecutableTests());
        }
    }

    /// <summary>
    /// Event arguments for test discovery started event.
    /// </summary>
    public class TestDiscoveryStartedEventArgs : EventArgs
    {
        public TestDiscoveryStartedEventArgs(string assemblyPath)
        {
            AssemblyPath = assemblyPath;
            StartedAt = DateTimeOffset.UtcNow;
        }

        public string AssemblyPath { get; }
        public DateTimeOffset StartedAt { get; }
    }

    /// <summary>
    /// Event arguments for test discovery completed event.
    /// </summary>
    public class TestDiscoveryCompletedEventArgs : EventArgs
    {
        public TestDiscoveryCompletedEventArgs(string assemblyPath, TestDiscoveryResult result)
        {
            AssemblyPath = assemblyPath;
            Result = result;
            CompletedAt = DateTimeOffset.UtcNow;
        }

        public string AssemblyPath { get; }
        public TestDiscoveryResult Result { get; }
        public DateTimeOffset CompletedAt { get; }
    }

    /// <summary>
    /// Event arguments for test discovery error event.
    /// </summary>
    public class TestDiscoveryErrorEventArgs : EventArgs
    {
        public TestDiscoveryErrorEventArgs(string assemblyPath, Exception exception)
        {
            AssemblyPath = assemblyPath;
            Exception = exception;
            ErrorAt = DateTimeOffset.UtcNow;
        }

        public string AssemblyPath { get; }
        public Exception Exception { get; }
        public DateTimeOffset ErrorAt { get; }
    }
}