using System;
using System.Collections.Generic;

namespace TestIntelligence.Core.Models;

public class ExecutionTrace
{
    public string TestMethodId { get; set; } = string.Empty;
    public string TestMethodName { get; set; } = string.Empty;
    public string TestClassName { get; set; } = string.Empty;
    public List<ExecutedMethod> ExecutedMethods { get; set; } = new();
    public int TotalMethodsCalled { get; set; }
    public int ProductionMethodsCalled { get; set; }
    public TimeSpan EstimatedExecutionComplexity { get; set; }
    public DateTime TraceTimestamp { get; set; }
}

public class ExecutedMethod
{
    public string MethodId { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public string ContainingType { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string[] CallPath { get; set; } = Array.Empty<string>();
    public int CallDepth { get; set; }
    public bool IsProductionCode { get; set; }
    public MethodCategory Category { get; set; }
}

public class ExecutionCoverageReport
{
    public Dictionary<string, ExecutionTrace> TestToExecutionMap { get; set; } = new();
    public List<string> UncoveredMethods { get; set; } = new();
    public CoverageStatistics Statistics { get; set; } = new();
    public DateTime GeneratedTimestamp { get; set; }
}

public class CoverageStatistics
{
    public int TotalProductionMethods { get; set; }
    public int CoveredProductionMethods { get; set; }
    public int TotalTestMethods { get; set; }
    public double CoveragePercentage => TotalProductionMethods > 0 
        ? (double)CoveredProductionMethods / TotalProductionMethods * 100 
        : 0;
    public int AverageCallDepth { get; set; }
    public int MaxCallDepth { get; set; }
    public Dictionary<MethodCategory, int> CategoryBreakdown { get; set; } = new();
}

public enum MethodCategory
{
    Unknown,
    BusinessLogic,
    DataAccess,
    Infrastructure,
    Framework,
    ThirdParty,
    TestUtility
}