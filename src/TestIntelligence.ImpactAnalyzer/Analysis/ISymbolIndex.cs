using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public interface ISymbolIndex
    {
        Task BuildIndexAsync(string solutionPath, CancellationToken cancellationToken = default);
        Task<List<string>> FindFilesContainingMethodAsync(string methodId, CancellationToken cancellationToken = default);
        Task<List<string>> FindFilesContainingTypeAsync(string typeName, CancellationToken cancellationToken = default);
        Task<List<string>> GetFilesInNamespaceAsync(string namespaceName, CancellationToken cancellationToken = default);
        SymbolIndex.ProjectInfo? GetProjectForFile(string filePath);
        Task<List<SymbolIndex.ProjectInfo>> FindProjectsContainingMethodAsync(string methodId, CancellationToken cancellationToken = default);
        Task RefreshFilesAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default);
        void Clear();
    }
}