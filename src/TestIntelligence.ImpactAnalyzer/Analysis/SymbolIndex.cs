using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    /// <summary>
    /// Fast symbol index for looking up methods and types without full semantic analysis.
    /// Provides O(1) lookups for method and type locations across large solutions.
    /// </summary>
    public class SymbolIndex : ISymbolIndex
    {
        private readonly ILogger<SymbolIndex> _logger;
        
        // Fast lookup tables - these are populated once and enable fast queries
        private readonly ConcurrentDictionary<string, HashSet<string>> _methodToFiles = new();
        private readonly ConcurrentDictionary<string, HashSet<string>> _typeToFiles = new();
        private readonly ConcurrentDictionary<string, HashSet<string>> _namespaceToFiles = new();
        private readonly ConcurrentDictionary<string, ProjectInfo> _fileToProject = new();
        private readonly HashSet<string> _indexedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Pattern matching for common method signatures without full parsing
        private static readonly Regex MethodDeclarationPattern = new Regex(
            @"^\s*(?:public|private|protected|internal|static|\w+)*\s+(?:\w+\s+)*(\w+)\s*\([^)]*\)\s*(?:\{|=>)", 
            RegexOptions.Compiled | RegexOptions.Multiline);
            
        private static readonly Regex ClassDeclarationPattern = new Regex(
            @"^\s*(?:public|private|protected|internal|static|abstract|partial)*\s*(?:class|interface|struct|enum)\s+(\w+)",
            RegexOptions.Compiled | RegexOptions.Multiline);
            
        private static readonly Regex NamespacePattern = new Regex(
            @"^\s*namespace\s+([\w\.]+)",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private volatile bool _isIndexBuilt = false;
        private readonly object _indexLock = new object();

        public SymbolIndex(ILogger<SymbolIndex> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Builds the symbol index for fast lookups. This is a lightweight operation that scans
        /// file headers and declarations without full semantic analysis.
        /// </summary>
        public async Task BuildIndexAsync(string solutionPath, CancellationToken cancellationToken = default)
        {
            if (_isIndexBuilt)
            {
                _logger.LogDebug("Symbol index already built for solution");
                return;
            }

            lock (_indexLock)
            {
                if (_isIndexBuilt) return;
            }

            _logger.LogInformation("Building symbol index for solution: {SolutionPath}", solutionPath);
            var startTime = DateTime.UtcNow;

            try
            {
                // Find all C# source files in the solution
                var sourceFiles = await FindAllSourceFilesAsync(solutionPath, cancellationToken);
                _logger.LogInformation("Found {FileCount} source files to index", sourceFiles.Count);

                // Process files in parallel with controlled concurrency
                var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2);
                var indexingTasks = sourceFiles.Select(async filePath =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        await IndexFileAsync(filePath, cancellationToken);
                        lock (_indexedFiles) _indexedFiles.Add(filePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to index file: {FilePath}", filePath);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(indexingTasks);

                lock (_indexLock)
                {
                    _isIndexBuilt = true;
                }

                var elapsed = DateTime.UtcNow - startTime;
                _logger.LogInformation("Symbol index built in {ElapsedMs}ms: {MethodCount} methods, {TypeCount} types, {FileCount} files",
                    elapsed.TotalMilliseconds,
                    _methodToFiles.Count,
                    _typeToFiles.Count,
                    sourceFiles.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build symbol index for solution: {SolutionPath}", solutionPath);
                throw;
            }
        }

        /// <summary>
        /// Builds a symbol index limited to the provided analysis scope. This avoids scanning the entire solution
        /// when a small set of files or projects is sufficient.
        /// </summary>
        public async Task BuildIndexForScopeAsync(AnalysisScope scope, CancellationToken cancellationToken = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));

            if (_isIndexBuilt)
            {
                _logger.LogDebug("Symbol index already built; skipping scoped build");
                return;
            }

            lock (_indexLock)
            {
                if (_isIndexBuilt) return;
            }

            _logger.LogInformation("Building scoped symbol index: files={FileCount}, projects={ProjectCount}",
                scope.ChangedFiles?.Count ?? 0, scope.RelevantProjects?.Count ?? 0);

            try
            {
                var indexedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Index specified projects, if any
                if (scope.RelevantProjects != null && scope.RelevantProjects.Count > 0)
                {
                    foreach (var proj in scope.RelevantProjects)
                    {
                        var files = await GetSourceFilesFromProjectAsync(proj, cancellationToken);
                        foreach (var f in files)
                        {
                            if (indexedFiles.Add(f))
                            {
                                await IndexFileAsync(f, cancellationToken);
                                lock (_indexedFiles) _indexedFiles.Add(f);
                            }
                        }
                    }
                }

                // Index changed files directly
                if (scope.ChangedFiles != null && scope.ChangedFiles.Count > 0)
                {
                    foreach (var file in scope.ChangedFiles)
                    {
                        var path = file;
                        if (!Path.IsPathRooted(path))
                        {
                            // Try resolve relative to solution
                            var baseDir = Path.GetDirectoryName(scope.SolutionPath) ?? string.Empty;
                            path = Path.Combine(baseDir, file);
                        }

                        if (File.Exists(path) && indexedFiles.Add(path))
                        {
                            await IndexFileAsync(path, cancellationToken);
                            lock (_indexedFiles) _indexedFiles.Add(path);
                        }
                    }
                }

                // If nothing was indexed (empty scope), fall back to full index
                if (indexedFiles.Count == 0)
                {
                    _logger.LogDebug("Scoped index had no inputs; building full index as fallback");
                    await BuildIndexAsync(scope.SolutionPath, cancellationToken);
                    return;
                }

                lock (_indexLock)
                {
                    _isIndexBuilt = true;
                }

                _logger.LogInformation("Scoped symbol index built: {FileCount} files indexed", indexedFiles.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build scoped symbol index; falling back to full index");
                await BuildIndexAsync(scope.SolutionPath, cancellationToken);
            }
        }

        /// <summary>
        /// Returns all files indexed in the most recent build.
        /// </summary>
        public IReadOnlyCollection<string> GetIndexedFiles()
        {
            lock (_indexedFiles)
            {
                return _indexedFiles.ToList().AsReadOnly();
            }
        }

        /// <summary>
        /// Finds files containing a specific method. Returns quickly using pre-built index.
        /// </summary>
        public Task<List<string>> FindFilesContainingMethodAsync(string methodId, CancellationToken cancellationToken = default)
        {
            if (!_isIndexBuilt)
            {
                _logger.LogWarning("Symbol index not built yet. Consider calling BuildIndexAsync first.");
                return Task.FromResult(new List<string>());
            }

            // Try exact match first
            if (_methodToFiles.TryGetValue(methodId, out var exactFiles))
            {
                return Task.FromResult(exactFiles.ToList());
            }

            // Try fuzzy matching based on method name patterns
            var methodName = ExtractMethodNameFromId(methodId);
            var matchingFiles = new HashSet<string>();

            foreach (var kvp in _methodToFiles)
            {
                if (kvp.Key.Contains(methodName))
                {
                    foreach (var file in kvp.Value)
                    {
                        matchingFiles.Add(file);
                    }
                }
            }

            _logger.LogDebug("Found {FileCount} files potentially containing method {MethodName}", matchingFiles.Count, methodName);
            return Task.FromResult(matchingFiles.ToList());
        }

        /// <summary>
        /// Finds files containing a specific type
        /// </summary>
        public Task<List<string>> FindFilesContainingTypeAsync(string typeName, CancellationToken cancellationToken = default)
        {
            if (!_isIndexBuilt)
            {
                _logger.LogWarning("Symbol index not built yet");
                return Task.FromResult(new List<string>());
            }

            if (_typeToFiles.TryGetValue(typeName, out var files))
            {
                return Task.FromResult(files.ToList());
            }

            // Try fuzzy matching
            var matchingFiles = new HashSet<string>();
            foreach (var kvp in _typeToFiles)
            {
                if (kvp.Key.Contains(typeName) || typeName.Contains(kvp.Key))
                {
                    foreach (var file in kvp.Value)
                    {
                        matchingFiles.Add(file);
                    }
                }
            }

            return Task.FromResult(matchingFiles.ToList());
        }

        /// <summary>
        /// Gets all files in a specific namespace
        /// </summary>
        public Task<List<string>> GetFilesInNamespaceAsync(string namespaceName, CancellationToken cancellationToken = default)
        {
            if (!_isIndexBuilt)
            {
                return Task.FromResult(new List<string>());
            }

            if (_namespaceToFiles.TryGetValue(namespaceName, out var files))
            {
                return Task.FromResult(files.ToList());
            }

            return Task.FromResult(new List<string>());
        }

        /// <summary>
        /// Gets project information for a specific file
        /// </summary>
        public ProjectInfo? GetProjectForFile(string filePath)
        {
            return _fileToProject.TryGetValue(filePath, out var projectInfo) ? projectInfo : null;
        }

        /// <summary>
        /// Finds all projects that might contain a specific method
        /// </summary>
        public async Task<List<ProjectInfo>> FindProjectsContainingMethodAsync(string methodId, CancellationToken cancellationToken = default)
        {
            var files = await FindFilesContainingMethodAsync(methodId, cancellationToken);
            var projects = new HashSet<ProjectInfo>();

            foreach (var file in files)
            {
                var project = GetProjectForFile(file);
                if (project != null)
                {
                    projects.Add(project);
                }
            }

            return projects.ToList();
        }

        /// <summary>
        /// Refreshes index for specific files (useful after file changes)
        /// </summary>
        public async Task RefreshFilesAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
        {
            var refreshTasks = filePaths.Select(async filePath =>
            {
                try
                {
                    // Remove existing entries for this file
                    RemoveFileFromIndex(filePath);
                    
                    // Re-index the file
                    await IndexFileAsync(filePath, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to refresh index for file: {FilePath}", filePath);
                }
            });

            await Task.WhenAll(refreshTasks);
            _logger.LogDebug("Refreshed symbol index for {FileCount} files", filePaths.Count());
        }

        /// <summary>
        /// Clears the entire index
        /// </summary>
        public void Clear()
        {
            lock (_indexLock)
            {
                _methodToFiles.Clear();
                _typeToFiles.Clear();
                _namespaceToFiles.Clear();
                _fileToProject.Clear();
                _isIndexBuilt = false;
            }
            
            _logger.LogInformation("Symbol index cleared");
        }

        private async Task<List<string>> FindAllSourceFilesAsync(string solutionPath, CancellationToken cancellationToken)
        {
            var sourceFiles = new List<string>();
            
            try
            {
                // If it's a solution file, parse it to find project files
                if (Path.GetExtension(solutionPath).Equals(".sln", StringComparison.OrdinalIgnoreCase))
                {
                    var solutionDir = Path.GetDirectoryName(solutionPath) ?? "";
                    var solutionContent = File.ReadAllText(solutionPath);
                    
                    // Simple regex to find project references in solution file
                    var projectPattern = new Regex(@"Project\(.+\)\s*=\s*"".+"",\s*""([^""]+\.(?:csproj|vbproj|fsproj))""", RegexOptions.Compiled);
                    var matches = projectPattern.Matches(solutionContent);
                    
                    foreach (Match match in matches)
                    {
                        var relativePath = match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar);
                        var projectPath = Path.Combine(solutionDir, relativePath);
                        if (File.Exists(projectPath))
                        {
                            var projectFiles = await GetSourceFilesFromProjectAsync(projectPath, cancellationToken);
                            sourceFiles.AddRange(projectFiles);
                        }
                    }
                }
                else if (Path.GetExtension(solutionPath).EndsWith("proj", StringComparison.OrdinalIgnoreCase))
                {
                    // Single project file
                    var projectFiles = await GetSourceFilesFromProjectAsync(solutionPath, cancellationToken);
                    sourceFiles.AddRange(projectFiles);
                }
                else
                {
                    // Directory - find all C# files recursively
                    var directory = Directory.Exists(solutionPath) ? solutionPath : Path.GetDirectoryName(solutionPath) ?? "";
                    if (Directory.Exists(directory))
                    {
                        sourceFiles.AddRange(Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to find source files for: {SolutionPath}", solutionPath);
            }
            
            return sourceFiles.Where(File.Exists).ToList();
        }

        private Task<List<string>> GetSourceFilesFromProjectAsync(string projectPath, CancellationToken cancellationToken)
        {
            var sourceFiles = new List<string>();
            
            try
            {
                var projectDir = Path.GetDirectoryName(projectPath) ?? "";
                var projectName = Path.GetFileNameWithoutExtension(projectPath);
                
                // Store project info
                var projectInfo = new ProjectInfo(projectName, projectPath, projectDir);
                
                // Simple approach: find all .cs files in project directory
                var csFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("bin") && !f.Contains("obj")) // Skip build outputs
                    .ToList();
                
                foreach (var file in csFiles)
                {
                    _fileToProject[file] = projectInfo;
                }
                
                sourceFiles.AddRange(csFiles);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get source files from project: {ProjectPath}", projectPath);
            }
            
            return Task.FromResult(sourceFiles);
        }

        private async Task IndexFileAsync(string filePath, CancellationToken cancellationToken)
        {
            try
            {
                if (!File.Exists(filePath))
                    return;

                var content = File.ReadAllText(filePath);
                
                // Extract namespace information
                var namespaceMatches = NamespacePattern.Matches(content);
                foreach (Match match in namespaceMatches)
                {
                    var namespaceName = match.Groups[1].Value;
                    if (!_namespaceToFiles.ContainsKey(namespaceName))
                        _namespaceToFiles[namespaceName] = new HashSet<string>();
                    _namespaceToFiles[namespaceName].Add(filePath);
                }

                // Extract type declarations
                var classMatches = ClassDeclarationPattern.Matches(content);
                foreach (Match match in classMatches)
                {
                    var typeName = match.Groups[1].Value;
                    if (!_typeToFiles.ContainsKey(typeName))
                        _typeToFiles[typeName] = new HashSet<string>();
                    _typeToFiles[typeName].Add(filePath);
                }

                // Extract method declarations using lightweight parsing
                await IndexMethodsInFileAsync(filePath, content, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to index file: {FilePath}", filePath);
            }
        }

        private async Task IndexMethodsInFileAsync(string filePath, string content, CancellationToken cancellationToken)
        {
            try
            {
                // Use lightweight syntax parsing for better accuracy than regex
                var syntaxTree = CSharpSyntaxTree.ParseText(content, CSharpParseOptions.Default, filePath, cancellationToken: cancellationToken);
                var root = await syntaxTree.GetRootAsync(cancellationToken);

                // Find all method declarations
                var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
                
                foreach (var method in methodDeclarations)
                {
                    var methodName = method.Identifier.ValueText;
                    var containingClass = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                    var containingNamespace = method.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
                    
                    // Build approximate method ID
                    var methodId = BuildApproximateMethodId(methodName, containingClass, containingNamespace, method);
                    
                    if (!_methodToFiles.ContainsKey(methodId))
                        _methodToFiles[methodId] = new HashSet<string>();
                    _methodToFiles[methodId].Add(filePath);

                    // Also index by simple method name for fuzzy matching
                    if (!_methodToFiles.ContainsKey(methodName))
                        _methodToFiles[methodName] = new HashSet<string>();
                    _methodToFiles[methodName].Add(filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse methods in file: {FilePath}", filePath);
                
                // Fallback to regex-based extraction
                var methodMatches = MethodDeclarationPattern.Matches(content);
                foreach (Match match in methodMatches)
                {
                    var methodName = match.Groups[1].Value;
                    if (!_methodToFiles.ContainsKey(methodName))
                        _methodToFiles[methodName] = new HashSet<string>();
                    _methodToFiles[methodName].Add(filePath);
                }
            }
        }

        private static string BuildApproximateMethodId(
            string methodName, 
            ClassDeclarationSyntax? containingClass, 
            NamespaceDeclarationSyntax? containingNamespace,
            MethodDeclarationSyntax method)
        {
            var parts = new List<string>();
            
            if (containingNamespace != null)
            {
                parts.Add(containingNamespace.Name.ToString());
            }
            
            if (containingClass != null)
            {
                parts.Add(containingClass.Identifier.ValueText);
            }
            
            parts.Add(methodName);
            
            // Add parameter count for basic overload differentiation
            var paramCount = method.ParameterList.Parameters.Count;
            parts.Add($"({paramCount} params)");
            
            return string.Join(".", parts);
        }

        private void RemoveFileFromIndex(string filePath)
        {
            // Remove file from all indexes
            foreach (var methodFiles in _methodToFiles.Values)
            {
                methodFiles.Remove(filePath);
            }
            
            foreach (var typeFiles in _typeToFiles.Values)
            {
                typeFiles.Remove(filePath);
            }
            
            foreach (var namespaceFiles in _namespaceToFiles.Values)
            {
                namespaceFiles.Remove(filePath);
            }
            
            _fileToProject.TryRemove(filePath, out _);
        }

        private static string ExtractMethodNameFromId(string methodId)
        {
            // Extract just the method name from a fully qualified method ID
            var parts = methodId.Split('.');
            if (parts.Length > 0)
            {
                var lastPart = parts[parts.Length - 1];
                // Remove parameter information if present
                var parenIndex = lastPart.IndexOf('(');
                return parenIndex > 0 ? lastPart.Substring(0, parenIndex) : lastPart;
            }
            return methodId;
        }

        public class ProjectInfo
        {
            public string Name { get; }
            public string Path { get; }
            public string Directory { get; }

            public ProjectInfo(string name, string path, string directory)
            {
                Name = name;
                Path = path;
                Directory = directory;
            }

            public override bool Equals(object? obj)
            {
                return obj is ProjectInfo other && Path.Equals(other.Path, StringComparison.OrdinalIgnoreCase);
            }

            public override int GetHashCode()
            {
                return StringComparer.OrdinalIgnoreCase.GetHashCode(Path);
            }
        }
    }
}
