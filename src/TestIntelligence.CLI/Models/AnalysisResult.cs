using TestIntelligence.SelectionEngine.Models;

namespace TestIntelligence.CLI.Models;

/// <summary>
/// Result of analyzing test assemblies.
/// </summary>
public class AnalysisResult
{
    public string AnalyzedPath { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public List<AssemblyAnalysis> Assemblies { get; set; } = new();
    public AnalysisSummary? Summary { get; set; }
}

/// <summary>
/// Analysis of a single assembly.
/// </summary>
public class AssemblyAnalysis
{
    public string AssemblyPath { get; set; } = string.Empty;
    public string? Framework { get; set; }
    public string? Error { get; set; }
    public List<TestMethodAnalysis> TestMethods { get; set; } = new();
}

/// <summary>
/// Analysis of a single test method.
/// </summary>
public class TestMethodAnalysis
{
    public string MethodName { get; set; } = string.Empty;
    public TestCategory Category { get; set; }
    public TimeSpan EstimatedDuration { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
}

/// <summary>
/// Summary statistics of the analysis.
/// </summary>
public class AnalysisSummary
{
    public int TotalAssemblies { get; set; }
    public int TotalTestMethods { get; set; }
    public int SuccessfullyAnalyzed { get; set; }
    public int FailedAnalyses { get; set; }
    public Dictionary<TestCategory, int> CategoryBreakdown { get; set; } = new();
}

/// <summary>
/// Result of categorizing tests.
/// </summary>
public class CategorizationResult
{
    public string AnalyzedPath { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public Dictionary<TestCategory, List<string>> Categories { get; set; } = new();
    public int TotalTests { get; set; }
}

/// <summary>
/// Result of test selection.
/// </summary>
public class SelectionResult
{
    public string AnalyzedPath { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string ConfidenceLevel { get; set; } = string.Empty;
    public string[] ChangedFiles { get; set; } = Array.Empty<string>();
    public List<SelectedTest> SelectedTests { get; set; } = new();
    public SelectionSummary? Summary { get; set; }
}

/// <summary>
/// A selected test with its metadata.
/// </summary>
public class SelectedTest
{
    public string TestName { get; set; } = string.Empty;
    public TestCategory Category { get; set; }
    public double SelectionScore { get; set; }
    public TimeSpan EstimatedDuration { get; set; }
    public string Assembly { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// Summary of test selection.
/// </summary>
public class SelectionSummary
{
    public int TotalSelectedTests { get; set; }
    public TimeSpan EstimatedTotalDuration { get; set; }
    public double AverageSelectionScore { get; set; }
    public Dictionary<TestCategory, int> CategoryBreakdown { get; set; } = new();
    public int OptimalParallelism { get; set; }
}