namespace TestIntelligence.E2E.Tests.Models;

public class FindTestsJsonOutput
{
    public List<TestCoverageInfo> Tests { get; set; } = new();
    public string TargetMethod { get; set; } = string.Empty;
    public DateTime AnalysisDate { get; set; }
    public int TotalTests { get; set; }
}

public class TestCoverageInfo
{
    public string TestClassName { get; set; } = string.Empty;
    public string TestMethodName { get; set; } = string.Empty;
    public string TestAssembly { get; set; } = string.Empty;
    public string TestType { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public int CallDepth { get; set; }
    public List<string> CallPath { get; set; } = new();
}

public class AnalyzeJsonOutput
{
    public AnalysisSummary Summary { get; set; } = new();
    public List<TestAssemblyInfo> TestAssemblies { get; set; } = new();
    public DateTime AnalysisDate { get; set; }
}

public class AnalysisSummary
{
    public int TotalTestMethods { get; set; }
    public int TotalTestFixtures { get; set; }
    public int TotalAssemblies { get; set; }
}

public class TestAssemblyInfo
{
    public string AssemblyName { get; set; } = string.Empty;
    public string AssemblyPath { get; set; } = string.Empty;
    public int TestMethodCount { get; set; }
    public int TestFixtureCount { get; set; }
    public string TargetFramework { get; set; } = string.Empty;
}

public class CallGraphJsonOutput
{
    public CallGraphSummary Summary { get; set; } = new();
    public List<MethodCallInfo> Methods { get; set; } = new();
    public DateTime AnalysisDate { get; set; }
}

public class CallGraphSummary
{
    public int TotalMethods { get; set; }
    public int TotalCallRelationships { get; set; }
    public int MaxCallDepth { get; set; }
}

public class MethodCallInfo
{
    public string MethodName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Assembly { get; set; } = string.Empty;
    public List<string> CalledMethods { get; set; } = new();
    public List<string> CallingMethods { get; set; } = new();
}