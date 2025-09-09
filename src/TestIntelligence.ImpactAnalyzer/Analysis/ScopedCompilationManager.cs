using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using TestIntelligence.ImpactAnalyzer.Analysis.Utilities;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    /// <summary>
    /// Lightweight compilation manager for scoped analysis: builds per-file compilations
    /// with basic framework references and avoids MSBuild workspace initialization.
    /// </summary>
    public class ScopedCompilationManager : ICompilationManager
    {
        private readonly ILogger<ScopedCompilationManager> _logger;
        private readonly IReadOnlyList<string> _sourceFiles;
        private readonly ConcurrentDictionary<string, SyntaxTree> _syntaxTrees = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Compilation> _compilations = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SemanticModel> _semanticModels = new(StringComparer.OrdinalIgnoreCase);

        public ScopedCompilationManager(ILogger<ScopedCompilationManager> logger, IEnumerable<string> sourceFiles)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sourceFiles = sourceFiles?.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                           ?? throw new ArgumentNullException(nameof(sourceFiles));
        }

        public Task<IReadOnlyDictionary<ProjectId, Compilation>> BuildSolutionCompilationsAsync(CancellationToken cancellationToken = default)
        {
            // Not applicable in scoped mode; return empty map
            IReadOnlyDictionary<ProjectId, Compilation> empty = new Dictionary<ProjectId, Compilation>();
            return Task.FromResult(empty);
        }

        public Compilation? GetCompilationForProject(string projectPath) => null;

        public Compilation? GetCompilationForFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            return _compilations.GetOrAdd(Path.GetFullPath(filePath), BuildCompilationForFile);
        }

        public SemanticModel? GetSemanticModel(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            return _semanticModels.GetOrAdd(Path.GetFullPath(filePath), fp =>
            {
                var compilation = GetCompilationForFile(fp);
                if (compilation == null) return null!;

                if (!_syntaxTrees.TryGetValue(fp, out var tree))
                {
                    var text = File.ReadAllText(fp);
                    tree = CSharpSyntaxTree.ParseText(text, path: fp);
                    _syntaxTrees[fp] = tree;
                }

                return compilation.GetSemanticModel(tree);
            });
        }

        public ISymbol? ResolveSymbolInfo(SyntaxNode node, string filePath)
        {
            var model = GetSemanticModel(filePath);
            if (model == null) return null;
            try { return model.GetSymbolInfo(node).Symbol; }
            catch { return null; }
        }

        public ITypeSymbol? ResolveTypeInfo(SyntaxNode node, string filePath)
        {
            var model = GetSemanticModel(filePath);
            if (model == null) return null;
            try { return model.GetTypeInfo(node).Type ?? model.GetTypeInfo(node).ConvertedType; }
            catch { return null; }
        }

        public Project? GetProjectForFile(string filePath) => null;

        public IReadOnlyList<Project> GetAllProjects() => Array.Empty<Project>();

        public IReadOnlyList<string> GetAllSourceFiles() => _sourceFiles.ToList();

        public Task<Document?> GetDocumentAsync(string filePath, CancellationToken cancellationToken = default) => Task.FromResult<Document?>(null);

        public Task<SyntaxTree?> GetSyntaxTreeAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return Task.FromResult<SyntaxTree?>(null);

            var full = Path.GetFullPath(filePath);
            if (_syntaxTrees.TryGetValue(full, out var tree))
                return Task.FromResult<SyntaxTree?>(tree);

            try
            {
                var text = File.ReadAllText(full);
                tree = CSharpSyntaxTree.ParseText(text, path: full);
                _syntaxTrees[full] = tree;
                return Task.FromResult<SyntaxTree?>(tree);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse syntax tree for file: {File}", filePath);
                return Task.FromResult<SyntaxTree?>(null);
            }
        }

        public void ClearSemanticModelCache()
        {
            _semanticModels.Clear();
            _syntaxTrees.Clear();
            _compilations.Clear();
        }

        public int GetCacheSize() => _semanticModels.Count;

        private Compilation BuildCompilationForFile(string filePath)
        {
            try
            {
                var tree = _syntaxTrees.GetOrAdd(filePath, fp =>
                {
                    var text = File.ReadAllText(fp);
                    return CSharpSyntaxTree.ParseText(text, path: fp);
                });

                var compilation = CSharpCompilation.Create(
                    assemblyName: Path.GetFileNameWithoutExtension(filePath),
                    syntaxTrees: new[] { tree },
                    references: RoslynAnalyzerHelper.GetBasicReferences(),
                    options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

                return compilation;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build standalone compilation for file: {File}", filePath);
                // Return an empty compilation to keep callers resilient
                return CSharpCompilation.Create(Path.GetFileNameWithoutExtension(filePath), references: RoslynAnalyzerHelper.GetBasicReferences());
            }
        }
    }
}

