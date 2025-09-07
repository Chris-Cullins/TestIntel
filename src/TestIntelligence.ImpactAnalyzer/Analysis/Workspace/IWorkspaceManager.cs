using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace TestIntelligence.ImpactAnalyzer.Analysis.Workspace
{
    public interface IWorkspaceManager : IDisposable
    {
        Task InitializeAsync(string solutionPath, CancellationToken cancellationToken = default);
        Task<SemanticModel?> GetSemanticModelAsync(string filePath, CancellationToken cancellationToken = default);
        Task<Microsoft.CodeAnalysis.SyntaxTree?> GetSyntaxTreeAsync(string filePath, CancellationToken cancellationToken = default);
        bool IsInitialized { get; }
        ICompilationManager? CompilationManager { get; }
        SymbolResolutionEngine? SymbolResolver { get; }
    }
}