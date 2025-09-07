using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using TestIntelligence.ImpactAnalyzer.Analysis.Utilities;

namespace TestIntelligence.ImpactAnalyzer.Analysis.Workspace
{
    public class WorkspaceManager : IWorkspaceManager
    {
        private readonly ILogger<WorkspaceManager> _logger;
        private readonly ILoggerFactory _loggerFactory;
        
        // Component dependencies
        private readonly SolutionParser _solutionParser;
        private readonly ProjectParser _projectParser;
        private readonly DependencyGraphBuilder _dependencyGraphBuilder;
        private readonly SolutionWorkspaceBuilder _workspaceBuilder;

        // Legacy infrastructure (kept for fallback)
        private SolutionWorkspace? _currentWorkspace;
        private ICompilationManager? _compilationManager;
        private SymbolResolutionEngine? _symbolResolver;

        // New lazy infrastructure for performance
        private LazyWorkspaceBuilder? _lazyWorkspaceBuilder;
        private SymbolIndex? _symbolIndex;

        public bool IsInitialized => _currentWorkspace != null && _compilationManager != null;
        public ICompilationManager? CompilationManager => _compilationManager;
        public SymbolResolutionEngine? SymbolResolver => _symbolResolver;

        public WorkspaceManager(ILogger<WorkspaceManager> logger, ILoggerFactory loggerFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            
            _solutionParser = new SolutionParser(_loggerFactory.CreateLogger<SolutionParser>());
            _projectParser = new ProjectParser(_loggerFactory.CreateLogger<ProjectParser>());
            _dependencyGraphBuilder = new DependencyGraphBuilder(_loggerFactory.CreateLogger<DependencyGraphBuilder>());
            _workspaceBuilder = new SolutionWorkspaceBuilder(_loggerFactory.CreateLogger<SolutionWorkspaceBuilder>());
        }

        public async Task InitializeAsync(string solutionPath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (_currentWorkspace != null)
            {
                _logger.LogDebug("Workspace already initialized for solution");
                return;
            }

            _logger.LogInformation("Initializing solution workspace: {SolutionPath}", solutionPath);

            try
            {
                // Parse solution structure
                var solutionInfo = await _solutionParser.ParseSolutionAsync(solutionPath, cancellationToken).ConfigureAwait(false);
                
                // Parse individual projects
                var projectTasks = solutionInfo.Projects.Select(async p =>
                {
                    try
                    {
                        return await _projectParser.ParseProjectAsync(p.Path, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse project: {ProjectPath}", p.Path);
                        return null;
                    }
                });
                
                var projectDetails = (await Task.WhenAll(projectTasks).ConfigureAwait(false)).Where(p => p != null).ToList();
                
                // Build dependency graph
                var dependencyGraph = _dependencyGraphBuilder.BuildDependencyGraph(projectDetails!);
                
                // Create workspace
                _currentWorkspace = await _workspaceBuilder.CreateWorkspaceAsync(solutionPath, cancellationToken).ConfigureAwait(false);
                
                // Initialize managers
                _compilationManager = new CompilationManager(
                    _loggerFactory.CreateLogger<CompilationManager>(), _currentWorkspace);
                
                _symbolResolver = new SymbolResolutionEngine(
                    _compilationManager, _loggerFactory.CreateLogger<SymbolResolutionEngine>());

                _logger.LogInformation("Solution workspace initialized successfully with {ProjectCount} projects", 
                    solutionInfo.Projects.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize workspace for solution: {SolutionPath}", solutionPath);
                throw;
            }
        }

        public async Task InitializeLazyAsync(string solutionPath, CancellationToken cancellationToken = default)
        {
            if (_lazyWorkspaceBuilder != null && _symbolIndex != null)
            {
                _logger.LogDebug("Lazy workspace already initialized");
                return;
            }

            _logger.LogInformation("Initializing high-performance lazy workspace: {SolutionPath}", solutionPath);
            var startTime = DateTime.UtcNow;

            try
            {
                // Initialize symbol index
                _symbolIndex = new SymbolIndex(_loggerFactory.CreateLogger<SymbolIndex>());
                await _symbolIndex.BuildIndexAsync(solutionPath, cancellationToken);

                // Initialize lazy workspace builder
                _lazyWorkspaceBuilder = new LazyWorkspaceBuilder(_symbolIndex, _loggerFactory.CreateLogger<LazyWorkspaceBuilder>());
                await _lazyWorkspaceBuilder.InitializeAsync(solutionPath, cancellationToken);

                // Initialize fallback workspace if needed for symbol resolution
                if (_compilationManager == null)
                {
                    await InitializeAsync(solutionPath, cancellationToken);
                }

                var elapsed = DateTime.UtcNow - startTime;
                _logger.LogInformation("Lazy workspace initialized in {ElapsedMs}ms with high-performance indexing", 
                    elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize lazy workspace: {SolutionPath}", solutionPath);
                throw;
            }
        }

        public async Task<SemanticModel?> GetSemanticModelAsync(string filePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (_compilationManager != null)
            {
                var semanticModel = _compilationManager.GetSemanticModel(filePath);
                if (semanticModel != null)
                {
                    return semanticModel;
                }
            }

            // Fallback to individual file compilation
            return await GetSemanticModelFallbackAsync(filePath, cancellationToken).ConfigureAwait(false);
        }

        public async Task<Microsoft.CodeAnalysis.SyntaxTree?> GetSyntaxTreeAsync(string filePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (_compilationManager != null)
            {
                return await _compilationManager.GetSyntaxTreeAsync(filePath, cancellationToken);
            }

            // Fallback: create syntax tree from file
            if (!File.Exists(filePath))
                return null;

            var sourceCode = await File.ReadAllTextAsync(filePath, cancellationToken);
            return Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
        }

        private async Task<SemanticModel> GetSemanticModelFallbackAsync(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var sourceCode = await File.ReadAllTextAsync(filePath, cancellationToken);
            var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                assemblyName: Path.GetFileNameWithoutExtension(filePath),
                syntaxTrees: new[] { syntaxTree },
                references: RoslynAnalyzerHelper.GetBasicReferences()
            );

            return compilation.GetSemanticModel(syntaxTree);
        }

        public void Dispose()
        {
            _currentWorkspace?.Dispose();
            _currentWorkspace = null;
            _compilationManager = null;
            _symbolResolver = null;
            _lazyWorkspaceBuilder = null;
            _symbolIndex = null;
        }
    }
}