using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    /// <summary>
    /// Lazy workspace builder that loads MSBuild projects on-demand instead of upfront.
    /// This dramatically reduces initialization time and memory usage for large solutions.
    /// </summary>
    public class LazyWorkspaceBuilder : IDisposable
    {
        private readonly ILogger<LazyWorkspaceBuilder> _logger;
        private readonly MSBuildWorkspace _workspace;
        private readonly SymbolIndex _symbolIndex;
        
        // Cache for loaded projects to avoid reloading
        private readonly ConcurrentDictionary<string, Task<Project?>> _projectCache = new();
        
        // Cache for compilations to avoid recompiling
        private readonly ConcurrentDictionary<string, Task<Compilation?>> _compilationCache = new();
        
        // Map of file paths to their containing project paths
        private readonly ConcurrentDictionary<string, string> _fileToProjectMap = new();
        
        // Solution-level information
        private Solution? _solution;
        private readonly object _solutionLock = new object();
        
        private bool _disposed = false;

        public LazyWorkspaceBuilder(SymbolIndex symbolIndex, ILogger<LazyWorkspaceBuilder> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _symbolIndex = symbolIndex ?? throw new ArgumentNullException(nameof(symbolIndex));
            
            _workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
            {
                // Optimize MSBuild for better performance
                { "CheckForSystemRuntimeDependency", "true" },
                { "DesignTimeBuild", "true" },
                { "BuildProjectReferences", "false" }, // Don't build dependencies upfront
                { "SkipCompilerExecution", "true" }
            });
            
            // Subscribe to workspace events for debugging
            _workspace.WorkspaceFailed += OnWorkspaceFailed;
        }

        /// <summary>
        /// Initializes the workspace with solution information without loading all projects
        /// </summary>
        public async Task InitializeAsync(string solutionPath, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Initializing lazy workspace for solution: {SolutionPath}", solutionPath);
            var startTime = DateTime.UtcNow;

            try
            {
                // Build symbol index first for fast file-to-project mapping
                await _symbolIndex.BuildIndexAsync(solutionPath, cancellationToken);
                
                // Load solution metadata without loading all projects
                if (Path.GetExtension(solutionPath).Equals(".sln", StringComparison.OrdinalIgnoreCase))
                {
                    await InitializeFromSolutionAsync(solutionPath, cancellationToken);
                }
                else if (Path.GetExtension(solutionPath).EndsWith("proj", StringComparison.OrdinalIgnoreCase))
                {
                    await InitializeFromProjectAsync(solutionPath, cancellationToken);
                }
                else
                {
                    throw new ArgumentException($"Unsupported file type: {solutionPath}");
                }

                var elapsed = DateTime.UtcNow - startTime;
                _logger.LogInformation("Lazy workspace initialized in {ElapsedMs}ms without loading projects", elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize lazy workspace for: {SolutionPath}", solutionPath);
                throw;
            }
        }

        /// <summary>
        /// Gets the project containing a specific file, loading it on-demand
        /// </summary>
        public async Task<Project?> GetProjectContainingFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            // Normalize file path
            filePath = Path.GetFullPath(filePath);

            // Check if we already know which project contains this file
            if (_fileToProjectMap.TryGetValue(filePath, out var cachedProjectPath))
            {
                return await GetOrLoadProjectAsync(cachedProjectPath, cancellationToken);
            }

            // Use symbol index to find the project
            var projectInfo = _symbolIndex.GetProjectForFile(filePath);
            if (projectInfo != null)
            {
                _fileToProjectMap[filePath] = projectInfo.Path;
                return await GetOrLoadProjectAsync(projectInfo.Path, cancellationToken);
            }

            // Fallback: search through all projects (expensive, but comprehensive)
            _logger.LogDebug("File {FilePath} not found in symbol index, searching all projects", filePath);
            var projects = await GetAllProjectPathsAsync();
            
            foreach (var projectPath in projects)
            {
                var project = await GetOrLoadProjectAsync(projectPath, cancellationToken);
                if (project?.Documents.Any(d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase)) == true)
                {
                    _fileToProjectMap[filePath] = projectPath;
                    return project;
                }
            }

            _logger.LogWarning("Could not find project containing file: {FilePath}", filePath);
            return null;
        }

        /// <summary>
        /// Gets a project by path, loading it on-demand
        /// </summary>
        public async Task<Project?> GetOrLoadProjectAsync(string projectPath, CancellationToken cancellationToken = default)
        {
            return await _projectCache.GetOrAdd(projectPath, async path =>
            {
                CancellationTokenSource? timeoutCts = null;
                CancellationTokenSource? combinedCts = null;
                
                try
                {
                    _logger.LogDebug("Loading project on-demand: {ProjectPath}", path);
                    var startTime = DateTime.UtcNow;

                    // Add timeout to prevent hanging on project loading
                    timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                    var projectTask = _workspace.OpenProjectAsync(path);
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15), combinedCts.Token);
                    
                    var completedTask = await Task.WhenAny(projectTask, timeoutTask);
                    if (completedTask == timeoutTask)
                    {
                        _logger.LogWarning("Project loading timed out after 15 seconds: {ProjectPath}", path);
                        return null;
                    }
                    
                    var project = await projectTask;
                    
                    var elapsed = DateTime.UtcNow - startTime;
                    _logger.LogDebug("Project loaded in {ElapsedMs}ms: {ProjectName}", elapsed.TotalMilliseconds, project.Name);

                    return project;
                }
                catch (OperationCanceledException) when (timeoutCts?.Token.IsCancellationRequested == true)
                {
                    _logger.LogWarning("Project loading timed out after 15 seconds: {ProjectPath}", path);
                    return null;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load project: {ProjectPath}", path);
                    return null;
                }
                finally
                {
                    timeoutCts?.Dispose();
                    combinedCts?.Dispose();
                }
            });
        }

        /// <summary>
        /// Gets compilation for a project, compiling on-demand
        /// </summary>
        public async Task<Compilation?> GetCompilationAsync(string projectPath, CancellationToken cancellationToken = default)
        {
            return await _compilationCache.GetOrAdd(projectPath, async path =>
            {
                try
                {
                    var project = await GetOrLoadProjectAsync(path, cancellationToken);
                    if (project == null) return null;

                    _logger.LogDebug("Compiling project on-demand: {ProjectName}", project.Name);
                    var startTime = DateTime.UtcNow;

                    var compilation = await project.GetCompilationAsync(cancellationToken);
                    
                    var elapsed = DateTime.UtcNow - startTime;
                    _logger.LogDebug("Project compiled in {ElapsedMs}ms: {ProjectName}", elapsed.TotalMilliseconds, project.Name);

                    return compilation;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to compile project: {ProjectPath}", path);
                    return null;
                }
            });
        }

        /// <summary>
        /// Gets projects that might contain a specific method, using the symbol index
        /// </summary>
        public async Task<List<Project>> GetProjectsContainingMethodAsync(string methodId, CancellationToken cancellationToken = default)
        {
            var projects = new List<Project>();
            
            try
            {
                var projectInfos = await _symbolIndex.FindProjectsContainingMethodAsync(methodId, cancellationToken);
                
                var loadTasks = projectInfos.Select(async projectInfo =>
                {
                    var project = await GetOrLoadProjectAsync(projectInfo.Path, cancellationToken);
                    return project;
                });

                var loadedProjects = await Task.WhenAll(loadTasks);
                projects.AddRange(loadedProjects.Where(p => p != null)!);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get projects containing method: {MethodId}", methodId);
            }
            
            return projects;
        }

        /// <summary>
        /// Gets all available project paths without loading them
        /// </summary>
        public Task<List<string>> GetAllProjectPathsAsync()
        {
            var projectPaths = new List<string>();
            
            if (_solution != null)
            {
                projectPaths.AddRange(_solution.Projects.Select(p => p.FilePath).Where(p => !string.IsNullOrEmpty(p))!);
            }
            else
            {
                // Fallback: get from symbol index
                projectPaths.AddRange(_fileToProjectMap.Values.Distinct());
            }
            
            return Task.FromResult(projectPaths);
        }

        /// <summary>
        /// Preloads specific projects in the background for better performance
        /// </summary>
        public async Task PreloadProjectsAsync(IEnumerable<string> projectPaths, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Preloading {ProjectCount} projects in background", projectPaths.Count());
            
            var preloadTasks = projectPaths.Select(async projectPath =>
            {
                try
                {
                    await GetOrLoadProjectAsync(projectPath, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to preload project: {ProjectPath}", projectPath);
                }
            });

            await Task.WhenAll(preloadTasks);
            _logger.LogDebug("Project preloading completed");
        }

        /// <summary>
        /// Gets memory usage statistics for the workspace
        /// </summary>
        public WorkspaceStats GetStats()
        {
            return new WorkspaceStats
            {
                LoadedProjects = _projectCache.Count,
                CompiledProjects = _compilationCache.Count,
                FileToProjectMappings = _fileToProjectMap.Count,
                TotalProjects = _solution?.Projects.Count() ?? 0
            };
        }

        /// <summary>
        /// Clears caches to free memory
        /// </summary>
        public void ClearCaches()
        {
            _logger.LogInformation("Clearing workspace caches");
            
            _projectCache.Clear();
            _compilationCache.Clear();
            
            // Keep file-to-project mappings as they're lightweight and useful
            _logger.LogInformation("Workspace caches cleared");
        }

        private async Task InitializeFromSolutionAsync(string solutionPath, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Loading solution metadata: {SolutionPath}", solutionPath);
            
            // Load solution directly with timeout to prevent hanging
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            
            try
            {
                var solutionTask = _workspace.OpenSolutionAsync(solutionPath);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30), combinedCts.Token);
                
                var completedTask = await Task.WhenAny(solutionTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    _logger.LogWarning("Solution loading timed out after 30 seconds, falling back to manual solution parsing");
                    await InitializeFromSolutionManuallyAsync(solutionPath, cancellationToken);
                    return;
                }
                
                _solution = await solutionTask;
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                _logger.LogWarning("Solution loading timed out after 30 seconds, falling back to manual solution parsing");
                await InitializeFromSolutionManuallyAsync(solutionPath, cancellationToken);
                return;
            }
            
            var solutionInfo = _solution;

            // Build file-to-project mapping from solution
            foreach (var project in solutionInfo.Projects)
            {
                if (string.IsNullOrEmpty(project.FilePath)) continue;
                
                // Use symbol index to get files for this project
                var symbolProjectInfo = _symbolIndex.GetProjectForFile(project.FilePath!);
                if (symbolProjectInfo != null)
                {
                    var projectFiles = Directory.GetFiles(symbolProjectInfo.Directory, "*.cs", SearchOption.AllDirectories)
                        .Where(f => !f.Contains("bin") && !f.Contains("obj"));
                    
                    foreach (var file in projectFiles)
                    {
                        _fileToProjectMap[file] = project.FilePath!;
                    }
                }
            }
            
            _logger.LogInformation("Solution metadata loaded: {ProjectCount} projects, {FileCount} file mappings", 
                solutionInfo.Projects.Count(), _fileToProjectMap.Count);
        }

        private async Task InitializeFromProjectAsync(string projectPath, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Loading single project: {ProjectPath}", projectPath);
            
            // For single projects, we can load them immediately since there's only one
            var project = await GetOrLoadProjectAsync(projectPath, cancellationToken);
            if (project != null)
            {
                foreach (var document in project.Documents)
                {
                    if (!string.IsNullOrEmpty(document.FilePath) && !string.IsNullOrEmpty(projectPath))
                    {
                        _fileToProjectMap[document.FilePath!] = projectPath;
                    }
                }
            }
        }

        /// <summary>
        /// Manual solution parsing fallback when MSBuild workspace fails
        /// </summary>
        private async Task InitializeFromSolutionManuallyAsync(string solutionPath, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Using manual solution parsing for: {SolutionPath}", solutionPath);
                
                // Parse solution file manually to get project paths
                var solutionDir = Path.GetDirectoryName(solutionPath)!;
                var solutionLines = await File.ReadAllLinesAsync(solutionPath, cancellationToken);
                
                var projectPaths = new List<string>();
                foreach (var line in solutionLines)
                {
                    // Look for project lines: Project("{...}") = "ProjectName", "RelativePath", "{...}"
                    if (line.StartsWith("Project(") && line.Contains(".csproj"))
                    {
                        var parts = line.Split(',');
                        if (parts.Length >= 2)
                        {
                            var relativePath = parts[1].Trim().Trim('"');
                            var fullPath = Path.GetFullPath(Path.Combine(solutionDir, relativePath));
                            if (File.Exists(fullPath))
                            {
                                projectPaths.Add(fullPath);
                            }
                        }
                    }
                }
                
                _logger.LogInformation("Found {ProjectCount} projects via manual parsing", projectPaths.Count);
                
                // Build file-to-project mapping using file system scanning
                foreach (var projectPath in projectPaths)
                {
                    try
                    {
                        var projectDir = Path.GetDirectoryName(projectPath)!;
                        var csFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                            .Where(f => !f.Contains("bin") && !f.Contains("obj") && !f.Contains("packages"));
                        
                        foreach (var file in csFiles)
                        {
                            _fileToProjectMap[file] = projectPath;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to scan project directory: {ProjectPath}", projectPath);
                    }
                }
                
                _logger.LogInformation("Manual solution parsing completed: {ProjectCount} projects, {FileCount} file mappings", 
                    projectPaths.Count, _fileToProjectMap.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Manual solution parsing failed for: {SolutionPath}", solutionPath);
                throw;
            }
        }

        private void OnWorkspaceFailed(object? sender, WorkspaceDiagnosticEventArgs e)
        {
            _logger.LogDebug("Workspace diagnostic: {Kind} - {Message}", e.Diagnostic.Kind, e.Diagnostic.Message);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _workspace.WorkspaceFailed -= OnWorkspaceFailed;
                _workspace.Dispose();
                _disposed = true;
            }
        }

        public class WorkspaceStats
        {
            public int LoadedProjects { get; set; }
            public int CompiledProjects { get; set; }
            public int FileToProjectMappings { get; set; }
            public int TotalProjects { get; set; }

            public double LoadedProjectsPercentage => TotalProjects > 0 ? (LoadedProjects * 100.0) / TotalProjects : 0;
        }
    }
}