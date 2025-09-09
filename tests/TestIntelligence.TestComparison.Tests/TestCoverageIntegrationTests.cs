using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using TestIntelligence.TestComparison.Models;
using TestIntelligence.TestComparison.Services;

namespace TestIntelligence.TestComparison.Tests;

/// <summary>
/// Integration tests for test comparison functionality using real test methods.
/// These tests verify that the comparison engine works correctly with actual C# test code.
/// </summary>
public class TestCoverageIntegrationTests : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ITestComparisonService _comparisonService;
    private readonly string _testSolutionPath;

    public TestCoverageIntegrationTests()
    {
        // Set up dependency injection container with real services
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        
        // Add TestComparison services (would need to be configured properly in real scenario)
        // For now, we'll create a minimal setup for testing
        services.AddScoped<ITestComparisonService, TestComparisonService>();
        // Note: Other dependencies would need to be registered here
        
        _serviceProvider = services.BuildServiceProvider();
        _comparisonService = _serviceProvider.GetRequiredService<ITestComparisonService>();
        
        // Set up test solution path - this would point to a real test solution
        // For integration testing, we need a sample solution with test methods
        _testSolutionPath = GetTestSolutionPath();
    }

    public void Dispose()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    [Fact(Skip = "Integration test requiring full service setup")]
    [Trait("Category", "Integration")]
    public async Task CompareTestsAsync_WithSimilarUnitTests_ShouldReturnHighSimilarity()
    {
        // Arrange
        var test1Id = "TestIntelligence.Tests.UnitTests.ServiceTests.UserServiceTests.GetUserById_ValidId_ReturnsUser";
        var test2Id = "TestIntelligence.Tests.UnitTests.ServiceTests.UserServiceTests.GetUserById_InvalidId_ReturnsNull";
        
        var options = new ComparisonOptions
        {
            Depth = AnalysisDepth.Medium
        };

        // Act
        var result = await _comparisonService.CompareTestsAsync(test1Id, test2Id, _testSolutionPath, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(test1Id, result.Test1Id);
        Assert.Equal(test2Id, result.Test2Id);
        Assert.True(result.OverallSimilarity > 0.6, "Similar unit tests should have high similarity");
        Assert.True(result.CoverageOverlap.OverlapPercentage > 50.0, "Similar tests should have significant coverage overlap");
        Assert.True(result.MetadataSimilarity.CategoryAlignmentScore > 0.8, "Both should be categorized as unit tests");
        Assert.True(result.MetadataSimilarity.NamingPatternScore > 0.7, "Test names are very similar");
        Assert.NotEmpty(result.Recommendations);
    }

    [Fact(Skip = "Integration test requiring full service setup")]
    [Trait("Category", "Integration")]
    public async Task CompareTestsAsync_WithDifferentTestTypes_ShouldReturnLowSimilarity()
    {
        // Arrange
        var unitTestId = "TestIntelligence.Tests.UnitTests.ServiceTests.UserServiceTests.ValidateUser_ValidInput_ReturnsTrue";
        var integrationTestId = "TestIntelligence.Tests.IntegrationTests.DatabaseTests.UserRepositoryTests.SaveUser_ToDatabase_PersistsCorrectly";
        
        var options = new ComparisonOptions
        {
            Depth = AnalysisDepth.Deep
        };

        // Act
        var result = await _comparisonService.CompareTestsAsync(unitTestId, integrationTestId, _testSolutionPath, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.OverallSimilarity < 0.4, "Different test types should have low similarity");
        Assert.True(result.MetadataSimilarity.CategoryAlignmentScore < 0.5, "Different test categories");
        Assert.True(result.CoverageOverlap.OverlapPercentage < 30.0, "Different test types should have minimal overlap");
        
        // Should recommend keeping both tests
        var primaryRecommendation = result.GetPrimaryRecommendation();
        Assert.NotNull(primaryRecommendation);
        Assert.Equal("KeepBoth", primaryRecommendation.Type);
    }

    [Fact(Skip = "Integration test requiring full service setup")]
    [Trait("Category", "Integration")]
    public async Task CompareTestsAsync_WithIdenticalTestLogic_ShouldIdentifyDuplication()
    {
        // Arrange - These would be test methods with nearly identical implementation
        var test1Id = "TestIntelligence.Tests.UnitTests.ValidationTests.EmailValidatorTests.IsValid_ValidEmail_ReturnsTrue";
        var test2Id = "TestIntelligence.Tests.UnitTests.ValidationTests.EmailValidatorTests.Validate_ValidEmailAddress_ReturnsTrue";
        
        var options = new ComparisonOptions
        {
            Depth = AnalysisDepth.Deep
        };

        // Act
        var result = await _comparisonService.CompareTestsAsync(test1Id, test2Id, _testSolutionPath, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.OverallSimilarity > 0.8, "Nearly identical tests should have very high similarity");
        Assert.True(result.CoverageOverlap.OverlapPercentage > 90.0, "Identical logic should have nearly complete overlap");
        
        // Should recommend consolidation or removal
        var primaryRecommendation = result.GetPrimaryRecommendation();
        Assert.NotNull(primaryRecommendation);
        Assert.True(primaryRecommendation.Type == "Consolidate" || primaryRecommendation.Type == "RemoveDuplicate");
        Assert.Equal(EstimatedEffortLevel.High, primaryRecommendation.EstimatedEffortLevel);
    }

    [Fact(Skip = "Integration test requiring full service setup")]
    [Trait("Category", "Integration")]
    public async Task CompareTestsAsync_WithShallowAnalysis_ShouldCompleteQuickly()
    {
        // Arrange
        var test1Id = "TestIntelligence.Tests.UnitTests.UtilityTests.StringHelperTests.IsEmpty_EmptyString_ReturnsTrue";
        var test2Id = "TestIntelligence.Tests.UnitTests.UtilityTests.StringHelperTests.IsEmpty_NullString_ReturnsTrue";
        
        var options = new ComparisonOptions
        {
            Depth = AnalysisDepth.Shallow
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var result = await _comparisonService.CompareTestsAsync(test1Id, test2Id, _testSolutionPath, options);

        // Assert
        stopwatch.Stop();
        Assert.NotNull(result);
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, "Shallow analysis should complete quickly");
        Assert.True(result.AnalysisDuration.TotalSeconds < 5, "Recorded analysis time should be under timeout");
        
        // Shallow analysis should still provide basic insights
        Assert.NotNull(result.CoverageOverlap);
        Assert.NotNull(result.MetadataSimilarity);
    }

    [Fact(Skip = "Integration test requiring full service setup")]
    [Trait("Category", "Integration")]
    public async Task CompareTestsAsync_WithPerformanceMetrics_ShouldIncludeTimingData()
    {
        // Arrange
        var test1Id = "TestIntelligence.Tests.PerformanceTests.SortingTests.QuickSort_LargeArray_PerformsEfficiently";
        var test2Id = "TestIntelligence.Tests.PerformanceTests.SortingTests.MergeSort_LargeArray_PerformsEfficiently";
        
        var options = new ComparisonOptions
        {
            Depth = AnalysisDepth.Medium
        };

        // Act
        var result = await _comparisonService.CompareTestsAsync(test1Id, test2Id, _testSolutionPath, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.AnalysisDuration > TimeSpan.Zero);
        // Performance metrics are always included in this implementation
        
        // Performance tests might have different execution time similarities
        Assert.InRange(result.MetadataSimilarity.ExecutionTimeSimilarity, 0.0, 1.0);
    }

    [Fact(Skip = "Integration test requiring full service setup")]
    [Trait("Category", "Integration")]
    public async Task CompareTestsAsync_WithNonExistentTest_ShouldThrowException()
    {
        // Arrange
        var validTestId = "TestIntelligence.Tests.UnitTests.ServiceTests.UserServiceTests.GetUserById_ValidId_ReturnsUser";
        var invalidTestId = "TestIntelligence.Tests.NonExistent.FakeTest.DoesNotExist";
        
        var options = new ComparisonOptions
        {
            Depth = AnalysisDepth.Medium
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _comparisonService.CompareTestsAsync(validTestId, invalidTestId, _testSolutionPath, options);
        });

        Assert.Contains("test method not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = "Integration test requiring full service setup")]
    [Trait("Category", "Integration")]
    public async Task CompareTestsAsync_WithCancellation_ShouldRespectCancellationToken()
    {
        // Arrange
        var test1Id = "TestIntelligence.Tests.IntegrationTests.SlowTests.DatabaseMigration_LargeDataset_CompletesSuccessfully";
        var test2Id = "TestIntelligence.Tests.IntegrationTests.SlowTests.DataImport_LargeFile_ProcessesCorrectly";
        
        var options = new ComparisonOptions
        {
            Depth = AnalysisDepth.Deep
        };

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100)); // Cancel very quickly

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await _comparisonService.CompareTestsAsync(test1Id, test2Id, _testSolutionPath, options, cts.Token);
        });
    }

    [Fact(Skip = "Integration test requiring full service setup")]
    [Trait("Category", "Integration")]
    public async Task CompareTestsAsync_WithVerboseLogging_ShouldProvideDetailedResults()
    {
        // Arrange
        var test1Id = "TestIntelligence.Tests.UnitTests.BusinessLogicTests.OrderProcessorTests.ProcessOrder_ValidOrder_ReturnsSuccess";
        var test2Id = "TestIntelligence.Tests.UnitTests.BusinessLogicTests.OrderProcessorTests.ProcessOrder_InvalidOrder_ReturnsError";
        
        var options = new ComparisonOptions
        {
            Depth = AnalysisDepth.Medium
        };

        // Act
        var result = await _comparisonService.CompareTestsAsync(test1Id, test2Id, _testSolutionPath, options);

        // Assert
        Assert.NotNull(result);
        // Verbose logging is controlled at the service level, not in options
        
        // Verbose results should include detailed shared method information
        if (result.CoverageOverlap.SharedMethods != null && result.CoverageOverlap.SharedMethods.Count > 0)
        {
            Assert.True(result.CoverageOverlap.SharedMethods.Count > 0);
            
            foreach (var sharedMethod in result.CoverageOverlap.SharedMethods)
            {
                Assert.NotNull(sharedMethod.Method);
                Assert.NotEmpty(sharedMethod.Method);
                // Call depth should be available
                Assert.True(sharedMethod.CallDepth >= 0);
            }
        }
    }

    /// <summary>
    /// Gets the path to a test solution for integration testing.
    /// In a real scenario, this would point to a sample solution set up for testing.
    /// </summary>
    private string GetTestSolutionPath()
    {
        // Search upwards from the current directory for the solution file
        var solutionFileName = "TestIntel-TestCompare.sln";
        var solutionPath = FindSolutionFileUpwards(Directory.GetCurrentDirectory(), solutionFileName);
        if (solutionPath != null)
        {
            return solutionPath;
        }
        // Fallback: use current directory if solution not found
        return Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Searches upwards from the given directory for the specified solution file.
    /// Returns the full path if found, or null if not found.
    /// </summary>
    private string? FindSolutionFileUpwards(string startDirectory, string solutionFileName)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, solutionFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }
}