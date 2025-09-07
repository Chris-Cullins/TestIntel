using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Core.Models;
using TestIntelligence.ImpactAnalyzer.Analysis.Workspace;

namespace TestIntelligence.ImpactAnalyzer.Analysis.CallGraph
{
    public interface ICallGraphAnalyzer
    {
        Task<MethodCallGraph> BuildCallGraphAsync(string[] solutionFiles, CancellationToken cancellationToken = default);
        Task<MethodCallGraph> BuildCallGraphFromFilesAsync(string[] files, CancellationToken cancellationToken = default);
        Task<MethodCallGraph> BuildCallGraphWithSharedCompilationAsync(string[] sourceFiles, CancellationToken cancellationToken = default);
    }
}