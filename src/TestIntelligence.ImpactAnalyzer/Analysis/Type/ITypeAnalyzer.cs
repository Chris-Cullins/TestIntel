using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Core.Models;

namespace TestIntelligence.ImpactAnalyzer.Analysis.Type
{
    public interface ITypeAnalyzer
    {
        Task<IReadOnlyList<TypeUsageInfo>> AnalyzeTypeUsageAsync(string[] sourceFiles, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<TypeUsageInfo>> AnalyzeTypeUsageInFileAsync(string filePath, CancellationToken cancellationToken = default);
    }
}