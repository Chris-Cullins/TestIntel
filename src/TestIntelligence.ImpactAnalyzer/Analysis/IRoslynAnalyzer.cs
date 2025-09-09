using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using TestIntelligence.Core.Models;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public interface IRoslynAnalyzer
    {
        Task<MethodCallGraph> BuildCallGraphAsync(string[] solutionFiles, CancellationToken cancellationToken = default);
        Task<MethodCallGraph> BuildCallGraphAsync(AnalysisScope scope, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<string>> GetAffectedMethodsAsync(string[] changedFiles, string[] changedMethods, CancellationToken cancellationToken = default);
        Task<SemanticModel> GetSemanticModelAsync(string filePath, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<TypeUsageInfo>> AnalyzeTypeUsageAsync(string[] sourceFiles, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<MethodInfo>> ExtractMethodsFromFileAsync(string filePath, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<TestCoverageResult>> FindTestsExercisingMethodAsync(string methodId, string[] solutionFiles, CancellationToken cancellationToken = default);
    }
}
