using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.ImpactAnalyzer.Analysis;

namespace TestIntelligence.CLI.Services;

/// <summary>
/// Service for analyzing code structure and generating method call graph reports.
/// </summary>
public class CallGraphService : ICallGraphService
{
    private readonly IRoslynAnalyzer _roslynAnalyzer;
    private readonly IOutputFormatter _outputFormatter;
    private readonly ILogger<CallGraphService> _logger;

    public CallGraphService(
        IRoslynAnalyzer roslynAnalyzer,
        IOutputFormatter outputFormatter,
        ILogger<CallGraphService> logger)
    {
        _roslynAnalyzer = roslynAnalyzer ?? throw new ArgumentNullException(nameof(roslynAnalyzer));
        _outputFormatter = outputFormatter ?? throw new ArgumentNullException(nameof(outputFormatter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task AnalyzeCallGraphAsync(string path, string? outputPath, string format, bool verbose, int? maxMethods)
    {
        try
        {
            _logger.LogInformation("Starting method call graph analysis for: {Path}", path);

            // Get all C# source files from the specified path
            var sourceFiles = GetSourceFiles(path);
            
            if (!sourceFiles.Any())
            {
                _logger.LogWarning("No C# source files found in path: {Path}", path);
                Console.WriteLine("No C# source files found in the specified path.");
                return;
            }

            _logger.LogInformation("Found {FileCount} source files to analyze", sourceFiles.Length);

            // Build the method call graph
            var callGraph = await _roslynAnalyzer.BuildCallGraphAsync(sourceFiles);
            
            // Generate the report
            var report = GenerateCallGraphReport(callGraph, verbose, maxMethods ?? 50);
            
            // Output the report
            await _outputFormatter.WriteOutputAsync(report, format, outputPath);
            
            _logger.LogInformation("Method call graph analysis completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during method call graph analysis");
            Console.WriteLine($"Error during analysis: {ex.Message}");
            if (verbose)
            {
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }

    private string[] GetSourceFiles(string path)
    {
        if (File.Exists(path))
        {
            // Single file
            return path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? new[] { path } : Array.Empty<string>();
        }
        
        if (Directory.Exists(path))
        {
            // Directory - get all C# files recursively
            return Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !f.Contains("\\obj\\") && !f.Contains("\\bin\\"))
                .ToArray();
        }
        
        throw new FileNotFoundException($"Path not found: {path}");
    }

    private CallGraphReport GenerateCallGraphReport(MethodCallGraph callGraph, bool verbose, int maxMethods)
    {
        var allMethods = callGraph.GetAllMethods().OrderBy(m => m).ToList();
        
        // Calculate statistics
        var methodsWithMostCalls = allMethods
            .Select(m => new MethodCallInfo
            {
                MethodId = m,
                MethodInfo = callGraph.GetMethodInfo(m),
                CallCount = callGraph.GetMethodCalls(m).Count,
                DependentCount = callGraph.GetMethodDependents(m).Count
            })
            .OrderByDescending(x => x.CallCount)
            .Take(10)
            .ToList();

        var mostCalledMethods = allMethods
            .Select(m => new MethodCallInfo
            {
                MethodId = m,
                MethodInfo = callGraph.GetMethodInfo(m),
                CallCount = callGraph.GetMethodCalls(m).Count,
                DependentCount = callGraph.GetMethodDependents(m).Count
            })
            .OrderByDescending(x => x.DependentCount)
            .Take(10)
            .ToList();

        // Method details for verbose output or limited output
        var methodDetails = allMethods
            .Take(maxMethods)
            .Select(methodId =>
            {
                var methodInfo = callGraph.GetMethodInfo(methodId);
                var calls = callGraph.GetMethodCalls(methodId);
                var dependents = callGraph.GetMethodDependents(methodId);

                return new MethodDetail
                {
                    MethodId = methodId,
                    MethodInfo = methodInfo,
                    CallCount = calls.Count,
                    DependentCount = dependents.Count,
                    Calls = verbose ? calls.Take(10).Select(c => callGraph.GetMethodInfo(c)).Where(c => c != null).Cast<MethodInfo>().ToList() : new(),
                    Dependents = verbose ? dependents.Take(5).Select(d => callGraph.GetMethodInfo(d)).Where(d => d != null).Cast<MethodInfo>().ToList() : new()
                };
            })
            .ToList();

        return new CallGraphReport
        {
            TotalMethods = allMethods.Count,
            TotalSourceFiles = GetUniqueFileCount(allMethods.Select(m => callGraph.GetMethodInfo(m)?.FilePath).Where(f => f != null)!),
            MethodsWithMostCalls = methodsWithMostCalls,
            MostCalledMethods = mostCalledMethods,
            MethodDetails = methodDetails,
            ShowingMethodCount = Math.Min(maxMethods, allMethods.Count),
            IsVerbose = verbose
        };
    }

    private int GetUniqueFileCount(IEnumerable<string> filePaths)
    {
        return filePaths.Distinct().Count();
    }
}

/// <summary>
/// Report model for method call graph analysis.
/// </summary>
public class CallGraphReport
{
    public int TotalMethods { get; set; }
    public int TotalSourceFiles { get; set; }
    public int ShowingMethodCount { get; set; }
    public bool IsVerbose { get; set; }
    public List<MethodCallInfo> MethodsWithMostCalls { get; set; } = new();
    public List<MethodCallInfo> MostCalledMethods { get; set; } = new();
    public List<MethodDetail> MethodDetails { get; set; } = new();
}

/// <summary>
/// Information about a method's call statistics.
/// </summary>
public class MethodCallInfo
{
    public string MethodId { get; set; } = string.Empty;
    public MethodInfo? MethodInfo { get; set; }
    public int CallCount { get; set; }
    public int DependentCount { get; set; }
}

/// <summary>
/// Detailed information about a method including its calls and dependents.
/// </summary>
public class MethodDetail
{
    public string MethodId { get; set; } = string.Empty;
    public MethodInfo? MethodInfo { get; set; }
    public int CallCount { get; set; }
    public int DependentCount { get; set; }
    public List<MethodInfo> Calls { get; set; } = new();
    public List<MethodInfo> Dependents { get; set; } = new();
}