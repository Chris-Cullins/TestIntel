using Microsoft.Extensions.Logging;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Services;

// Quick diagnostic script to understand the reverse lookup issue
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
        .Take(10)
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
    
    var allMethods = callGraph.GetAllMethods().ToList();
    Console.WriteLine($"🔍 Found {allMethods.Count} total methods in call graph");
    
    // Show sample methods
    var sampleMethods = allMethods.Take(10).ToList();
    Console.WriteLine($"Sample methods:");
    foreach(var methodId in sampleMethods)
    {
        var methodInfo = callGraph.GetMethodInfo(methodId);
        Console.WriteLine($"   {methodId}");
        Console.WriteLine($"      -> Name: {methodInfo?.Name}");
        Console.WriteLine($"      -> File: {Path.GetFileName(methodInfo?.FilePath ?? "unknown")}");
    }
    Console.WriteLine();

    // Look specifically for GetUniqueId methods
    var getUniqueIdMethods = allMethods.Where(m => m.Contains("GetUniqueId")).ToList();
    Console.WriteLine($"🎯 Found {getUniqueIdMethods.Count} GetUniqueId methods:");
    foreach(var method in getUniqueIdMethods)
    {
        Console.WriteLine($"   {method}");
    }
    Console.WriteLine();

    // Test with a method that should definitely exist
    var testMethodFiles = sourceFiles.Where(f => f.Contains("TestMethod.cs")).ToArray();
    Console.WriteLine($"📋 TestMethod.cs files: {testMethodFiles.Length}");
    foreach(var file in testMethodFiles)
    {
        Console.WriteLine($"   {file}");
    }
    
    if (testMethodFiles.Any())
    {
        // Try to find any method from TestMethod class specifically
        var testMethodClassMethods = allMethods.Where(m => m.Contains("TestMethod")).ToList();
        Console.WriteLine($"🔍 Methods from TestMethod class: {testMethodClassMethods.Count}");
        foreach(var method in testMethodClassMethods.Take(5))
        {
            Console.WriteLine($"   {method}");
        }
    }
    
    Console.WriteLine();
    
    // Test with the first available method
    if (allMethods.Any())
    {
        var firstMethod = allMethods.First();
        Console.WriteLine($"🎯 Testing coverage for first method: {firstMethod}");
        var coverage = await coverageAnalyzer.FindTestsExercisingMethodAsync(firstMethod, solutionPath);
        Console.WriteLine($"   Found {coverage.Count} tests");
        
        if (coverage.Count > 0)
        {
            foreach(var test in coverage.Take(3))
            {
                Console.WriteLine($"   - {test.TestMethodName} ({test.TestType})");
            }
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
