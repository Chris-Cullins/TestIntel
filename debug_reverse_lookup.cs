using Microsoft.Extensions.Logging;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Services;

// Quick diagnostic script to understand the issue
var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddConsole().SetMinimumLevel(LogLevel.Information));

var roslynLogger = loggerFactory.CreateLogger<RoslynAnalyzer>();
var coverageLogger = loggerFactory.CreateLogger<TestCoverageAnalyzer>();

var roslynAnalyzer = new RoslynAnalyzer(roslynLogger);
var coverageAnalyzer = new TestCoverageAnalyzer(roslynAnalyzer, coverageLogger);

var solutionPath = "/Users/chriscullins/src/TestIntel/TestIntelligence.sln";

Console.WriteLine("🔍 Diagnosing Reverse Lookup Issues");
Console.WriteLine("===================================");

try 
{
    // Get a few source files to check what we're actually parsing
    var sourceFiles = Directory.GetFiles("/Users/chriscullins/src/TestIntel/src", "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains("bin") && !f.Contains("obj"))
        .Take(5)
        .ToArray();
    
    Console.WriteLine($"📂 Found {sourceFiles.Length} sample source files:");
    foreach(var file in sourceFiles)
    {
        Console.WriteLine($"   {Path.GetFileName(file)}");
    }
    Console.WriteLine();

    // Build call graph to see what methods we're finding
    Console.WriteLine("🏗️ Building call graph...");
    var callGraph = await roslynAnalyzer.BuildCallGraphAsync(sourceFiles, CancellationToken.None);
    
    var allMethods = callGraph.GetAllMethods().Take(10).ToList();
    Console.WriteLine($"🔍 Found {allMethods.Count} sample methods in call graph:");
    foreach(var methodId in allMethods)
    {
        var methodInfo = callGraph.GetMethodInfo(methodId);
        Console.WriteLine($"   {methodId} -> {methodInfo?.Name} in {Path.GetFileName(methodInfo?.FilePath ?? "unknown")}");
    }
    Console.WriteLine();

    // Test with one specific method that should exist
    var targetMethod = allMethods.FirstOrDefault(m => m.Contains("GetUniqueId"));
    if (targetMethod != null)
    {
        Console.WriteLine($"🎯 Testing coverage for: {targetMethod}");
        var coverage = await coverageAnalyzer.FindTestsExercisingMethodAsync(targetMethod, solutionPath);
        Console.WriteLine($"   Found {coverage.Count} tests");
        
        if (coverage.Count > 0)
        {
            foreach(var test in coverage.Take(3))
            {
                Console.WriteLine($"   - {test.TestMethodName} ({test.TestType})");
            }
        }
    }
    else 
    {
        Console.WriteLine("❌ No GetUniqueId method found in call graph");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error: {ex.Message}");
    Console.WriteLine($"Stack: {ex.StackTrace}");
}

loggerFactory.Dispose();