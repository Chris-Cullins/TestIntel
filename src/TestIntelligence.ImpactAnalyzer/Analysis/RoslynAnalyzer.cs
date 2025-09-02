using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Models;
using TestIntelligence.ImpactAnalyzer.Models;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public class RoslynAnalyzer : IRoslynAnalyzer
    {
        private readonly ILogger<RoslynAnalyzer> _logger;
        private readonly SolutionParser _solutionParser;
        private readonly ProjectParser _projectParser;
        private readonly DependencyGraphBuilder _dependencyGraphBuilder;
        private readonly SolutionWorkspaceBuilder _workspaceBuilder;
        private readonly ILoggerFactory _loggerFactory;

        // New lazy infrastructure for performance
        private LazyWorkspaceBuilder? _lazyWorkspaceBuilder;
        private SymbolIndex? _symbolIndex;
        private IncrementalCallGraphBuilder? _incrementalCallGraphBuilder;

        // Legacy infrastructure (kept for fallback)
        private SolutionWorkspace? _currentWorkspace;
        private ICompilationManager? _compilationManager;
        private SymbolResolutionEngine? _symbolResolver;
        private CallGraphBuilderV2? _callGraphBuilder;

        public RoslynAnalyzer(ILogger<RoslynAnalyzer> logger, ILoggerFactory loggerFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            
            _solutionParser = new SolutionParser(_loggerFactory.CreateLogger<SolutionParser>());
            _projectParser = new ProjectParser(_loggerFactory.CreateLogger<ProjectParser>());
            _dependencyGraphBuilder = new DependencyGraphBuilder(_loggerFactory.CreateLogger<DependencyGraphBuilder>());
            _workspaceBuilder = new SolutionWorkspaceBuilder(_loggerFactory.CreateLogger<SolutionWorkspaceBuilder>());
        }

        public async Task<MethodCallGraph> BuildCallGraphAsync(string[] solutionFiles, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("Building call graph for {FileCount} solution files using enhanced analyzer", solutionFiles.Length);

            // Find the solution file
            var solutionFile = FindSolutionFile(solutionFiles);
            if (solutionFile == null)
            {
                _logger.LogWarning("No solution file found, falling back to individual file analysis");
                return await BuildCallGraphFromFilesAsync(solutionFiles, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                // Initialize lazy workspace for much better performance
                await InitializeLazyWorkspaceAsync(solutionFile, cancellationToken).ConfigureAwait(false);

                if (_incrementalCallGraphBuilder != null)
                {
                    _logger.LogInformation("Using high-performance incremental call graph builder");
                    // For full solution analysis, we still need to analyze all files, but incrementally
                    return await _incrementalCallGraphBuilder.BuildCallGraphForMethodsAsync(
                        solutionFiles.SelectMany(f => GetMethodIdsFromFile(f)), 10, cancellationToken).ConfigureAwait(false);
                }
                
                // Fallback to legacy full analysis
                await InitializeWorkspaceAsync(solutionFile, cancellationToken).ConfigureAwait(false);

                if (_callGraphBuilder == null)
                {
                    _logger.LogError("Call graph builder not initialized");
                    throw new InvalidOperationException("Call graph builder not initialized");
                }

                return await _callGraphBuilder.BuildCallGraphAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build call graph using enhanced analyzer, falling back to file-based analysis");
                return await BuildCallGraphFromFilesAsync(solutionFiles, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<IReadOnlyList<string>> GetAffectedMethodsAsync(string[] changedFiles, string[] changedMethods, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("Analyzing impact of {MethodCount} changed methods in {FileCount} files", changedMethods.Length, changedFiles.Length);

            var affectedMethods = new HashSet<string>(changedMethods);
            var callGraph = await BuildCallGraphAsync(changedFiles, cancellationToken).ConfigureAwait(false);

            var queue = new Queue<string>(changedMethods);
            
            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var currentMethod = queue.Dequeue();
                
                foreach (var dependent in callGraph.GetMethodDependents(currentMethod))
                {
                    if (affectedMethods.Add(dependent))
                    {
                        queue.Enqueue(dependent);
                    }
                }
            }

            return affectedMethods.ToList();
        }

        public async Task<SemanticModel> GetSemanticModelAsync(string filePath, CancellationToken cancellationToken = default)
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

        public async Task<IReadOnlyList<TypeUsageInfo>> AnalyzeTypeUsageAsync(string[] sourceFiles, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("Analyzing type usage in {FileCount} source files", sourceFiles.Length);

            var typeUsages = new List<TypeUsageInfo>();

            foreach (var filePath in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(filePath)) continue;

                try
                {
                    var usages = await AnalyzeTypeUsageInFileAsync(filePath, cancellationToken).ConfigureAwait(false);
                    typeUsages.AddRange(usages);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to analyze type usage in file: {FilePath}", filePath);
                }
            }

            return typeUsages;
        }

        public async Task<IReadOnlyList<MethodInfo>> ExtractMethodsFromFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            try
            {
                if (_compilationManager != null)
                {
                    return await ExtractMethodsUsingWorkspaceAsync(filePath, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract methods using workspace, falling back to standalone analysis");
            }

            // Fallback to standalone analysis
            return await ExtractMethodsStandaloneAsync(filePath, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<TestCoverageResult>> FindTestsExercisingMethodAsync(string methodId, string[] solutionFiles, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("Finding tests exercising method: {MethodId} using enhanced analyzer", methodId);
            
            var callGraph = await BuildCallGraphAsync(solutionFiles, cancellationToken).ConfigureAwait(false);
            var results = callGraph.GetTestCoverageForMethod(methodId);
            
            _logger.LogInformation("Found {TestCount} tests exercising method {MethodId} with enhanced accuracy", 
                results.Count, methodId);
            
            return results.ToList();
        }

        private async Task InitializeWorkspaceAsync(string solutionPath, CancellationToken cancellationToken)
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
                
                _callGraphBuilder = new CallGraphBuilderV2(
                    _compilationManager, _symbolResolver, _loggerFactory.CreateLogger<CallGraphBuilderV2>(), _loggerFactory);

                _logger.LogInformation("Solution workspace initialized successfully with {ProjectCount} projects", 
                    solutionInfo.Projects.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize workspace for solution: {SolutionPath}", solutionPath);
                throw;
            }
        }

        private async Task InitializeLazyWorkspaceAsync(string solutionPath, CancellationToken cancellationToken)
        {
            if (_lazyWorkspaceBuilder != null && _symbolIndex != null && _incrementalCallGraphBuilder != null)
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

                // Initialize incremental call graph builder
                // Note: We need a minimal compilation manager for the incremental builder
                // For now, we'll create a lightweight proxy or use the existing one
                if (_compilationManager == null)
                {
                    // Create a lightweight workspace for symbol resolution
                    await InitializeWorkspaceAsync(solutionPath, cancellationToken);
                }

                if (_compilationManager != null && _symbolResolver != null)
                {
                    _incrementalCallGraphBuilder = new IncrementalCallGraphBuilder(
                        _compilationManager, 
                        _symbolResolver, 
                        _symbolIndex,
                        _loggerFactory.CreateLogger<IncrementalCallGraphBuilder>(), 
                        _loggerFactory);
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

        private IEnumerable<string> GetMethodIdsFromFile(string filePath)
        {
            // This is a placeholder - in a real implementation, we'd quickly scan the file
            // for method signatures without full compilation
            if (_symbolIndex != null)
            {
                // Use symbol index to quickly find methods in this file
                return new List<string>(); // Placeholder - would return actual method IDs
            }
            
            return new List<string>();
        }

        private string? FindSolutionFile(string[] files)
        {
            foreach (var file in files)
            {
                if (file.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }

            // Look for solution files in the parent directories of the provided files
            foreach (var file in files)
            {
                var directory = Path.GetDirectoryName(file);
                while (!string.IsNullOrEmpty(directory))
                {
                    var solutionFiles = Directory.GetFiles(directory, "*.sln");
                    if (solutionFiles.Length > 0)
                    {
                        return solutionFiles[0];
                    }
                    directory = Path.GetDirectoryName(directory);
                }
            }

            return null;
        }

        private async Task<MethodCallGraph> BuildCallGraphFromFilesAsync(string[] files, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Fallback to the original file-based approach
            _logger.LogInformation("Building call graph from individual files (fallback mode)");

            // Expand solution files to individual source files
            var sourceFiles = new List<string>();
            foreach (var file in files)
            {
                if (file.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Expanding solution file to source files: {SolutionFile}", file);
                    var solutionSourceFiles = GetSourceFilesFromSolution(file);
                    sourceFiles.AddRange(solutionSourceFiles);
                    _logger.LogInformation("Found {FileCount} source files in solution", solutionSourceFiles.Length);
                }
                else if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && File.Exists(file))
                {
                    sourceFiles.Add(file);
                }
            }

            // Create a single compilation with all files to enable cross-file symbol resolution
            return await BuildCallGraphWithSharedCompilationAsync(sourceFiles.ToArray(), cancellationToken);
        }

        private async Task<MethodCallGraph> BuildCallGraphWithSharedCompilationAsync(string[] sourceFiles, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var callGraph = new Dictionary<string, HashSet<string>>();
            var methodDefinitions = new Dictionary<string, MethodInfo>();

            // Create syntax trees for all files
            var syntaxTrees = new List<Microsoft.CodeAnalysis.SyntaxTree>();
            foreach (var file in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(file)) continue;
                
                var sourceCode = File.ReadAllText(file);
                var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode, path: file);
                syntaxTrees.Add(syntaxTree);
                
                // Add a small async delay to allow cancellation token to be checked more frequently
                if (syntaxTrees.Count % 5 == 0)
                {
                    await Task.Delay(1, cancellationToken);
                }
            }

            if (syntaxTrees.Count == 0)
                return new MethodCallGraph(callGraph, methodDefinitions);

            // Create a single compilation with all syntax trees
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                assemblyName: "SharedAnalysis",
                syntaxTrees: syntaxTrees,
                references: GetBasicReferences()
            );

            // First pass: Extract method definitions from all files
            foreach (var syntaxTree in syntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var root = syntaxTree.GetRoot(cancellationToken);
                var methodDeclarations = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>();

                foreach (var method in methodDeclarations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var symbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
                    if (symbol is not IMethodSymbol methodSymbol) continue;

                    var methodId = GetMethodIdentifier(methodSymbol);
                    var isTest = IsTestMethod(methodSymbol, method);
                    var methodInfo = new MethodInfo(
                        methodId,
                        methodSymbol.Name,
                        methodSymbol.ContainingType.Name,
                        syntaxTree.FilePath ?? "unknown",
                        method.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        isTest
                    );

                    methodDefinitions[methodId] = methodInfo;
                    if (!callGraph.ContainsKey(methodId))
                        callGraph[methodId] = new HashSet<string>();
                }
            }

            // Second pass: Extract method calls using shared semantic models
            foreach (var syntaxTree in syntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var root = syntaxTree.GetRoot(cancellationToken);
                var methodDeclarations = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>();

                foreach (var method in methodDeclarations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var symbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
                    if (symbol is not IMethodSymbol methodSymbol) continue;

                    var callerMethodId = GetMethodIdentifier(methodSymbol);
                    
                    // Extract all types of method calls using the same logic as the enhanced version
                    await ExtractMethodCallsWithSharedSemanticModel(method, semanticModel, callerMethodId, callGraph, methodDefinitions, cancellationToken);
                }
            }

            return new MethodCallGraph(callGraph, methodDefinitions);
        }

        private async Task ExtractMethodCallsWithSharedSemanticModel(
            Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax method,
            SemanticModel semanticModel,
            string callerMethodId,
            Dictionary<string, HashSet<string>> callGraph,
            Dictionary<string, MethodInfo> methodDefinitions,
            CancellationToken cancellationToken)
        {
            // 1. Regular method invocations
            var invocations = method.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>();
            var invocationList = invocations.ToList();
            for (int i = 0; i < invocationList.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var invocation = invocationList[i];
                var invokedSymbol = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
                if (invokedSymbol != null)
                {
                    var calledMethodId = GetMethodIdentifier(invokedSymbol);
                    callGraph[callerMethodId].Add(calledMethodId);
                    
                    // Ensure the called method also exists in the call graph
                    if (!callGraph.ContainsKey(calledMethodId))
                    {
                        callGraph[calledMethodId] = new HashSet<string>();
                    }
                    
                    // Add basic method info for external methods if not already present
                    if (!methodDefinitions.ContainsKey(calledMethodId))
                    {
                        var methodInfo = new MethodInfo(
                            calledMethodId,
                            invokedSymbol.Name,
                            invokedSymbol.ContainingType.Name,
                            "external",
                            0,
                            false
                        );
                        methodDefinitions[calledMethodId] = methodInfo;
                    }
                }
                
                // Add small delay every 10 invocations to allow cancellation
                if (i > 0 && i % 10 == 0)
                {
                    await Task.Delay(1, cancellationToken);
                }
            }

            // 2. Constructor calls (new MyClass())
            var objectCreations = method.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax>();
            foreach (var creation in objectCreations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var constructorSymbol = semanticModel.GetSymbolInfo(creation, cancellationToken).Symbol as IMethodSymbol;
                if (constructorSymbol != null)
                {
                    var calledMethodId = GetMethodIdentifier(constructorSymbol);
                    callGraph[callerMethodId].Add(calledMethodId);
                    
                    if (!callGraph.ContainsKey(calledMethodId))
                        callGraph[calledMethodId] = new HashSet<string>();
                        
                    if (!methodDefinitions.ContainsKey(calledMethodId))
                    {
                        var methodInfo = new MethodInfo(
                            calledMethodId,
                            constructorSymbol.Name,
                            constructorSymbol.ContainingType.Name,
                            "external",
                            0,
                            false
                        );
                        methodDefinitions[calledMethodId] = methodInfo;
                    }
                }
            }

            // 3. Property access (obj.MyProperty)
            var memberAccesses = method.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax>();
            foreach (var memberAccess in memberAccesses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Skip if this member access is part of an invocation (already handled above)
                if (memberAccess.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax)
                    continue;

                var symbolInfo = semanticModel.GetSymbolInfo(memberAccess, cancellationToken);
                var memberSymbol = symbolInfo.Symbol;
                
                if (memberSymbol is IPropertySymbol propertySymbol)
                {
                    var isWriteAccess = IsWriteAccess(memberAccess);
                    
                    if (isWriteAccess && propertySymbol.SetMethod != null)
                    {
                        var calledMethodId = GetMethodIdentifier(propertySymbol.SetMethod);
                        callGraph[callerMethodId].Add(calledMethodId);
                        
                        if (!callGraph.ContainsKey(calledMethodId))
                            callGraph[calledMethodId] = new HashSet<string>();
                            
                        if (!methodDefinitions.ContainsKey(calledMethodId))
                        {
                            var methodInfo = new MethodInfo(
                                calledMethodId,
                                propertySymbol.SetMethod.Name,
                                propertySymbol.SetMethod.ContainingType.Name,
                                "external",
                                0,
                                false
                            );
                            methodDefinitions[calledMethodId] = methodInfo;
                        }
                    }
                    else if (!isWriteAccess && propertySymbol.GetMethod != null)
                    {
                        var calledMethodId = GetMethodIdentifier(propertySymbol.GetMethod);
                        callGraph[callerMethodId].Add(calledMethodId);
                        
                        if (!callGraph.ContainsKey(calledMethodId))
                            callGraph[calledMethodId] = new HashSet<string>();
                            
                        if (!methodDefinitions.ContainsKey(calledMethodId))
                        {
                            var methodInfo = new MethodInfo(
                                calledMethodId,
                                propertySymbol.GetMethod.Name,
                                propertySymbol.GetMethod.ContainingType.Name,
                                "external",
                                0,
                                false
                            );
                            methodDefinitions[calledMethodId] = methodInfo;
                        }
                    }
                }
            }
        }

        private Task<SemanticModel> GetSemanticModelFallbackAsync(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var sourceCode = File.ReadAllText(filePath);
            var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                assemblyName: Path.GetFileNameWithoutExtension(filePath),
                syntaxTrees: new[] { syntaxTree },
                references: GetBasicReferences()
            );

            return Task.FromResult(compilation.GetSemanticModel(syntaxTree));
        }

        private async Task<IReadOnlyList<TypeUsageInfo>> AnalyzeTypeUsageInFileAsync(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var typeUsages = new List<TypeUsageInfo>();

            if (_compilationManager != null)
            {
                var semanticModel = _compilationManager.GetSemanticModel(filePath);
                var syntaxTree = await _compilationManager.GetSyntaxTreeAsync(filePath, cancellationToken);
                
                if (semanticModel != null && syntaxTree != null)
                {
                    var root = syntaxTree.GetRoot(cancellationToken);
                    // Analyze types using the proper semantic model
                    // Implementation similar to the original but with better symbol resolution
                    return await AnalyzeTypeUsageWithSemanticModel(root, semanticModel, filePath, cancellationToken);
                }
            }

            // Fallback implementation
            return await AnalyzeTypeUsageFallbackAsync(filePath, cancellationToken);
        }

        private async Task<IReadOnlyList<MethodInfo>> ExtractMethodsUsingWorkspaceAsync(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var methods = new List<MethodInfo>();
            
            if (_compilationManager == null)
                return methods;

            var syntaxTree = await _compilationManager.GetSyntaxTreeAsync(filePath, cancellationToken);
            var semanticModel = _compilationManager.GetSemanticModel(filePath);
            
            if (syntaxTree == null || semanticModel == null)
                return methods;

            var root = syntaxTree.GetRoot(cancellationToken);
            var methodDeclarations = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>();
            
            foreach (var method in methodDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
                if (symbol is not IMethodSymbol methodSymbol) continue;

                var isTest = IsTestMethod(methodSymbol, method);
                var methodInfo = new MethodInfo(
                    _symbolResolver?.GetFullyQualifiedMethodName(methodSymbol) ?? GetMethodIdentifier(methodSymbol),
                    methodSymbol.Name,
                    methodSymbol.ContainingType.Name,
                    filePath,
                    method.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    isTest
                );

                methods.Add(methodInfo);
            }

            return methods;
        }

        private Task<IReadOnlyList<MethodInfo>> ExtractMethodsStandaloneAsync(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Fallback implementation using standalone compilation
            var methods = new List<MethodInfo>();
            
            var sourceCode = File.ReadAllText(filePath);
            var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            var root = syntaxTree.GetRoot(cancellationToken);

            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                assemblyName: Path.GetFileNameWithoutExtension(filePath),
                syntaxTrees: new[] { syntaxTree },
                references: GetBasicReferences()
            );

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var methodDeclarations = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>();
            
            foreach (var method in methodDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
                if (symbol is not IMethodSymbol methodSymbol) continue;

                var isTest = IsTestMethod(methodSymbol, method);
                var methodInfo = new MethodInfo(
                    GetMethodIdentifier(methodSymbol),
                    methodSymbol.Name,
                    methodSymbol.ContainingType.Name,
                    filePath,
                    method.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    isTest
                );

                methods.Add(methodInfo);
            }

            return Task.FromResult<IReadOnlyList<MethodInfo>>(methods);
        }

        private Task ExtractMethodCallsStandaloneAsync(string filePath, Dictionary<string, HashSet<string>> callGraph, 
            Dictionary<string, MethodInfo> methodDefinitions, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceCode = File.ReadAllText(filePath);
            var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            var root = syntaxTree.GetRoot(cancellationToken);

            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                assemblyName: Path.GetFileNameWithoutExtension(filePath),
                syntaxTrees: new[] { syntaxTree },
                references: GetBasicReferences()
            );

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var methodDeclarations = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>();

            foreach (var method in methodDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
                if (symbol is not IMethodSymbol methodSymbol) continue;

                var callerMethodId = GetMethodIdentifier(methodSymbol);
                
                // Ensure this method exists in the call graph
                if (!callGraph.ContainsKey(callerMethodId))
                {
                    callGraph[callerMethodId] = new HashSet<string>();
                    
                    // If we didn't see this method in the first pass, add it to methodDefinitions too
                    if (!methodDefinitions.ContainsKey(callerMethodId))
                    {
                        var isTest = IsTestMethod(methodSymbol, method);
                        var methodInfo = new MethodInfo(
                            callerMethodId,
                            methodSymbol.Name,
                            methodSymbol.ContainingType.Name,
                            filePath,
                            method.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                            isTest
                        );
                        methodDefinitions[callerMethodId] = methodInfo;
                    }
                }

                // Find all method calls in this method
                
                // 1. Regular method invocations
                var invocations = method.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>();
                foreach (var invocation in invocations)
                {
                    var invokedSymbol = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
                    if (invokedSymbol != null)
                    {
                        var calledMethodId = GetMethodIdentifier(invokedSymbol);
                        callGraph[callerMethodId].Add(calledMethodId);
                        
                        // Ensure the called method also exists in the call graph (for external methods)
                        if (!callGraph.ContainsKey(calledMethodId))
                        {
                            callGraph[calledMethodId] = new HashSet<string>();
                        }
                        
                        // Add basic method info for external methods if not already present
                        if (!methodDefinitions.ContainsKey(calledMethodId))
                        {
                            var methodInfo = new MethodInfo(
                                calledMethodId,
                                invokedSymbol.Name,
                                invokedSymbol.ContainingType.Name,
                                filePath, // We don't know the actual file path for external methods
                                0, // Line number unknown
                                false // External methods are not test methods
                            );
                            methodDefinitions[calledMethodId] = methodInfo;
                        }
                    }
                }

                // 2. Constructor calls (new MyClass())
                var objectCreations = method.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax>();
                foreach (var creation in objectCreations)
                {
                    var constructorSymbol = semanticModel.GetSymbolInfo(creation, cancellationToken).Symbol as IMethodSymbol;
                    if (constructorSymbol != null)
                    {
                        var calledMethodId = GetMethodIdentifier(constructorSymbol);
                        callGraph[callerMethodId].Add(calledMethodId);
                        
                        // Ensure the called method also exists in the call graph
                        if (!callGraph.ContainsKey(calledMethodId))
                        {
                            callGraph[calledMethodId] = new HashSet<string>();
                        }
                        
                        // Add basic method info for constructor if not already present
                        if (!methodDefinitions.ContainsKey(calledMethodId))
                        {
                            var methodInfo = new MethodInfo(
                                calledMethodId,
                                constructorSymbol.Name,
                                constructorSymbol.ContainingType.Name,
                                filePath,
                                0,
                                false
                            );
                            methodDefinitions[calledMethodId] = methodInfo;
                        }
                    }
                }

                // 3. Property access (obj.MyProperty)
                var memberAccesses = method.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax>();
                foreach (var memberAccess in memberAccesses)
                {
                    // Skip if this member access is part of an invocation (already handled above)
                    if (memberAccess.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax)
                        continue;

                    var symbolInfo = semanticModel.GetSymbolInfo(memberAccess, cancellationToken);
                    var memberSymbol = symbolInfo.Symbol;
                    
                    if (memberSymbol is IPropertySymbol propertySymbol)
                    {
                        // Add both getter and setter calls based on usage context
                        var isWriteAccess = IsWriteAccess(memberAccess);
                        
                        if (isWriteAccess && propertySymbol.SetMethod != null)
                        {
                            var calledMethodId = GetMethodIdentifier(propertySymbol.SetMethod);
                            callGraph[callerMethodId].Add(calledMethodId);
                            
                            if (!callGraph.ContainsKey(calledMethodId))
                                callGraph[calledMethodId] = new HashSet<string>();
                                
                            if (!methodDefinitions.ContainsKey(calledMethodId))
                            {
                                var methodInfo = new MethodInfo(
                                    calledMethodId,
                                    propertySymbol.SetMethod.Name,
                                    propertySymbol.SetMethod.ContainingType.Name,
                                    filePath,
                                    0,
                                    false
                                );
                                methodDefinitions[calledMethodId] = methodInfo;
                            }
                        }
                        else if (!isWriteAccess && propertySymbol.GetMethod != null)
                        {
                            var calledMethodId = GetMethodIdentifier(propertySymbol.GetMethod);
                            callGraph[callerMethodId].Add(calledMethodId);
                            
                            if (!callGraph.ContainsKey(calledMethodId))
                                callGraph[calledMethodId] = new HashSet<string>();
                                
                            if (!methodDefinitions.ContainsKey(calledMethodId))
                            {
                                var methodInfo = new MethodInfo(
                                    calledMethodId,
                                    propertySymbol.GetMethod.Name,
                                    propertySymbol.GetMethod.ContainingType.Name,
                                    filePath,
                                    0,
                                    false
                                );
                                methodDefinitions[calledMethodId] = methodInfo;
                            }
                        }
                    }
                }
            }

            return Task.CompletedTask;
        }

        private static bool IsWriteAccess(Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax memberAccess)
        {
            var parent = memberAccess.Parent;

            // Check if it's the left side of an assignment
            if (parent is Microsoft.CodeAnalysis.CSharp.Syntax.AssignmentExpressionSyntax assignment && assignment.Left == memberAccess)
                return true;

            // Check if it's used with ++ or -- operators
            if (parent is Microsoft.CodeAnalysis.CSharp.Syntax.PrefixUnaryExpressionSyntax prefixUnary && 
                (prefixUnary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PreIncrementExpression) || 
                 prefixUnary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PreDecrementExpression)))
                return true;

            if (parent is Microsoft.CodeAnalysis.CSharp.Syntax.PostfixUnaryExpressionSyntax postfixUnary && 
                (postfixUnary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PostIncrementExpression) || 
                 postfixUnary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PostDecrementExpression)))
                return true;

            return false;
        }

        private Task<IReadOnlyList<TypeUsageInfo>> AnalyzeTypeUsageWithSemanticModel(SyntaxNode root, SemanticModel semanticModel, string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var typeUsages = new List<TypeUsageInfo>();

            // Analyze type declarations
            var typeDeclarations = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax>();
            foreach (var typeDecl in typeDeclarations)
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);
                if (typeSymbol == null) continue;

                var usage = new TypeUsageInfo(
                    typeSymbol.Name,
                    typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                    filePath,
                    typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    GetTypeUsageContext(typeDecl)
                );

                typeUsages.Add(usage);
            }

            // Analyze type references
            var identifierNames = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax>();
            foreach (var identifier in identifierNames)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(identifier, cancellationToken);
                if (symbolInfo.Symbol is ITypeSymbol typeSymbol)
                {
                    var usage = new TypeUsageInfo(
                        typeSymbol.Name,
                        typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                        filePath,
                        identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        TypeUsageContext.Reference
                    );

                    typeUsages.Add(usage);
                }
            }

            return Task.FromResult<IReadOnlyList<TypeUsageInfo>>(typeUsages);
        }

        private Task<IReadOnlyList<TypeUsageInfo>> AnalyzeTypeUsageFallbackAsync(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Original implementation as fallback
            var typeUsages = new List<TypeUsageInfo>();

            var sourceCode = File.ReadAllText(filePath);
            var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            var root = syntaxTree.GetRoot(cancellationToken);

            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                assemblyName: Path.GetFileNameWithoutExtension(filePath),
                syntaxTrees: new[] { syntaxTree },
                references: GetBasicReferences()
            );

            var semanticModel = compilation.GetSemanticModel(syntaxTree);

            var typeDeclarations = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax>();
            foreach (var typeDecl in typeDeclarations)
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);
                if (typeSymbol == null) continue;

                var usage = new TypeUsageInfo(
                    typeSymbol.Name,
                    typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                    filePath,
                    typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    GetTypeUsageContext(typeDecl)
                );

                typeUsages.Add(usage);
            }

            return Task.FromResult<IReadOnlyList<TypeUsageInfo>>(typeUsages);
        }

        private static string GetMethodIdentifier(IMethodSymbol methodSymbol)
        {
            return $"{methodSymbol.ContainingType.ToDisplayString()}.{methodSymbol.Name}({string.Join(",", methodSymbol.Parameters.Select(p => p.Type.ToDisplayString()))})";
        }

        private static TypeUsageContext GetTypeUsageContext(Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax typeDecl)
        {
            switch (typeDecl)
            {
                case Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax _:
                case Microsoft.CodeAnalysis.CSharp.Syntax.InterfaceDeclarationSyntax _:
                case Microsoft.CodeAnalysis.CSharp.Syntax.StructDeclarationSyntax _:
                case Microsoft.CodeAnalysis.CSharp.Syntax.EnumDeclarationSyntax _:
                    return TypeUsageContext.Declaration;
                default:
                    return TypeUsageContext.Reference;
            }
        }

        private static bool IsTestMethod(IMethodSymbol methodSymbol, Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax methodSyntax)
        {
            // Primary check: Look for test framework attributes (most accurate)
            var testAttributes = new[] { "Test", "TestMethod", "Fact", "Theory", "TestCase", "DataTestMethod" };
            
            foreach (var attributeList in methodSyntax.AttributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    var attributeName = attribute.Name.ToString();
                    if (testAttributes.Any(ta => attributeName.EndsWith(ta, StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
            }

            // Secondary check: Only for methods in explicitly recognized test projects
            // This prevents false positives in production code
            var filePath = methodSyntax.SyntaxTree.FilePath ?? string.Empty;
            if (IsInTestProject(filePath))
            {
                var methodName = methodSymbol.Name;
                
                // More restrictive name patterns for fallback cases
                // Only match methods that are clearly test methods by naming convention
                if (methodName.StartsWith("Test", StringComparison.OrdinalIgnoreCase) ||
                    methodName.StartsWith("Should", StringComparison.OrdinalIgnoreCase) ||
                    methodName.StartsWith("When", StringComparison.OrdinalIgnoreCase) ||
                    methodName.StartsWith("Given", StringComparison.OrdinalIgnoreCase))
                {
                    // Additional safety: method should be public to be a test
                    return methodSymbol.DeclaredAccessibility == Accessibility.Public;
                }
            }

            return false;
        }
        
        private static bool IsInTestProject(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;
                
            // Check if the file path contains test indicators
            var pathLower = filePath.ToLowerInvariant();
            return pathLower.Contains("/test") || pathLower.Contains("\\test") ||
                   pathLower.Contains("/tests") || pathLower.Contains("\\tests") ||
                   pathLower.Contains(".test.") || pathLower.Contains(".tests.");
        }

        private static System.Collections.Immutable.ImmutableArray<MetadataReference> GetBasicReferences()
        {
            var references = new List<MetadataReference>();
            
            var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (runtimePath != null)
            {
                references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Runtime.dll")));
                references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Collections.dll")));
                references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Linq.dll")));
                references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Threading.Tasks.dll")));
                references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            }

            return ImmutableArray.CreateRange(references);
        }

        private string[] GetSourceFilesFromSolution(string solutionPath)
        {
            try
            {
                var solutionDir = Path.GetDirectoryName(solutionPath) ?? Path.GetDirectoryName(Path.GetFullPath(solutionPath));
                if (string.IsNullOrEmpty(solutionDir))
                {
                    _logger.LogWarning("Could not determine solution directory for: {SolutionPath}", solutionPath);
                    return Array.Empty<string>();
                }

                var sourceFiles = Directory.GetFiles(solutionDir, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("/bin/") && !f.Contains("\\bin\\") && 
                               !f.Contains("/obj/") && !f.Contains("\\obj\\")) // Skip build artifacts
                    .ToArray();

                _logger.LogInformation("Found {FileCount} source files in solution directory: {SolutionDir}", sourceFiles.Length, solutionDir);
                return sourceFiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get source files from solution: {SolutionPath}", solutionPath);
                return Array.Empty<string>();
            }
        }

        public void Dispose()
        {
            _currentWorkspace?.Dispose();
            _currentWorkspace = null;
            _compilationManager = null;
            _symbolResolver = null;
            _callGraphBuilder = null;
        }
    }
}