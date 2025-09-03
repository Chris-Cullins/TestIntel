using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Models;

namespace TestIntelligence.Core.Discovery
{
    /// <summary>
    /// NUnit-specific test discovery implementation that identifies test fixtures and methods.
    /// </summary>
    public class NUnitTestDiscovery : ITestDiscovery
    {
        private static readonly HashSet<string> TestFixtureAttributeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TestFixtureAttribute",
            "NUnit.Framework.TestFixtureAttribute",
            "TestClassAttribute",
            "Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute"
        };

        private static readonly HashSet<string> TestMethodAttributeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TestAttribute",
            "TestCaseAttribute", 
            "NUnit.Framework.TestAttribute",
            "NUnit.Framework.TestCaseAttribute",
            "FactAttribute",
            "TheoryAttribute",
            "Xunit.FactAttribute",
            "Xunit.TheoryAttribute"
        };

        private static readonly HashSet<string> SetupAttributeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SetUpAttribute",
            "OneTimeSetUpAttribute",
            "TearDownAttribute", 
            "OneTimeTearDownAttribute",
            "NUnit.Framework.SetUpAttribute",
            "NUnit.Framework.OneTimeSetUpAttribute",
            "NUnit.Framework.TearDownAttribute",
            "NUnit.Framework.OneTimeTearDownAttribute"
        };

        /// <inheritdoc />
        public event EventHandler<TestDiscoveryStartedEventArgs>? DiscoveryStarted;

        /// <inheritdoc />
        public event EventHandler<TestDiscoveryCompletedEventArgs>? DiscoveryCompleted;

        /// <inheritdoc />
        public event EventHandler<TestDiscoveryErrorEventArgs>? DiscoveryError;

        /// <inheritdoc />
        public async Task<TestDiscoveryResult> DiscoverTestsAsync(ITestAssembly testAssembly, CancellationToken cancellationToken = default)
        {
            if (testAssembly == null)
                throw new ArgumentNullException(nameof(testAssembly));

            var assemblyPath = testAssembly.AssemblyPath;
            var errors = new List<string>();

            try
            {
                OnDiscoveryStarted(assemblyPath);

                var testFixtures = await Task.Run(() => DiscoverTestFixturesInAssembly(testAssembly, errors), cancellationToken)
                    .ConfigureAwait(false);

                var result = new TestDiscoveryResult(
                    assemblyPath,
                    testAssembly.FrameworkVersion,
                    testFixtures,
                    errors);

                OnDiscoveryCompleted(assemblyPath, result);
                return result;
            }
            catch (Exception ex)
            {
                errors.Add($"Fatal error during discovery: {ex.Message}");
                OnDiscoveryError(assemblyPath, ex);
                
                return new TestDiscoveryResult(
                    assemblyPath,
                    testAssembly.FrameworkVersion,
                    Array.Empty<TestFixture>(),
                    errors);
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyDictionary<string, TestDiscoveryResult>> DiscoverTestsAsync(
            IEnumerable<ITestAssembly> testAssemblies, 
            CancellationToken cancellationToken = default)
        {
            if (testAssemblies == null)
                throw new ArgumentNullException(nameof(testAssemblies));

            var assemblies = testAssemblies.ToList();
            var results = new ConcurrentDictionary<string, TestDiscoveryResult>();

            // Process assemblies in parallel for better performance
            var tasks = assemblies.Select(async assembly =>
            {
                var result = await DiscoverTestsAsync(assembly, cancellationToken).ConfigureAwait(false);
                results.TryAdd(assembly.AssemblyPath, result);
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return results;
        }

        /// <inheritdoc />
        public bool IsTestFixture(Type type)
        {
            if (type == null)
                return false;

            // Check for explicit TestFixture attribute
            var hasTestFixtureAttribute = type.GetCustomAttributes(inherit: false)
                .Any(attr => TestFixtureAttributeNames.Contains(attr.GetType().Name) ||
                           TestFixtureAttributeNames.Contains(attr.GetType().FullName ?? ""));

            if (hasTestFixtureAttribute)
                return true;

            // Check for test methods (NUnit allows fixtures without explicit [TestFixture] if they have test methods)
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            return methods.Any(IsTestMethod);
        }

        /// <inheritdoc />
        public bool IsTestMethod(MethodInfo method)
        {
            if (method == null)
                return false;

            // Added enhanced attribute checking for test coverage analysis
            var attributes = method.GetCustomAttributes(inherit: false);
            var isTestMethod = attributes.Any(attr => 
                TestMethodAttributeNames.Contains(attr.GetType().Name) ||
                TestMethodAttributeNames.Contains(attr.GetType().FullName ?? "") ||
                SetupAttributeNames.Contains(attr.GetType().Name) ||
                SetupAttributeNames.Contains(attr.GetType().FullName ?? ""));
                
            return isTestMethod;
        }

        /// <summary>
        /// Discovers all test fixtures in the given assembly.
        /// </summary>
        private IReadOnlyList<TestFixture> DiscoverTestFixturesInAssembly(ITestAssembly testAssembly, List<string> errors)
        {
            var fixtures = new List<TestFixture>();

            try
            {
                var assembly = testAssembly.UnderlyingAssembly;
                var types = GetTypesFromAssembly(assembly, errors);

                foreach (var type in types)
                {
                    try
                    {
                        if (IsTestFixture(type))
                        {
                            var fixture = new TestFixture(type, testAssembly.AssemblyPath, testAssembly.FrameworkVersion);
                            
                            // Only include fixtures that actually have tests
                            if (fixture.HasTests || fixture.HasSetUpMethods || fixture.HasTearDownMethods)
                            {
                                fixtures.Add(fixture);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Error processing type '{type.FullName}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Error discovering fixtures in assembly: {ex.Message}");
            }

            return fixtures.AsReadOnly();
        }

        /// <summary>
        /// Safely gets all types from an assembly, handling ReflectionTypeLoadException.
        /// </summary>
        private IEnumerable<Type> GetTypesFromAssembly(System.Reflection.Assembly assembly, List<string> errors)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Log loader exceptions but continue with successfully loaded types
                if (ex.LoaderExceptions != null)
                {
                    foreach (var loaderException in ex.LoaderExceptions)
                    {
                        if (loaderException != null)
                        {
                            errors.Add($"Type load error: {loaderException.Message}");
                        }
                    }
                }

                // Return the types that did load successfully
                return ex.Types.Where(t => t != null)!;
            }
            catch (Exception ex)
            {
                errors.Add($"Error getting types from assembly: {ex.Message}");
                return Enumerable.Empty<Type>();
            }
        }

        /// <summary>
        /// Raises the DiscoveryStarted event.
        /// </summary>
        protected virtual void OnDiscoveryStarted(string assemblyPath)
        {
            DiscoveryStarted?.Invoke(this, new TestDiscoveryStartedEventArgs(assemblyPath));
        }

        /// <summary>
        /// Raises the DiscoveryCompleted event.
        /// </summary>
        protected virtual void OnDiscoveryCompleted(string assemblyPath, TestDiscoveryResult result)
        {
            DiscoveryCompleted?.Invoke(this, new TestDiscoveryCompletedEventArgs(assemblyPath, result));
        }

        /// <summary>
        /// Raises the DiscoveryError event.
        /// </summary>
        protected virtual void OnDiscoveryError(string assemblyPath, Exception exception)
        {
            DiscoveryError?.Invoke(this, new TestDiscoveryErrorEventArgs(assemblyPath, exception));
        }
    }
}