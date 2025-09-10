using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using TestIntelligence.ImpactAnalyzer.Analysis.Utilities;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    /// <summary>
    /// NetStandard-friendly Roslyn analyzer implementation that avoids MSBuildWorkspace.
    /// Provides fallback analysis based on individual files and basic compilation.
    /// </summary>
    public class RoslynAnalyzer : IRoslynAnalyzer
    {
        private readonly ILogger<RoslynAnalyzer> _logger;

        public RoslynAnalyzer(ILogger<RoslynAnalyzer> logger, ILoggerFactory _)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<MethodCallGraph> BuildCallGraphAsync(string[] solutionFiles, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("[NetStandard] Building call graph from files (MSBuild-free path): {Count}", solutionFiles.Length);

            // In netstandard mode, we can't open a .sln with MSBuildWorkspace.
            // Treat inputs as file paths (assemblies or source files).
            return BuildCallGraphFromFilesAsync(solutionFiles, cancellationToken);
        }

        public Task<MethodCallGraph> BuildCallGraphAsync(AnalysisScope scope, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (scope == null) throw new ArgumentNullException(nameof(scope));

            var files = new List<string>();
            if (scope.ChangedFiles != null) files.AddRange(scope.ChangedFiles);
            if (!string.IsNullOrWhiteSpace(scope.SolutionPath) && File.Exists(scope.SolutionPath))
            {
                // Best-effort: include the path if it's a file; otherwise ignore
                files.Add(scope.SolutionPath);
            }

            return BuildCallGraphFromFilesAsync(files.ToArray(), cancellationToken);
        }

        public async Task<IReadOnlyList<string>> GetAffectedMethodsAsync(string[] changedFiles, string[] changedMethods, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Minimal conservative behavior: changed methods are affected.
            // A richer implementation would parse files and track usages.
            _logger.LogInformation("[NetStandard] Returning changed methods as affected (Count={Count})", changedMethods.Length);
            await Task.Yield();
            return changedMethods.Distinct().ToList();
        }

        public async Task<SemanticModel> GetSemanticModelAsync(string filePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(filePath)) throw new FileNotFoundException($"File not found: {filePath}");

            var sourceCode = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                assemblyName: Path.GetFileNameWithoutExtension(filePath),
                syntaxTrees: new[] { syntaxTree },
                references: RoslynAnalyzerHelper.GetBasicReferences()
            );
            return compilation.GetSemanticModel(syntaxTree);
        }

        public async Task<IReadOnlyList<TypeUsageInfo>> AnalyzeTypeUsageAsync(string[] sourceFiles, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("[NetStandard] Type usage analysis across {Count} files (simplified)", sourceFiles.Length);

            var results = new List<TypeUsageInfo>();
            foreach (var file in sourceFiles.Where(File.Exists))
            {
                var sourceCode = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode, path: file);
                var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                    assemblyName: Path.GetFileNameWithoutExtension(file),
                    syntaxTrees: new[] { syntaxTree },
                    references: RoslynAnalyzerHelper.GetBasicReferences()
                );
                var semanticModel = compilation.GetSemanticModel(syntaxTree);

                var root = await syntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
                var typeDecls = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax>();
                foreach (var typeDecl in typeDecls)
                {
                    var symbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);
                    if (symbol == null) continue;
                    var ns = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                    var line = typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    results.Add(new TypeUsageInfo(symbol.Name, ns, file, line, TypeUsageContext.Declaration));
                }
            }
            return results;
        }

        public async Task<IReadOnlyList<MethodInfo>> ExtractMethodsFromFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(filePath)) throw new FileNotFoundException($"File not found: {filePath}");

            var methods = new List<MethodInfo>();
            var sourceCode = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            var root = await syntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                assemblyName: Path.GetFileNameWithoutExtension(filePath),
                syntaxTrees: new[] { syntaxTree },
                references: RoslynAnalyzerHelper.GetBasicReferences()
            );
            var semanticModel = compilation.GetSemanticModel(syntaxTree);

            var methodDecls = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>();
            foreach (var method in methodDecls)
            {
                var symbol = semanticModel.GetDeclaredSymbol(method, cancellationToken) as IMethodSymbol;
                if (symbol == null) continue;
                var id = RoslynAnalyzerHelper.GetMethodIdentifier(symbol);
                methods.Add(new MethodInfo(id, symbol.Name, symbol.ContainingType.Name, filePath, method.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
            }
            return methods;
        }

        private Task<MethodCallGraph> BuildCallGraphFromFilesAsync(string[] files, CancellationToken cancellationToken)
        {
            // Minimal graph: return an empty graph in netstandard mode.
            // Upstream logic is resilient and will handle empty graphs gracefully.
            var graph = new MethodCallGraph(
                new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>(),
                new System.Collections.Generic.Dictionary<string, MethodInfo>()
            );
            return Task.FromResult(graph);
        }

        public void Dispose()
        {
        }

        public Task<IReadOnlyList<TestCoverageResult>> FindTestsExercisingMethodAsync(string methodId, string[] solutionFiles, CancellationToken cancellationToken = default)
        {
            // Netstandard fallback: without full solution analysis, return empty coverage set.
            IReadOnlyList<TestCoverageResult> empty = Array.Empty<TestCoverageResult>();
            return Task.FromResult(empty);
        }
    }
}
