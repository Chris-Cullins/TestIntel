using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Core.Models;

namespace TestIntelligence.Core.Interfaces;

public interface ITestExecutionTracer
{
    Task<ExecutionTrace> TraceTestExecutionAsync(
        string testMethodId, 
        string solutionPath, 
        CancellationToken cancellationToken = default);
        
    Task<IReadOnlyList<ExecutionTrace>> TraceMultipleTestsAsync(
        IEnumerable<string> testMethodIds, 
        string solutionPath, 
        CancellationToken cancellationToken = default);
        
    Task<ExecutionCoverageReport> GenerateCoverageReportAsync(
        string solutionPath,
        CancellationToken cancellationToken = default);
}