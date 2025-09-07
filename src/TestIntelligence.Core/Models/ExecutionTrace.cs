using System;
using System.Collections.Generic;
using System.Linq;

namespace TestIntelligence.Core.Models;

public class ExecutionTrace
{
    public ExecutionTrace(string testMethodId, string testMethodName, string testClassName)
    {
        TestMethodId = testMethodId ?? throw new ArgumentNullException(nameof(testMethodId));
        TestMethodName = testMethodName ?? throw new ArgumentNullException(nameof(testMethodName));
        TestClassName = testClassName ?? throw new ArgumentNullException(nameof(testClassName));
        TraceTimestamp = DateTime.UtcNow;
    }

    public string TestMethodId { get; }
    public string TestMethodName { get; }
    public string TestClassName { get; }
    public List<ExecutedMethod> ExecutedMethods { get; init; } = new();
    public int TotalMethodsCalled { get; set; }
    public int ProductionMethodsCalled { get; set; }
    public TimeSpan EstimatedExecutionComplexity { get; set; }
    public DateTime TraceTimestamp { get; init; }

    /// <summary>
    /// Gets the strongly-typed method identifier for the test method.
    /// </summary>
    public MethodId MethodId => new(TestMethodId);

    /// <summary>
    /// Recalculates method counts based on current ExecutedMethods collection.
    /// </summary>
    public void RefreshCounts()
    {
        TotalMethodsCalled = ExecutedMethods.Count;
        ProductionMethodsCalled = ExecutedMethods.Count(m => m.IsProductionCode);
    }
}

public class ExecutedMethod
{
    public ExecutedMethod(string methodId, string methodName, string containingType, bool isProductionCode)
    {
        MethodId = methodId ?? throw new ArgumentNullException(nameof(methodId));
        MethodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
        ContainingType = containingType ?? throw new ArgumentNullException(nameof(containingType));
        IsProductionCode = isProductionCode;
    }

    public string MethodId { get; }
    public string MethodName { get; }
    public string ContainingType { get; }
    public string FilePath { get; init; } = string.Empty;
    public int LineNumber { get; init; }
    public string[] CallPath { get; init; } = Array.Empty<string>();
    public int CallDepth { get; init; }
    public bool IsProductionCode { get; }
    public MethodCategory Category { get; set; }

    /// <summary>
    /// Gets the strongly-typed method identifier.
    /// </summary>
    public MethodId StrongMethodId => new(MethodId);
}

public class ExecutionCoverageReport
{
    public ExecutionCoverageReport()
    {
        GeneratedTimestamp = DateTime.UtcNow;
    }

    public Dictionary<string, ExecutionTrace> TestToExecutionMap { get; init; } = new();
    public List<string> UncoveredMethods { get; init; } = new();
    public CoverageStatistics Statistics { get; set; } = new();
    public DateTime GeneratedTimestamp { get; init; }

    /// <summary>
    /// Gets all covered methods across all execution traces.
    /// </summary>
    public IEnumerable<string> GetAllCoveredMethods()
    {
        return TestToExecutionMap.Values
            .SelectMany(trace => trace.ExecutedMethods)
            .Select(method => method.MethodId)
            .Distinct();
    }

    /// <summary>
    /// Gets execution traces for methods matching the specified pattern.
    /// </summary>
    public IEnumerable<ExecutionTrace> GetTracesForMethodPattern(string methodPattern)
    {
        return TestToExecutionMap.Values
            .Where(trace => trace.MethodId.Matches(methodPattern));
    }
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