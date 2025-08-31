using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public interface ICompilationManager
    {
        Task<IReadOnlyDictionary<ProjectId, Compilation>> BuildSolutionCompilationsAsync(CancellationToken cancellationToken = default);
        Compilation? GetCompilationForProject(string projectPath);
        Compilation? GetCompilationForFile(string filePath);
        SemanticModel? GetSemanticModel(string filePath);
        ISymbol? ResolveSymbolInfo(SyntaxNode node, string filePath);
        ITypeSymbol? ResolveTypeInfo(SyntaxNode node, string filePath);
        Project? GetProjectForFile(string filePath);
        IReadOnlyList<Project> GetAllProjects();
        IReadOnlyList<string> GetAllSourceFiles();
        Task<Document?> GetDocumentAsync(string filePath, CancellationToken cancellationToken = default);
        Task<SyntaxTree?> GetSyntaxTreeAsync(string filePath, CancellationToken cancellationToken = default);
        void ClearSemanticModelCache();
        int GetCacheSize();
    }
}