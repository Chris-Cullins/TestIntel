using Microsoft.Extensions.Logging;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Services;

// Clear, focused test to show exactly what methods we have and their coverage
var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddConsole().SetMinimumLevel(LogLevel.Warning)); // Less verbose

var roslynLogger = loggerFactory.CreateLogger<RoslynAnalyzer>();
var coverageLogger = loggerFactory.CreateLogger<TestCoverageAnalyzer>();

var roslynAnalyzer = new RoslynAnalyzer(roslynLogger);
var coverageAnalyzer = new TestCoverageAnalyzer(roslynAnalyzer, coverageLogger);

var solutionPath = "/Users/chriscullins/src/TestIntel/TestIntelligence.sln";

Console.WriteLine("🎯 FOCUSED REVERSE LOOKUP TEST");
Console.WriteLine("==============================");

try 
{
    // Build coverage map once for efficiency
    Console.WriteLine("🏗️ Building coverage map...");
    var coverageMap = await coverageAnalyzer.BuildTestCoverageMapAsync(solutionPath, CancellationToken.None);
    
    Console.WriteLine($"📊 Coverage Map Statistics:");
    Console.WriteLine($"   Methods with Coverage: {coverageMap.CoveredMethodCount}");
    Console.WriteLine($"   Total Coverage Relationships: {coverageMap.TotalCoverageRelationships}");
    Console.WriteLine();

    // Show which methods actually have coverage
    Console.WriteLine("🔍 METHODS WITH TEST COVERAGE:");
    if (coverageMap.CoveredMethodCount > 0)
    {
        int count = 0;
        foreach (var methodId in coverageMap.MethodToTests.Keys.Take(10))
        {
            var tests = coverageMap.GetTestsForMethod(methodId);
            Console.WriteLine($"   {++count}. Method: {methodId}");
            Console.WriteLine($"      Tests covering it: {tests.Count}");
            
            foreach (var test in tests.Take(3))
            {
                Console.WriteLine($"         🧪 {test.TestMethodName} ({test.TestType}, confidence: {test.Confidence:F2})");
                Console.WriteLine($"            Call path: {string.Join(" -> ", test.CallPath.Take(3))}...");
            }
            if (tests.Count > 3)
            {
                Console.WriteLine($"         ... and {tests.Count - 3} more tests");
            }
            Console.WriteLine();
        }
    }
    else
    {
        Console.WriteLine("   ❌ No methods found with test coverage");
        
        // Debug: Let's see what test methods were found
        var stats = await coverageAnalyzer.GetCoverageStatisticsAsync(solutionPath);
        Console.WriteLine($"   📋 But we found {stats.TotalTests} total test methods in the solution");
        Console.WriteLine($"   📊 Total methods analyzed: {stats.TotalMethods}");
        Console.WriteLine();
        
        // Test 3 specific methods from our test projects
        var testMethods = new[] {
            "TestIntelligence.Core.Tests.Models.TestMethodTests.GetUniqueId_ReturnsExpectedFormat",
            "TestIntelligence.ImpactAnalyzer.Tests.Analysis.RoslynAnalyzerTests.BuildCallGraph_ValidCode_ReturnsCallGraph", 
            "TestIntelligence.DataTracker.Tests.Analysis.DataPatternAnalyzerTests.AnalyzeMethod_WithEfCoreContext_DetectsEfCoreUsage"
        };
        
        Console.WriteLine("🎯 TESTING SPECIFIC METHODS:");
        foreach (var methodId in testMethods)
        {
            Console.WriteLine($"   Testing: {methodId}");
            var coverage = await coverageAnalyzer.FindTestsExercisingMethodAsync(methodId, solutionPath);
            Console.WriteLine($"   Coverage: {coverage.Count} tests found");
            
            if (coverage.Count > 0)
            {
                foreach (var test in coverage.Take(2))
                {
                    Console.WriteLine($"      🧪 {test.TestMethodName} ({test.TestType})");
                }
            }
            Console.WriteLine();
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error: {ex.Message}");
}
finally
{
    loggerFactory.Dispose();
}