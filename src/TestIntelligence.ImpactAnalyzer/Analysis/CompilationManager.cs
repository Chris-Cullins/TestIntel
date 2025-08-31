using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public class CompilationManager
    {
        private readonly ILogger<CompilationManager> _logger;
        private readonly SolutionWorkspace _solutionWorkspace;
        private readonly ConcurrentDictionary<string, SemanticModel> _semanticModelCache;

        public CompilationManager(ILogger<CompilationManager> logger, SolutionWorkspace solutionWorkspace)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _solutionWorkspace = solutionWorkspace ?? throw new ArgumentNullException(nameof(solutionWorkspace));
            _semanticModelCache = new ConcurrentDictionary<string, SemanticModel>();
        }

        public async Task<IReadOnlyDictionary<ProjectId, Compilation>> BuildSolutionCompilationsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Building solution compilations");
            
            var compilations = new Dictionary<ProjectId, Compilation>();
            var projects = _solutionWorkspace.Solution.Projects.Where(p => p.Language == LanguageNames.CSharp).ToList();

            _logger.LogDebug("Found {ProjectCount} C# projects to compile", projects.Count);

            // Build compilations in parallel for independent projects
            var compilationTasks = projects.Select(async project =>
            {
                try
                {
                    var compilation = await project.GetCompilationAsync(cancellationToken);
                    if (compilation != null)
                    {
                        return new KeyValuePair<ProjectId, Compilation>(project.Id, compilation);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to build compilation for project: {ProjectName}", project.Name);
                }
                
                return new KeyValuePair<ProjectId, Compilation>(project.Id, null!);
            });

            var results = await Task.WhenAll(compilationTasks);
            
            foreach (var result in results.Where(r => r.Value != null))
            {
                compilations[result.Key] = result.Value;
            }

            _logger.LogInformation("Successfully built {CompilationCount} of {ProjectCount} compilations", 
                compilations.Count, projects.Count);

            return compilations;
        }

        public Compilation? GetCompilationForProject(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath))
                return null;

            var normalizedPath = Path.GetFullPath(projectPath);
            
            if (_solutionWorkspace.ProjectPathToId.TryGetValue(normalizedPath, out var projectId))
            {
                if (_solutionWorkspace.Compilations.TryGetValue(projectId, out var compilation))
                {
                    return compilation;
                }
            }

            _logger.LogWarning("No compilation found for project path: {ProjectPath}", projectPath);
            return null;
        }

        public Compilation? GetCompilationForFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            var normalizedPath = Path.GetFullPath(filePath);

            // Find the project that contains this file
            foreach (var project in _solutionWorkspace.Solution.Projects)
            {
                if (project.Documents.Any(d => 
                    !string.IsNullOrEmpty(d.FilePath) && 
                    Path.GetFullPath(d.FilePath) == normalizedPath))
                {
                    if (_solutionWorkspace.Compilations.TryGetValue(project.Id, out var compilation))
                    {
                        return compilation;
                    }
                }
            }

            _logger.LogDebug("No compilation found for file: {FilePath}", filePath);
            return null;
        }

        public SemanticModel? GetSemanticModel(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            var normalizedPath = Path.GetFullPath(filePath);

            // Check cache first
            if (_semanticModelCache.TryGetValue(normalizedPath, out var cachedModel))
            {
                return cachedModel;
            }

            var compilation = GetCompilationForFile(filePath);
            if (compilation == null)
                return null;

            // Find the syntax tree for this file
            var syntaxTree = compilation.SyntaxTrees.FirstOrDefault(st => 
                !string.IsNullOrEmpty(st.FilePath) && 
                Path.GetFullPath(st.FilePath) == normalizedPath);

            if (syntaxTree != null)
            {
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                _semanticModelCache.TryAdd(normalizedPath, semanticModel);
                return semanticModel;
            }

            return null;
        }

        public ISymbol? ResolveSymbolInfo(SyntaxNode node, string filePath)
        {
            var semanticModel = GetSemanticModel(filePath);
            if (semanticModel == null)
                return null;

            try
            {
                var symbolInfo = semanticModel.GetSymbolInfo(node);
                return symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to resolve symbol info for node in file: {FilePath}", filePath);
                return null;
            }
        }

        public ITypeSymbol? ResolveTypeInfo(SyntaxNode node, string filePath)
        {
            var semanticModel = GetSemanticModel(filePath);
            if (semanticModel == null)
                return null;

            try
            {
                var typeInfo = semanticModel.GetTypeInfo(node);
                return typeInfo.Type ?? typeInfo.ConvertedType;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to resolve type info for node in file: {FilePath}", filePath);
                return null;
            }
        }

        public Project? GetProjectForFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            var normalizedPath = Path.GetFullPath(filePath);

            foreach (var project in _solutionWorkspace.Solution.Projects)
            {
                if (project.Documents.Any(d => 
                    !string.IsNullOrEmpty(d.FilePath) && 
                    Path.GetFullPath(d.FilePath) == normalizedPath))
                {
                    return project;
                }
            }

            return null;
        }

        public IReadOnlyList<Project> GetAllProjects()
        {
            return _solutionWorkspace.Solution.Projects
                .Where(p => p.Language == LanguageNames.CSharp)
                .ToList();
        }

        public IReadOnlyList<string> GetAllSourceFiles()
        {
            var sourceFiles = new List<string>();
            
            foreach (var project in GetAllProjects())
            {
                foreach (var document in project.Documents)
                {
                    if (!string.IsNullOrEmpty(document.FilePath))
                    {
                        sourceFiles.Add(document.FilePath!);
                    }
                }
            }

            return sourceFiles;
        }

        public Task<Document?> GetDocumentAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(filePath))
                return Task.FromResult<Document?>(null);

            var normalizedPath = Path.GetFullPath(filePath);

            foreach (var project in _solutionWorkspace.Solution.Projects)
            {
                var document = project.Documents.FirstOrDefault(d => 
                    !string.IsNullOrEmpty(d.FilePath) && 
                    Path.GetFullPath(d.FilePath) == normalizedPath);
                
                if (document != null)
                    return Task.FromResult<Document?>(document);
            }

            return Task.FromResult<Document?>(null);
        }

        public async Task<SyntaxTree?> GetSyntaxTreeAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var document = await GetDocumentAsync(filePath, cancellationToken);
            if (document == null)
                return null;

            try
            {
                return await document.GetSyntaxTreeAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get syntax tree for file: {FilePath}", filePath);
                return null;
            }
        }

        public void ClearSemanticModelCache()
        {
            _semanticModelCache.Clear();
            _logger.LogDebug("Semantic model cache cleared");
        }

        public int GetCacheSize()
        {
            return _semanticModelCache.Count;
        }
    }
}