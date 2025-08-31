using Microsoft.Extensions.Logging;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Services;

// Clear, focused test to show exactly what methods we have and their coverage
var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddConsole().SetMinimumLevel(LogLevel.Information));

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
    int count = 0;
    foreach (var methodId in coverageMap.MethodToTests.Keys.Take(10))
    {
        var tests = coverageMap.GetTestsForMethod(methodId);
        Console.WriteLine($"   {++count}. {methodId}");
        Console.WriteLine($"      -> {tests.Count} tests cover this method");
        
        foreach (var test in tests.Take(3))
        {
            Console.WriteLine($"         🧪 {test.TestMethodName} ({test.TestType}, {test.Confidence:F2})");
        }
        if (tests.Count > 3)
        {
            Console.WriteLine($"         ... and {tests.Count - 3} more tests");
        }
        Console.WriteLine();
    }
    
    if (coverageMap.CoveredMethodCount == 0)
    {
        Console.WriteLine("   ❌ No methods found with test coverage");
        
        // Let's see what test methods were found
        var stats = await coverageAnalyzer.GetCoverageStatisticsAsync(solutionPath);
        Console.WriteLine($"   📋 But we found {stats.TotalTests} total test methods in the solution");
        
        // Get some sample methods to test manually
        var sourceFiles = Directory.GetFiles("/Users/chriscullins/src/TestIntel/src", "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("bin") && !f.Contains("obj"))
            .Take(10)
            .ToArray();
            
        var callGraph = await roslynAnalyzer.BuildCallGraphAsync(sourceFiles, CancellationToken.None);
        var allMethods = callGraph.GetAllMethods().Take(5).ToList();
        
        Console.WriteLine($"   🔍 Testing 5 random methods:");
        foreach (var methodId in allMethods)
        {
            var methodInfo = callGraph.GetMethodInfo(methodId);
            Console.WriteLine($"      {methodId} -> {methodInfo?.Name}");
            
            var coverage = await coverageAnalyzer.FindTestsExercisingMethodAsync(methodId, solutionPath);
            Console.WriteLine($"         Coverage: {coverage.Count} tests");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error: {ex.Message}");
    Console.WriteLine($"Stack: {ex.StackTrace}");
}
finally
{
    loggerFactory.Dispose();
}