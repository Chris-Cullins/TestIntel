using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using TestIntelligence.ImpactAnalyzer.Analysis.Utilities;

namespace TestIntelligence.ImpactAnalyzer.Analysis.Workspace
{
    /// <summary>
    /// Lightweight workspace manager that avoids MSBuild dependencies.
    /// Used for netstandard2.1 target to eliminate NU1701 warnings while
    /// providing basic semantic model and syntax tree capabilities.
    /// </summary>
    public class BasicWorkspaceManager : IWorkspaceManager
    {
        public bool IsInitialized { get; private set; }
        public ICompilationManager? CompilationManager => null;
        public SymbolResolutionEngine? SymbolResolver => null;

        public Task InitializeAsync(string solutionPath, CancellationToken cancellationToken = default)
        {
            // No-op initialization for netstandard fallback mode
            IsInitialized = true;
            return Task.CompletedTask;
        }

        public async Task<SemanticModel?> GetSemanticModelAsync(string filePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(filePath)) return null;

            var sourceCode = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                assemblyName: Path.GetFileNameWithoutExtension(filePath),
                syntaxTrees: new[] { syntaxTree },
                references: RoslynAnalyzerHelper.GetBasicReferences()
            );

            return compilation.GetSemanticModel(syntaxTree);
        }

        public async Task<Microsoft.CodeAnalysis.SyntaxTree?> GetSyntaxTreeAsync(string filePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(filePath)) return null;

            var sourceCode = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            return Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
        }

        public void Dispose()
        {
        }
    }
}
