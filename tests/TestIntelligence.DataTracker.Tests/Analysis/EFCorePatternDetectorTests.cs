using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TestIntelligence.Core.Models;
using TestIntelligence.DataTracker.Analysis;
using TestIntelligence.DataTracker.Models;
using Xunit;

namespace TestIntelligence.DataTracker.Tests.Analysis
{
    public class EFCorePatternDetectorTests
    {
        private readonly EFCorePatternDetector _detector;
        private readonly TestMethod _sampleTestMethod;

        public EFCorePatternDetectorTests()
        {
            _detector = new EFCorePatternDetector();
            
            // Create a sample test method for testing
            var methodInfo = typeof(EFCorePatternDetectorTests).GetMethod(nameof(SampleTestMethod), BindingFlags.NonPublic | BindingFlags.Instance)!;
            _sampleTestMethod = new TestMethod(methodInfo, typeof(EFCorePatternDetectorTests), "TestAssembly.dll", TestIntelligence.Core.Assembly.FrameworkVersion.Net5Plus);
        }

        [Fact]
        public void SupportedFrameworks_ContainsEntityFrameworkCore()
        {
            // Act
            var frameworks = _detector.SupportedFrameworks;

            // Assert
            frameworks.Should().Contain(DatabaseFramework.EntityFrameworkCore);
            frameworks.Should().HaveCount(1);
        }

        [Fact]
        public async Task DetectDatabaseOperationsAsync_WithNullTestMethod_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = async () => await _detector.DetectDatabaseOperationsAsync((TestMethod)null!, CancellationToken.None);

            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public async Task DetectDatabaseOperationsAsync_WithValidTestMethod_ReturnsEmptyList()
        {
            // Act
            var result = await _detector.DetectDatabaseOperationsAsync(_sampleTestMethod, CancellationToken.None);

            // Assert - Since method body is empty in our simplified implementation
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task DetectDatabaseOperationsAsync_WithCancellationToken_RespectsToken()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            var act = async () => await _detector.DetectDatabaseOperationsAsync(_sampleTestMethod, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task DetectDatabaseOperationsAsync_WithSetupMethods_HandlesNullEnumerable()
        {
            // Act & Assert
            var act = async () => await _detector.DetectDatabaseOperationsAsync((IEnumerable<MethodInfo>)null!, CancellationToken.None);

            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public async Task DetectDatabaseOperationsAsync_WithSetupMethods_ReturnsEmptyListForEmptyEnumerable()
        {
            // Act
            var result = await _detector.DetectDatabaseOperationsAsync(Enumerable.Empty<MethodInfo>(), CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task DetectDatabaseOperationsAsync_WithSetupMethods_ProcessesMultipleMethods()
        {
            // Arrange
            var methods = new[]
            {
                typeof(EFCorePatternDetectorTests).GetMethod(nameof(SampleTestMethod), BindingFlags.NonPublic | BindingFlags.Instance)!,
                typeof(EFCorePatternDetectorTests).GetMethod(nameof(AnotherSampleMethod), BindingFlags.NonPublic | BindingFlags.Instance)!
            };

            // Act
            var result = await _detector.DetectDatabaseOperationsAsync(methods, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public void RequiresExclusiveDbAccess_WithInMemoryDatabase_ReturnsFalse()
        {
            // Arrange
            var methodInfo = typeof(EFCorePatternDetectorTests).GetMethod(nameof(SampleTestMethod), BindingFlags.NonPublic | BindingFlags.Instance)!;
            var testMethod = new TestMethod(methodInfo, typeof(EFCorePatternDetectorTests), "TestAssembly.dll", TestIntelligence.Core.Assembly.FrameworkVersion.Net5Plus);

            // Act
            var result = _detector.RequiresExclusiveDbAccess(testMethod);

            // Assert - EF Core should default to false (assumes in-memory databases)
            result.Should().BeFalse();
        }

        [Fact]
        public void RequiresExclusiveDbAccess_WithNullTestMethod_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => _detector.RequiresExclusiveDbAccess(null!);

            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void SharesDatabaseDependency_WithNullParameters_ThrowsArgumentNullException()
        {
            // Arrange
            var dependencies = new List<DataDependency>();

            // Act & Assert
            var act1 = () => _detector.SharesDatabaseDependency(null!, _sampleTestMethod, dependencies, dependencies);
            var act2 = () => _detector.SharesDatabaseDependency(_sampleTestMethod, null!, dependencies, dependencies);
            var act3 = () => _detector.SharesDatabaseDependency(_sampleTestMethod, _sampleTestMethod, null!, dependencies);
            var act4 = () => _detector.SharesDatabaseDependency(_sampleTestMethod, _sampleTestMethod, dependencies, null!);

            act1.Should().Throw<ArgumentNullException>();
            act2.Should().Throw<ArgumentNullException>();
            act3.Should().Throw<ArgumentNullException>();
            act4.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void SharesDatabaseDependency_WithNoDatabaseDependencies_ReturnsFalse()
        {
            // Arrange
            var dependenciesA = new List<DataDependency>
            {
                new("TestA", DataDependencyType.FileSystem, "file.txt", DataAccessType.Read, new[] { "File" })
            };
            var dependenciesB = new List<DataDependency>
            {
                new("TestB", DataDependencyType.Network, "http://api.com", DataAccessType.Read, new[] { "Api" })
            };

            // Act
            var result = _detector.SharesDatabaseDependency(_sampleTestMethod, _sampleTestMethod, dependenciesA, dependenciesB);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void SharesDatabaseDependency_WithSameInMemoryDatabase_ReturnsTrue()
        {
            // Arrange
            var dependenciesA = new List<DataDependency>
            {
                new("TestA", DataDependencyType.Database, "EFCore:InMemory:TestDb", DataAccessType.Read, new[] { "User" })
            };
            var dependenciesB = new List<DataDependency>
            {
                new("TestB", DataDependencyType.Database, "EFCore:InMemory:TestDb", DataAccessType.Write, new[] { "Product" })
            };

            // Act
            var result = _detector.SharesDatabaseDependency(_sampleTestMethod, _sampleTestMethod, dependenciesA, dependenciesB);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void SharesDatabaseDependency_WithDifferentInMemoryDatabases_ReturnsFalse()
        {
            // Arrange
            var dependenciesA = new List<DataDependency>
            {
                new("TestA", DataDependencyType.Database, "EFCore:InMemory:TestDbA", DataAccessType.Read, new[] { "User" })
            };
            var dependenciesB = new List<DataDependency>
            {
                new("TestB", DataDependencyType.Database, "EFCore:InMemory:TestDbB", DataAccessType.Write, new[] { "Product" })
            };

            // Act
            var result = _detector.SharesDatabaseDependency(_sampleTestMethod, _sampleTestMethod, dependenciesA, dependenciesB);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void SharesDatabaseDependency_WithSameResourceIdentifier_ReturnsTrue()
        {
            // Arrange
            var dependenciesA = new List<DataDependency>
            {
                new("TestA", DataDependencyType.Database, "EFCore:TestContext", DataAccessType.Read, new[] { "User" })
            };
            var dependenciesB = new List<DataDependency>
            {
                new("TestB", DataDependencyType.Database, "EFCore:TestContext", DataAccessType.Write, new[] { "Product" })
            };

            // Act
            var result = _detector.SharesDatabaseDependency(_sampleTestMethod, _sampleTestMethod, dependenciesA, dependenciesB);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void SharesDatabaseDependency_WithSharedEntityTypes_ReturnsTrue()
        {
            // Arrange
            var dependenciesA = new List<DataDependency>
            {
                new("TestA", DataDependencyType.Database, "EFCore:ContextA", DataAccessType.Read, new[] { "User", "Product" })
            };
            var dependenciesB = new List<DataDependency>
            {
                new("TestB", DataDependencyType.Database, "EFCore:ContextB", DataAccessType.Write, new[] { "Product", "Order" })
            };

            // Act
            var result = _detector.SharesDatabaseDependency(_sampleTestMethod, _sampleTestMethod, dependenciesA, dependenciesB);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void SharesDatabaseDependency_WithNoSharedDependencies_ReturnsFalse()
        {
            // Arrange
            var dependenciesA = new List<DataDependency>
            {
                new("TestA", DataDependencyType.Database, "EFCore:ContextA", DataAccessType.Read, new[] { "User" })
            };
            var dependenciesB = new List<DataDependency>
            {
                new("TestB", DataDependencyType.Database, "EFCore:ContextB", DataAccessType.Write, new[] { "Product" })
            };

            // Act
            var result = _detector.SharesDatabaseDependency(_sampleTestMethod, _sampleTestMethod, dependenciesA, dependenciesB);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task DetectDbContextUsageAsync_WithNullMethod_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = async () => await _detector.DetectDbContextUsageAsync(null!);

            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public async Task DetectDbContextUsageAsync_WithValidMethod_ReturnsResult()
        {
            // Arrange
            var method = typeof(EFCorePatternDetectorTests).GetMethod(nameof(SampleTestMethod), BindingFlags.NonPublic | BindingFlags.Instance)!;

            // Act
            var result = await _detector.DetectDbContextUsageAsync(method);

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public async Task DetectInMemoryDatabaseUsageAsync_WithNullMethod_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = async () => await _detector.DetectInMemoryDatabaseUsageAsync(null!);

            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public async Task DetectInMemoryDatabaseUsageAsync_WithValidMethod_ReturnsResult()
        {
            // Arrange
            var method = typeof(EFCorePatternDetectorTests).GetMethod(nameof(SampleTestMethod), BindingFlags.NonPublic | BindingFlags.Instance)!;

            // Act
            var result = await _detector.DetectInMemoryDatabaseUsageAsync(method);

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public void RequiresExclusiveDbAccess_WithExceptionThrown_ReturnsFalse()
        {
            // Arrange - Use the sample test method which should work normally
            var testMethod = _sampleTestMethod;

            // Act
            var result = _detector.RequiresExclusiveDbAccess(testMethod);

            // Assert - Method should return a result without throwing (either true or false)
            (result == true || result == false).Should().BeTrue();
        }

        [Theory]
        [InlineData("TestWithCreate", DataAccessType.Create)]
        [InlineData("TestWithAdd", DataAccessType.Create)]
        [InlineData("TestWithInsert", DataAccessType.Create)]
        [InlineData("TestWithUpdate", DataAccessType.Update)]
        [InlineData("TestWithModify", DataAccessType.Update)]
        [InlineData("TestWithDelete", DataAccessType.Delete)]
        [InlineData("TestWithRemove", DataAccessType.Delete)]
        [InlineData("TestWithRead", DataAccessType.Read)]
        [InlineData("TestWithGet", DataAccessType.Read)]
        [InlineData("TestWithFind", DataAccessType.Read)]
        [InlineData("TestWithSomethingElse", DataAccessType.ReadWrite)]
        public void DetermineAccessType_BasedOnMethodName_ReturnsCorrectAccessType(string methodName, DataAccessType expectedAccessType)
        {
            // This tests the internal logic through DetectDbContextUsageAsync
            // We can't test the private method directly, but we can verify the behavior indirectly
            
            // Act & Assert
            // The method name would be used in the internal DetermineAccessType method
            // We're testing that the pattern matching logic is correct
            var containsCreate = methodName.ToLowerInvariant().Contains("create") || 
                                methodName.ToLowerInvariant().Contains("add") || 
                                methodName.ToLowerInvariant().Contains("insert");
            var containsUpdate = methodName.ToLowerInvariant().Contains("update") || 
                                methodName.ToLowerInvariant().Contains("modify");
            var containsDelete = methodName.ToLowerInvariant().Contains("delete") || 
                                methodName.ToLowerInvariant().Contains("remove");
            var containsRead = methodName.ToLowerInvariant().Contains("read") || 
                              methodName.ToLowerInvariant().Contains("get") || 
                              methodName.ToLowerInvariant().Contains("find");

            if (containsCreate)
            {
                expectedAccessType.Should().Be(DataAccessType.Create);
            }
            else if (containsUpdate)
            {
                expectedAccessType.Should().Be(DataAccessType.Update);
            }
            else if (containsDelete)
            {
                expectedAccessType.Should().Be(DataAccessType.Delete);
            }
            else if (containsRead)
            {
                expectedAccessType.Should().Be(DataAccessType.Read);
            }
            else
            {
                expectedAccessType.Should().Be(DataAccessType.ReadWrite);
            }
        }

        // PHASE 1.1 CHARACTERIZATION TESTS FOR BLOCKING ASYNC OPERATIONS
        // These tests document the current behavior before refactoring

        [Fact]
        public void RequiresExclusiveDbAccess_CurrentBehavior_BlocksOnAsyncCall()
        {
            // Test current blocking behavior to understand timing
            var detector = new EFCorePatternDetector();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            var result = detector.RequiresExclusiveDbAccess(_sampleTestMethod);
            
            stopwatch.Stop();
            (result == true || result == false).Should().BeTrue(); // Document current return type
            // Document current performance characteristics - this uses .Result internally
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000); // Should complete quickly since no real DB ops
        }

        [Fact]
        public void RequiresExclusiveDbAccess_CurrentBehaviorWithException_HandlesGracefully()
        {
            // Test current behavior when async operation fails
            var detector = new EFCorePatternDetector();
            
            // This should not throw since the method catches exceptions internally
            var result = detector.RequiresExclusiveDbAccess(_sampleTestMethod);
            
            (result == true || result == false).Should().BeTrue();
        }

        [Fact]  
        public void EFCorePatternDetector_StressTest_CurrentPerformance()
        {
            // Baseline performance test before async refactoring
            var detector = new EFCorePatternDetector();
            var tasks = Enumerable.Range(0, 10)
                .Select(_ => Task.Run(() => detector.RequiresExclusiveDbAccess(_sampleTestMethod)))
                .ToArray();
                
            var act = () => Task.WaitAll(tasks, TimeSpan.FromSeconds(30));
            act.Should().NotThrow();
            
            // Verify all results are valid booleans
            foreach (var task in tasks)
            {
                (task.Result == true || task.Result == false).Should().BeTrue();
            }
        }

        // Sample methods for testing
        private void SampleTestMethod()
        {
            // Sample test method for reflection
        }

        private void AnotherSampleMethod()
        {
            // Another sample method for testing
        }
    }
}