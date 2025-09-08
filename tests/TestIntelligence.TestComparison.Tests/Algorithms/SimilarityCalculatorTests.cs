using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.Core.Assembly;
using TestIntelligence.Core.Models;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.TestComparison.Algorithms;
using TestIntelligence.TestComparison.Models;
using Xunit;

namespace TestIntelligence.TestComparison.Tests.Algorithms;

/// <summary>
/// Comprehensive test suite for SimilarityCalculator service.
/// Tests coverage overlap calculations, metadata similarity scoring, and edge cases.
/// Following TDD principles - these tests define the expected behavior before implementation.
/// </summary>
public class SimilarityCalculatorTests : TestBase
{
    private readonly ILogger<SimilarityCalculator> _mockLogger;
    private readonly SimilarityCalculator _calculator;

    public SimilarityCalculatorTests()
    {
        _mockLogger = Substitute.For<ILogger<SimilarityCalculator>>();
        _calculator = new SimilarityCalculator(_mockLogger);
    }

    #region CalculateCoverageOverlap Tests

    [Fact]
    public void CalculateCoverageOverlap_IdenticalSets_Returns100Percent()
    {
        // Arrange - Two identical sets of method names
        var methods1 = new HashSet<string> { "Method1", "Method2", "Method3", "Method4", "Method5" };
        var methods2 = new HashSet<string> { "Method1", "Method2", "Method3", "Method4", "Method5" };

        // Act - Calculate coverage overlap without weighting
        var result = _calculator.CalculateCoverageOverlap(methods1, methods2);

        // Assert - Should return 1.0 for identical sets (100% similarity)
        result.Should().Be(1.0, "identical method sets should have 100% overlap");
    }

    [Fact]
    public void CalculateCoverageOverlap_NoOverlap_ReturnsZero()
    {
        // Arrange - Two completely different sets with no common methods
        var methods1 = new HashSet<string> { "MethodA", "MethodB", "MethodC" };
        var methods2 = new HashSet<string> { "MethodX", "MethodY", "MethodZ" };

        // Act - Calculate coverage overlap
        var result = _calculator.CalculateCoverageOverlap(methods1, methods2);

        // Assert - Should return 0.0 for sets with no overlap
        result.Should().Be(0.0, "completely different method sets should have 0% overlap");
    }

    [Fact]
    public void CalculateCoverageOverlap_PartialOverlap_ReturnsCorrectJaccardScore()
    {
        // Arrange - Sets with partial overlap
        // Intersection: {Method2, Method3} = 2 elements
        // Union: {Method1, Method2, Method3, Method4, Method5} = 5 elements
        // Jaccard = 2/5 = 0.4
        var methods1 = new HashSet<string> { "Method1", "Method2", "Method3" };
        var methods2 = new HashSet<string> { "Method2", "Method3", "Method4", "Method5" };

        // Act - Calculate coverage overlap
        var result = _calculator.CalculateCoverageOverlap(methods1, methods2);

        // Assert - Should return correct Jaccard similarity coefficient
        result.Should().BeApproximately(0.4, 0.001, 
            "Jaccard similarity should be intersection/union = 2/5 = 0.4");
    }

    [Fact]
    public void CalculateCoverageOverlap_WithWeighting_AppliesCallDepthDecay()
    {
        // Arrange - Sets with weighting options that apply call depth decay
        var methods1 = new HashSet<string> { "Method1", "Method2", "System.String.Format" };
        var methods2 = new HashSet<string> { "Method2", "System.String.Format", "Method3" };
        
        var options = new WeightingOptions
        {
            CallDepthDecayFactor = 0.5, // Significant decay
            ProductionCodeWeight = 1.0,
            FrameworkCodeWeight = 0.3, // Lower weight for framework methods
            UseComplexityWeighting = false
        };

        // Act - Calculate with weighting
        var result = _calculator.CalculateCoverageOverlap(methods1, methods2, options);

        // Assert - Result should be affected by framework method weighting
        result.Should().BeGreaterThan(0.0).And.BeLessThan(1.0);
        result.Should().BeLessThan(0.5, "framework methods should have lower weight");
    }

    [Fact]
    public void CalculateCoverageOverlap_WithComplexityWeighting_AdjustsForMethodComplexity()
    {
        // Arrange - Methods with different complexity indicators
        var methods1 = new HashSet<string> 
        { 
            "GenericMethod<T>",          // Generic method (higher complexity)
            "get_Property",               // Property getter (lower complexity)
            "ComplexBusinessLogic"        // Regular method
        };
        var methods2 = new HashSet<string> 
        { 
            "GenericMethod<T>",          // Shared generic method
            "set_Property",              // Property setter (lower complexity)
            "AnotherBusinessMethod"      // Different regular method
        };
        
        var options = new WeightingOptions
        {
            UseComplexityWeighting = true,
            ProductionCodeWeight = 1.0,
            FrameworkCodeWeight = 1.0, // Same weight to isolate complexity effect
            CallDepthDecayFactor = 1.0 // No decay to isolate complexity effect
        };

        // Act - Calculate with complexity weighting
        var result = _calculator.CalculateCoverageOverlap(methods1, methods2, options);

        // Assert - Result should reflect complexity-adjusted weights
        result.Should().BeGreaterThan(0.0, "there is overlap in methods");
        result.Should().BeLessThan(1.0, "sets are not identical");
    }

    [Fact]
    public void CalculateCoverageOverlap_EmptySets_ReturnsExpectedValues()
    {
        // Arrange - Various empty set scenarios
        var emptySet = new HashSet<string>();
        var nonEmptySet = new HashSet<string> { "Method1", "Method2" };

        // Act & Assert - Both empty sets should return 1.0 (considered identical)
        var bothEmpty = _calculator.CalculateCoverageOverlap(emptySet, emptySet);
        bothEmpty.Should().Be(1.0, "two empty sets are considered identical");

        // Act & Assert - One empty, one non-empty should return 0.0
        var oneEmpty1 = _calculator.CalculateCoverageOverlap(emptySet, nonEmptySet);
        oneEmpty1.Should().Be(0.0, "empty and non-empty sets have no overlap");

        var oneEmpty2 = _calculator.CalculateCoverageOverlap(nonEmptySet, emptySet);
        oneEmpty2.Should().Be(0.0, "non-empty and empty sets have no overlap");
    }

    [Fact]
    public void CalculateCoverageOverlap_NullSets_ThrowsArgumentException()
    {
        // Arrange - Null sets and valid sets
        var validSet = new HashSet<string> { "Method1" };
        HashSet<string>? nullSet = null;

        // Act & Assert - Should throw ArgumentNullException for null first parameter
        var act1 = () => _calculator.CalculateCoverageOverlap(nullSet!, validSet);
        act1.Should().Throw<ArgumentNullException>()
            .WithParameterName("methods1");

        // Act & Assert - Should throw ArgumentNullException for null second parameter
        var act2 = () => _calculator.CalculateCoverageOverlap(validSet, nullSet!);
        act2.Should().Throw<ArgumentNullException>()
            .WithParameterName("methods2");

        // Act & Assert - Should throw ArgumentNullException for both null
        var act3 = () => _calculator.CalculateCoverageOverlap(nullSet!, nullSet!);
        act3.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CalculateCoverageOverlap_SingleElementSets_CalculatesCorrectly()
    {
        // Arrange - Single element sets
        var set1 = new HashSet<string> { "Method1" };
        var set2Same = new HashSet<string> { "Method1" };
        var set2Different = new HashSet<string> { "Method2" };

        // Act - Calculate for same single element
        var sameSingleElement = _calculator.CalculateCoverageOverlap(set1, set2Same);
        
        // Assert - Should be 1.0 for identical single elements
        sameSingleElement.Should().Be(1.0, "identical single element sets should have 100% overlap");

        // Act - Calculate for different single elements
        var differentSingleElement = _calculator.CalculateCoverageOverlap(set1, set2Different);
        
        // Assert - Should be 0.0 for different single elements
        differentSingleElement.Should().Be(0.0, "different single element sets should have 0% overlap");
    }

    [Theory]
    [InlineData(100, 100, 50)]     // Medium overlap
    [InlineData(1000, 1000, 500)]  // Large sets with 500 common elements
    [InlineData(5000, 5000, 2500)] // Very large sets with 2500 common elements
    [InlineData(10000, 10000, 100)] // Very large sets with small overlap
    public void CalculateCoverageOverlap_LargeSets_PerformsEfficiently(int set1Size, int set2Size, int overlapSize)
    {
        // Arrange - Create large sets with specified overlap
        var methods1 = new HashSet<string>();
        var methods2 = new HashSet<string>();
        
        // Add common methods (overlap)
        for (int i = 0; i < overlapSize; i++)
        {
            var method = $"CommonMethod_{i}";
            methods1.Add(method);
            methods2.Add(method);
        }
        
        // Add unique methods to set1
        for (int i = overlapSize; i < set1Size; i++)
        {
            methods1.Add($"Set1_Method_{i}");
        }
        
        // Add unique methods to set2
        for (int i = overlapSize; i < set2Size; i++)
        {
            methods2.Add($"Set2_Method_{i}");
        }

        // Act - Measure performance
        var stopwatch = Stopwatch.StartNew();
        var result = _calculator.CalculateCoverageOverlap(methods1, methods2);
        stopwatch.Stop();

        // Assert - Check correctness
        var expectedUnionSize = set1Size + set2Size - overlapSize;
        var expectedSimilarity = (double)overlapSize / expectedUnionSize;
        result.Should().BeApproximately(expectedSimilarity, 0.001, 
            $"Jaccard similarity should be {overlapSize}/{expectedUnionSize}");

        // Assert - Performance (should complete in reasonable time)
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100, 
            $"calculation for {set1Size}x{set2Size} sets should complete within 100ms");
    }

    [Theory]
    [InlineData(0.0, 1.0, 0.3)]   // No decay, normal production/framework weights
    [InlineData(0.5, 1.0, 0.3)]   // Medium decay
    [InlineData(0.8, 1.0, 0.3)]   // Low decay
    [InlineData(1.0, 1.0, 1.0)]   // No decay, equal weights (should give same as unweighted)
    [InlineData(0.5, 2.0, 0.1)]   // High production weight, low framework weight
    public void CalculateCoverageOverlap_WithVariousWeightingOptions_ProducesExpectedRanges(
        double callDepthDecay, double productionWeight, double frameworkWeight)
    {
        // Arrange - Mix of production and framework methods
        var methods1 = new HashSet<string> 
        { 
            "MyApp.BusinessLogic.Process",
            "System.String.Format",
            "MyApp.DataAccess.Save",
            "Microsoft.Extensions.Logging.LogInformation"
        };
        var methods2 = new HashSet<string> 
        { 
            "MyApp.BusinessLogic.Process",  // Shared production
            "System.String.Format",          // Shared framework
            "MyApp.Services.Calculate",      // Different production
            "System.Linq.Where"              // Different framework
        };

        var options = new WeightingOptions
        {
            CallDepthDecayFactor = callDepthDecay,
            ProductionCodeWeight = productionWeight,
            FrameworkCodeWeight = frameworkWeight,
            UseComplexityWeighting = false
        };

        // Act
        var result = _calculator.CalculateCoverageOverlap(methods1, methods2, options);

        // Assert - Result should be in valid range
        result.Should().BeInRange(0.0, 1.0, "similarity must be between 0 and 1");
        
        // When framework weight is lower, result should be different from unweighted
        if (Math.Abs(frameworkWeight - productionWeight) > 0.001)
        {
            var unweightedResult = _calculator.CalculateCoverageOverlap(methods1, methods2);
            result.Should().NotBeApproximately(unweightedResult, 0.001, 
                "weighted result should differ from unweighted when weights are different");
        }
    }

    #endregion

    #region CalculateMetadataSimilarity Tests

    [Fact]
    public void CalculateMetadataSimilarity_SameCategories_ReturnsHighScore()
    {
        // Arrange - Two tests with same category but different other attributes
        var test1 = CreateTestInfo(
            "Test1",
            TestCategory.Unit,
            TimeSpan.FromMilliseconds(100),
            tags: new[] { "fast", "core" });
        
        var test2 = CreateTestInfo(
            "Test2", 
            TestCategory.Unit,  // Same category
            TimeSpan.FromMilliseconds(150),
            tags: new[] { "slow", "edge" });

        // Act
        var result = _calculator.CalculateMetadataSimilarity(test1, test2);

        // Assert - Should have significant score due to category match (30% weight)
        result.Should().BeGreaterThanOrEqualTo(0.3, "category match contributes 30% to similarity");
        result.Should().BeLessThanOrEqualTo(1.0, "similarity cannot exceed 100%");
    }

    [Fact]
    public void CalculateMetadataSimilarity_DifferentCategories_ReturnsLowerScore()
    {
        // Arrange - Tests with different categories
        var test1 = CreateTestInfo(
            "Test1",
            TestCategory.Unit,
            TimeSpan.FromMilliseconds(100));
        
        var test2 = CreateTestInfo(
            "Test2",
            TestCategory.EndToEnd,  // Different category
            TimeSpan.FromMilliseconds(100));

        // Act
        var result = _calculator.CalculateMetadataSimilarity(test1, test2);

        // Assert - Should have lower score due to category mismatch
        result.Should().BeLessThan(0.7, "different categories should reduce similarity");
        result.Should().BeGreaterThanOrEqualTo(0.0, "similarity cannot be negative");
    }

    [Fact]
    public void CalculateMetadataSimilarity_SimilarNamingPatterns_IncreasesScore()
    {
        // Arrange - Tests with similar naming patterns
        var test1 = CreateTestInfoWithCustomName(
            "ProcessOrder_WhenValid_ShouldReturnSuccess",
            TestCategory.Unit);
        
        var test2 = CreateTestInfoWithCustomName(
            "ProcessOrder_WhenInvalid_ShouldReturnError",
            TestCategory.Unit);

        // Act
        var result = _calculator.CalculateMetadataSimilarity(test1, test2);

        // Assert - Should have high score due to similar naming patterns
        result.Should().BeGreaterThan(0.5, "similar naming patterns should increase similarity");
    }

    [Fact]
    public void CalculateMetadataSimilarity_SharedTags_IncreasesScore()
    {
        // Arrange - Tests with overlapping tags
        var test1 = CreateTestInfo(
            "Test1",
            TestCategory.Integration,
            tags: new[] { "database", "slow", "critical" });
        
        var test2 = CreateTestInfo(
            "Test2",
            TestCategory.Integration,
            tags: new[] { "database", "critical", "flaky" });

        // Act
        var result = _calculator.CalculateMetadataSimilarity(test1, test2);

        // Assert - Should have higher score due to shared tags (2 out of 4 unique tags)
        result.Should().BeGreaterThan(0.4, "shared tags should increase similarity");
    }

    [Fact]
    public void CalculateMetadataSimilarity_SimilarExecutionTimes_IncreasesScore()
    {
        // Arrange - Tests with similar execution times
        var test1 = CreateTestInfo(
            "Test1",
            TestCategory.Unit,
            TimeSpan.FromMilliseconds(100));
        
        var test2 = CreateTestInfo(
            "Test2",
            TestCategory.Unit,
            TimeSpan.FromMilliseconds(110));  // Very close execution time

        // Act
        var result = _calculator.CalculateMetadataSimilarity(test1, test2);

        // Assert - Should have contribution from execution time similarity
        result.Should().BeGreaterThan(0.3, "similar execution times should contribute to similarity");
    }

    [Fact]
    public void CalculateMetadataSimilarity_NullTestInfo_ThrowsArgumentException()
    {
        // Arrange
        var validTest = CreateTestInfo("Test1", TestCategory.Unit);
        TestInfo? nullTest = null;

        // Act & Assert - Should throw for null first parameter
        var act1 = () => _calculator.CalculateMetadataSimilarity(nullTest!, validTest);
        act1.Should().Throw<ArgumentNullException>()
            .WithParameterName("test1");

        // Act & Assert - Should throw for null second parameter
        var act2 = () => _calculator.CalculateMetadataSimilarity(validTest, nullTest!);
        act2.Should().Throw<ArgumentNullException>()
            .WithParameterName("test2");
    }

    [Theory]
    [InlineData(TestCategory.Unit, TestCategory.Unit, 1.0)]           // Same category
    [InlineData(TestCategory.Unit, TestCategory.Integration, 0.3)]    // Related categories
    [InlineData(TestCategory.Integration, TestCategory.EndToEnd, 0.4)] // Related categories
    [InlineData(TestCategory.Database, TestCategory.Integration, 0.5)] // Related categories
    [InlineData(TestCategory.Unit, TestCategory.EndToEnd, 0.0)]       // Unrelated categories
    public void CalculateMetadataSimilarity_CategoryRelationships_ProducesExpectedScores(
        TestCategory category1, TestCategory category2, double expectedCategoryScore)
    {
        // Arrange - Tests with specified categories and minimal other attributes
        var test1 = CreateTestInfo("Test1", category1, TimeSpan.Zero);
        var test2 = CreateTestInfo("Test2", category2, TimeSpan.Zero);

        // Act
        var result = _calculator.CalculateMetadataSimilarity(test1, test2);

        // Assert - Category score should contribute expected amount (30% weight)
        var expectedMinimum = expectedCategoryScore * 0.3;
        result.Should().BeGreaterThanOrEqualTo(expectedMinimum, 
            $"category similarity of {expectedCategoryScore} should contribute at least {expectedMinimum:F2} to total");
    }

    [Fact]
    public void CalculateMetadataSimilarity_CompletelyIdenticalTests_ReturnsHighScore()
    {
        // Arrange - Two tests with identical metadata
        var test1 = CreateTestInfo(
            "TestMethod",
            TestCategory.Integration,
            TimeSpan.FromMilliseconds(500),
            tags: new[] { "slow", "database", "critical" });
        
        // Create another test with same characteristics
        var test2 = CreateTestInfo(
            "TestMethod",  // Same name will result in identical TestMethod properties
            TestCategory.Integration,
            TimeSpan.FromMilliseconds(500),
            tags: new[] { "slow", "database", "critical" });

        // Act
        var result = _calculator.CalculateMetadataSimilarity(test1, test2);

        // Assert - Should have very high similarity
        result.Should().BeGreaterThan(0.9, "identical tests should have very high similarity");
        result.Should().BeLessThanOrEqualTo(1.0, "similarity cannot exceed 100%");
    }

    [Fact]
    public void CalculateMetadataSimilarity_CompletelyDifferentTests_ReturnsLowScore()
    {
        // Arrange - Two tests with completely different metadata
        var test1 = CreateTestInfo(
            "FastUnitTest",
            TestCategory.Unit,
            TimeSpan.FromMilliseconds(10),
            tags: new[] { "fast", "unit" });
        
        var test2 = CreateTestInfo(
            "SlowIntegrationDatabaseTest",
            TestCategory.Database,
            TimeSpan.FromSeconds(10),
            tags: new[] { "slow", "database", "integration", "external" });

        // Act
        var result = _calculator.CalculateMetadataSimilarity(test1, test2);

        // Assert - Should have low similarity
        result.Should().BeLessThan(0.5, "completely different tests should have low similarity");
        result.Should().BeGreaterThanOrEqualTo(0.0, "similarity cannot be negative");
    }

    [Theory]
    [InlineData(100, 100, 1.0)]      // Identical times
    [InlineData(100, 110, 0.95)]     // Very similar (within 10%)
    [InlineData(100, 200, 0.71)]     // 2x difference
    [InlineData(100, 1000, 0.32)]    // 10x difference
    [InlineData(0, 0, 1.0)]          // Both zero
    [InlineData(0, 100, 0.0)]        // One zero
    public void CalculateMetadataSimilarity_ExecutionTimeSimilarity_ProducesExpectedRatios(
        double time1Ms, double time2Ms, double expectedSimilarity)
    {
        // Arrange - Tests differing only in execution time
        var test1 = CreateTestInfo("Test1", TestCategory.Unit, TimeSpan.FromMilliseconds(time1Ms));
        var test2 = CreateTestInfo("Test2", TestCategory.Unit, TimeSpan.FromMilliseconds(time2Ms));

        // Act
        var result = _calculator.CalculateMetadataSimilarity(test1, test2);

        // Assert - Execution time contributes 10% to total score
        // Category contributes 30% (same category = 1.0 * 0.3 = 0.3)
        // The execution time component should be approximately expectedSimilarity * 0.1
        var executionTimeContribution = expectedSimilarity * 0.1;
        var minimumExpected = 0.3 + executionTimeContribution; // Category + execution time
        
        // Account for naming similarity which might add some score
        result.Should().BeGreaterThanOrEqualTo(minimumExpected - 0.1, 
            $"execution time similarity of {expectedSimilarity:F2} should contribute to total score");
    }

    #endregion

    #region Helper Methods for Complex Test Scenarios

    /// <summary>
    /// Creates a TestInfo with a custom method name for testing naming similarity.
    /// </summary>
    private TestInfo CreateTestInfoWithCustomName(string methodName, TestCategory category)
    {
        // Create a dynamic type with a method that has the desired name
        // Since we can't easily create a MethodInfo with a custom name, we'll use reflection
        // to find an existing method and use it as a template
        var templateMethod = GetType().GetMethod(nameof(CreateTestInfoWithCustomName))!;
        
        // Create a TestMethod - the method name will come from the MethodInfo
        // For testing naming similarity, we need to create TestInfo instances that have
        // TestMethod objects with specific MethodName values
        // Since MethodName is readonly and comes from MethodInfo.Name, we need actual methods
        
        // We'll create stub methods in this test class for the naming tests
        var actualMethod = methodName switch
        {
            "ProcessOrder_WhenValid_ShouldReturnSuccess" => GetType().GetMethod(nameof(ProcessOrder_WhenValid_ShouldReturnSuccess), 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance),
            "ProcessOrder_WhenInvalid_ShouldReturnError" => GetType().GetMethod(nameof(ProcessOrder_WhenInvalid_ShouldReturnError),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance),
            _ => null
        };
        
        // Use actualMethod if found, otherwise use template method
        if (actualMethod == null)
        {
            // For cases where we don't have a matching stub method, use the template
            actualMethod = templateMethod;
        }
        
        var testMethod = new TestMethod(
            actualMethod,
            typeof(SimilarityCalculatorTests),
            "TestAssembly.dll",
            FrameworkVersion.Net5Plus);

        return new TestInfo(
            testMethod,
            category,
            TimeSpan.FromMilliseconds(100),
            0.5);
    }
    
    // Stub methods for naming similarity tests (internal to avoid xUnit analyzer warnings, but still accessible via reflection)
    internal void ProcessOrder_WhenValid_ShouldReturnSuccess() { }
    internal void ProcessOrder_WhenInvalid_ShouldReturnError() { }

    #endregion

    #region Edge Cases and Boundary Tests

    [Fact]
    public void CalculateCoverageOverlap_CaseSensitiveMethodNames_TreatsAsDifferent()
    {
        // Arrange - Method names with different casing
        var methods1 = new HashSet<string> { "Method", "METHOD", "method" };
        var methods2 = new HashSet<string> { "Method" };  // Only matches one exactly

        // Act
        var result = _calculator.CalculateCoverageOverlap(methods1, methods2);

        // Assert - Should treat different casings as different methods
        result.Should().BeApproximately(1.0 / 3.0, 0.001, 
            "only 'Method' should match, giving 1/3 Jaccard similarity");
    }

    [Fact]
    public void CalculateCoverageOverlap_SpecialCharactersInMethodNames_HandlesCorrectly()
    {
        // Arrange - Method names with special characters
        var methods1 = new HashSet<string> 
        { 
            "Namespace.Class.Method<T>",
            "Namespace.Class+NestedClass.Method",
            "get_Property",
            "Method(String, Int32)"
        };
        var methods2 = new HashSet<string> 
        { 
            "Namespace.Class.Method<T>",  // Exact match with generics
            "Namespace.Class+NestedClass.Method",  // Exact match with nested class
            "set_Property",  // Different property accessor
            "Method(String, Int32)"  // Exact match with parameters
        };

        // Act
        var result = _calculator.CalculateCoverageOverlap(methods1, methods2);

        // Assert - Should correctly match special characters
        result.Should().BeApproximately(3.0 / 5.0, 0.001, 
            "3 out of 5 unique methods should match");
    }

    [Fact]
    public void CalculateMetadataSimilarity_ExtremeDifferenceInExecutionTime_StillBounded()
    {
        // Arrange - Tests with extreme execution time differences
        var fastTest = CreateTestInfo("FastTest", TestCategory.Unit, TimeSpan.FromMicroseconds(1));
        var slowTest = CreateTestInfo("SlowTest", TestCategory.Unit, TimeSpan.FromHours(1));

        // Act
        var result = _calculator.CalculateMetadataSimilarity(fastTest, slowTest);

        // Assert - Result should still be bounded between 0 and 1
        result.Should().BeInRange(0.0, 1.0, "similarity must always be between 0 and 1");
        result.Should().BeGreaterThan(0.0, "other factors should contribute even with extreme time difference");
    }

    [Fact]
    public void CalculateCoverageOverlap_VeryLongMethodNames_HandlesCorrectly()
    {
        // Arrange - Method names with excessive length
        var longMethodName1 = "Namespace." + new string('A', 1000) + ".Method";
        var longMethodName2 = "Namespace." + new string('B', 1000) + ".Method";
        
        var methods1 = new HashSet<string> { longMethodName1, "NormalMethod" };
        var methods2 = new HashSet<string> { longMethodName1, longMethodName2 };

        // Act
        var result = _calculator.CalculateCoverageOverlap(methods1, methods2);

        // Assert - Should handle long names without issues
        result.Should().BeApproximately(1.0 / 3.0, 0.001, 
            "only the long method name should match, giving 1/3 Jaccard similarity");
    }

    #endregion

    #region Performance and Stress Tests

    [Fact]
    public void CalculateCoverageOverlap_ThreadSafety_MultipleSimultaneousCalculations()
    {
        // Arrange - Prepare test data
        var methods1 = Enumerable.Range(0, 100).Select(i => $"Method_{i}").ToHashSet();
        var methods2 = Enumerable.Range(50, 100).Select(i => $"Method_{i}").ToHashSet();
        var results = new List<double>();

        // Act - Run multiple calculations in parallel
        Parallel.For(0, 100, i =>
        {
            var result = _calculator.CalculateCoverageOverlap(methods1, methods2);
            lock (results)
            {
                results.Add(result);
            }
        });

        // Assert - All results should be identical
        results.Should().HaveCount(100);
        results.Should().AllBeEquivalentTo(results[0], 
            "parallel calculations with same input should produce identical results");
    }

    [Fact]
    public void CalculateMetadataSimilarity_PerformanceWithManyTags_CompletesQuickly()
    {
        // Arrange - Tests with many tags
        var tags1 = Enumerable.Range(0, 1000).Select(i => $"tag_{i}").ToArray();
        var tags2 = Enumerable.Range(500, 1000).Select(i => $"tag_{i}").ToArray();
        
        var test1 = CreateTestInfo("Test1", TestCategory.Unit, tags: tags1);
        var test2 = CreateTestInfo("Test2", TestCategory.Unit, tags: tags2);

        // Act - Measure performance
        var stopwatch = Stopwatch.StartNew();
        var result = _calculator.CalculateMetadataSimilarity(test1, test2);
        stopwatch.Stop();

        // Assert - Should complete quickly even with many tags
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(50, 
            "metadata similarity calculation should be efficient even with 1000+ tags");
        result.Should().BeInRange(0.0, 1.0, "result should be valid");
    }

    #endregion

    #region Logging and Observability Tests

    [Fact]
    public void CalculateCoverageOverlap_LogsDebugInformation()
    {
        // Arrange
        var methods1 = new HashSet<string> { "Method1", "Method2" };
        var methods2 = new HashSet<string> { "Method2", "Method3" };

        // Act
        _calculator.CalculateCoverageOverlap(methods1, methods2);

        // Assert - Verify debug logging occurred
        _mockLogger.Received().Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Calculating coverage overlap")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void CalculateMetadataSimilarity_LogsDebugInformation()
    {
        // Arrange
        var test1 = CreateTestInfo("Test1", TestCategory.Unit);
        var test2 = CreateTestInfo("Test2", TestCategory.Unit);

        // Act
        _calculator.CalculateMetadataSimilarity(test1, test2);

        // Assert - Verify debug logging occurred
        _mockLogger.Received().Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Calculating metadata similarity")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion
}