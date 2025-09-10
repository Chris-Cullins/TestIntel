using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Models;
using TestIntelligence.ImpactAnalyzer.Analysis.Utilities;
using TestIntelligence.ImpactAnalyzer.Analysis.Workspace;
using TestIntelligence.ImpactAnalyzer.Analysis;

namespace TestIntelligence.ImpactAnalyzer.Analysis.CallGraph
{
    public class CallGraphAnalyzer : ICallGraphAnalyzer
    {
        private readonly ILogger<CallGraphAnalyzer> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IWorkspaceManager _workspaceManager;

        // High-performance incremental builder (initialized on demand)
        private IncrementalCallGraphBuilder? _incrementalCallGraphBuilder;

        public CallGraphAnalyzer(
            ILogger<CallGraphAnalyzer> logger, 
            ILoggerFactory loggerFactory,
            IWorkspaceManager workspaceManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
        }

        public async Task<MethodCallGraph> BuildCallGraphAsync(string[] solutionFiles, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("Building call graph for {FileCount} solution files using enhanced analyzer", solutionFiles.Length);

            // Find the solution file
            var solutionFile = RoslynAnalyzerHelper.FindSolutionFile(solutionFiles);
            if (solutionFile == null)
            {
                _logger.LogWarning("No solution file found, falling back to individual file analysis");
                return await BuildCallGraphFromFilesAsync(solutionFiles, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                // Initialize workspace
                await _workspaceManager.InitializeAsync(solutionFile, cancellationToken).ConfigureAwait(false);

                // Try high-performance incremental path when we can seed with changed file methods
                var seedMethodIds = solutionFiles
                    .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && File.Exists(f))
                    .SelectMany(GetMethodIdsFromFile)
                    .Distinct()
                    .ToList();

                if (seedMethodIds.Count > 0)
                {
                    // Initialize incremental builder lazily
                    if (_incrementalCallGraphBuilder == null)
                    {
                        if (_workspaceManager.CompilationManager != null && _workspaceManager.SymbolResolver != null)
                        {
                            var symbolIndex = new SymbolIndex(_loggerFactory.CreateLogger<SymbolIndex>());
                            await symbolIndex.BuildIndexAsync(solutionFile, cancellationToken).ConfigureAwait(false);

                            _incrementalCallGraphBuilder = new IncrementalCallGraphBuilder(
                                _workspaceManager.CompilationManager,
                                _workspaceManager.SymbolResolver,
                                symbolIndex,
                                _loggerFactory.CreateLogger<IncrementalCallGraphBuilder>(),
                                _loggerFactory);
                        }
                    }

                    if (_incrementalCallGraphBuilder != null)
                    {
                        _logger.LogInformation("Using incremental call graph builder with {SeedCount} seed methods", seedMethodIds.Count);
                        return await _incrementalCallGraphBuilder
                            .BuildCallGraphForMethodsAsync(seedMethodIds, 10, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                // Fallback to legacy full analysis
                _logger.LogInformation("Using legacy call graph builder (incremental path not engaged)");

                var callGraphBuilder = CreateCallGraphBuilder();
                if (callGraphBuilder == null)
                {
                    _logger.LogError("Call graph builder not initialized");
                    throw new InvalidOperationException("Call graph builder not initialized");
                }

                return await callGraphBuilder.BuildCallGraphAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build call graph using enhanced analyzer, falling back to file-based analysis");
                return await BuildCallGraphFromFilesAsync(solutionFiles, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<MethodCallGraph> BuildCallGraphFromFilesAsync(string[] files, CancellationToken cancellationToken)
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
                    var solutionSourceFiles = RoslynAnalyzerHelper.GetSourceFilesFromSolution(file);
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

        public async Task<MethodCallGraph> BuildCallGraphWithSharedCompilationAsync(string[] sourceFiles, CancellationToken cancellationToken)
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
                
                var sourceCode = await File.ReadAllTextAsync(file, cancellationToken);
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
                references: RoslynAnalyzerHelper.GetBasicReferences()
            );

            // First pass: Extract method definitions from all files
            await ExtractMethodDefinitions(syntaxTrees, compilation, methodDefinitions, callGraph, cancellationToken);

            // Second pass: Extract method calls using shared semantic models
            await ExtractMethodCalls(syntaxTrees, compilation, callGraph, methodDefinitions, cancellationToken);

            return new MethodCallGraph(callGraph, methodDefinitions);
        }

        private async Task ExtractMethodDefinitions(
            List<Microsoft.CodeAnalysis.SyntaxTree> syntaxTrees,
            Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation,
            Dictionary<string, MethodInfo> methodDefinitions,
            Dictionary<string, HashSet<string>> callGraph,
            CancellationToken cancellationToken)
        {
            foreach (var syntaxTree in syntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var root = await syntaxTree.GetRootAsync(cancellationToken);
                var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

                foreach (var method in methodDeclarations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var symbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
                    if (symbol is not IMethodSymbol methodSymbol) continue;

                    var methodId = RoslynAnalyzerHelper.GetMethodIdentifier(methodSymbol);
                    var isTest = RoslynAnalyzerHelper.IsTestMethod(methodSymbol, method);
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
        }

        private async Task ExtractMethodCalls(
            List<Microsoft.CodeAnalysis.SyntaxTree> syntaxTrees,
            Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation,
            Dictionary<string, HashSet<string>> callGraph,
            Dictionary<string, MethodInfo> methodDefinitions,
            CancellationToken cancellationToken)
        {
            foreach (var syntaxTree in syntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var root = await syntaxTree.GetRootAsync(cancellationToken);
                var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

                foreach (var method in methodDeclarations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var symbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
                    if (symbol is not IMethodSymbol methodSymbol) continue;

                    var callerMethodId = RoslynAnalyzerHelper.GetMethodIdentifier(methodSymbol);
                    
                    // Extract all types of method calls using the same logic as the enhanced version
                    await ExtractMethodCallsWithSharedSemanticModel(method, semanticModel, callerMethodId, callGraph, methodDefinitions, cancellationToken);
                }
            }
        }

        private async Task ExtractMethodCallsWithSharedSemanticModel(
            MethodDeclarationSyntax method,
            SemanticModel semanticModel,
            string callerMethodId,
            Dictionary<string, HashSet<string>> callGraph,
            Dictionary<string, MethodInfo> methodDefinitions,
            CancellationToken cancellationToken)
        {
            // 1. Regular method invocations
            var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>();
            await ProcessInvocations(invocations, semanticModel, callerMethodId, callGraph, methodDefinitions, cancellationToken);

            // 2. Constructor calls (new MyClass())
            var objectCreations = method.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
            await ProcessObjectCreations(objectCreations, semanticModel, callerMethodId, callGraph, methodDefinitions, cancellationToken);

            // 3. Property access (obj.MyProperty)
            var memberAccesses = method.DescendantNodes().OfType<MemberAccessExpressionSyntax>();
            await ProcessMemberAccesses(memberAccesses, semanticModel, callerMethodId, callGraph, methodDefinitions, cancellationToken);
        }

        private async Task ProcessInvocations(
            IEnumerable<InvocationExpressionSyntax> invocations,
            SemanticModel semanticModel,
            string callerMethodId,
            Dictionary<string, HashSet<string>> callGraph,
            Dictionary<string, MethodInfo> methodDefinitions,
            CancellationToken cancellationToken)
        {
            var invocationList = invocations.ToList();
            for (int i = 0; i < invocationList.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var invocation = invocationList[i];
                var invokedSymbol = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
                if (invokedSymbol != null)
                {
                    var calledMethodId = RoslynAnalyzerHelper.GetMethodIdentifier(invokedSymbol);
                    AddMethodCall(callerMethodId, calledMethodId, invokedSymbol, callGraph, methodDefinitions);
                }
                
                // Add small delay every 10 invocations to allow cancellation
                if (i > 0 && i % 10 == 0)
                {
                    await Task.Delay(1, cancellationToken);
                }
            }
        }

        private Task ProcessObjectCreations(
            IEnumerable<ObjectCreationExpressionSyntax> objectCreations,
            SemanticModel semanticModel,
            string callerMethodId,
            Dictionary<string, HashSet<string>> callGraph,
            Dictionary<string, MethodInfo> methodDefinitions,
            CancellationToken cancellationToken)
        {
            foreach (var creation in objectCreations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var constructorSymbol = semanticModel.GetSymbolInfo(creation, cancellationToken).Symbol as IMethodSymbol;
                if (constructorSymbol != null)
                {
                    var calledMethodId = RoslynAnalyzerHelper.GetMethodIdentifier(constructorSymbol);
                    AddMethodCall(callerMethodId, calledMethodId, constructorSymbol, callGraph, methodDefinitions);
                }
            }

            return Task.CompletedTask;
        }

        private Task ProcessMemberAccesses(
            IEnumerable<MemberAccessExpressionSyntax> memberAccesses,
            SemanticModel semanticModel,
            string callerMethodId,
            Dictionary<string, HashSet<string>> callGraph,
            Dictionary<string, MethodInfo> methodDefinitions,
            CancellationToken cancellationToken)
        {
            foreach (var memberAccess in memberAccesses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Skip if this member access is part of an invocation (already handled above)
                if (memberAccess.Parent is InvocationExpressionSyntax)
                    continue;

                var symbolInfo = semanticModel.GetSymbolInfo(memberAccess, cancellationToken);
                var memberSymbol = symbolInfo.Symbol;
                
                if (memberSymbol is IPropertySymbol propertySymbol)
                {
                    ProcessPropertyAccess(memberAccess, propertySymbol, callerMethodId, callGraph, methodDefinitions);
                }
            }

            return Task.CompletedTask;
        }

        private void ProcessPropertyAccess(
            MemberAccessExpressionSyntax memberAccess,
            IPropertySymbol propertySymbol,
            string callerMethodId,
            Dictionary<string, HashSet<string>> callGraph,
            Dictionary<string, MethodInfo> methodDefinitions)
        {
            var isWriteAccess = RoslynAnalyzerHelper.IsWriteAccess(memberAccess);
            
            if (isWriteAccess && propertySymbol.SetMethod != null)
            {
                var calledMethodId = RoslynAnalyzerHelper.GetMethodIdentifier(propertySymbol.SetMethod);
                AddMethodCall(callerMethodId, calledMethodId, propertySymbol.SetMethod, callGraph, methodDefinitions);
            }
            else if (!isWriteAccess && propertySymbol.GetMethod != null)
            {
                var calledMethodId = RoslynAnalyzerHelper.GetMethodIdentifier(propertySymbol.GetMethod);
                AddMethodCall(callerMethodId, calledMethodId, propertySymbol.GetMethod, callGraph, methodDefinitions);
            }
        }

        private void AddMethodCall(
            string callerMethodId,
            string calledMethodId,
            IMethodSymbol calledMethodSymbol,
            Dictionary<string, HashSet<string>> callGraph,
            Dictionary<string, MethodInfo> methodDefinitions)
        {
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
                    calledMethodSymbol.Name,
                    calledMethodSymbol.ContainingType.Name,
                    "external",
                    0,
                    false
                );
                methodDefinitions[calledMethodId] = methodInfo;
            }
        }

        private CallGraphBuilderV2? CreateCallGraphBuilder()
        {
            if (_workspaceManager.CompilationManager != null && _workspaceManager.SymbolResolver != null)
            {
                return new CallGraphBuilderV2(
                    _workspaceManager.CompilationManager,
                    _workspaceManager.SymbolResolver,
                    _loggerFactory.CreateLogger<CallGraphBuilderV2>(),
                    _loggerFactory);
            }
            return null;
        }

        private IEnumerable<string> GetMethodIdsFromFile(string filePath)
        {
            var results = new HashSet<string>();
            try
            {
                if (!File.Exists(filePath))
                    return results;

                // Prefer workspace semantic model + symbol resolver for exact identifier matching
                if (_workspaceManager.CompilationManager != null && _workspaceManager.SymbolResolver != null)
                {
                    var syntaxTree = _workspaceManager.CompilationManager.GetSyntaxTreeAsync(filePath, CancellationToken.None).GetAwaiter().GetResult();
                    var semanticModel = _workspaceManager.CompilationManager.GetSemanticModel(filePath);
                    if (syntaxTree != null && semanticModel != null)
                    {
                        var root = syntaxTree.GetRoot();

                        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                        {
                            var symbol = semanticModel.GetDeclaredSymbol(method);
                            if (symbol is IMethodSymbol methodSymbol)
                            {
                                var id = _workspaceManager.SymbolResolver.GetFullyQualifiedMethodName(methodSymbol);
                                if (!string.IsNullOrEmpty(id)) results.Add(id);
                            }
                        }

                        foreach (var ctor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
                        {
                            var symbol = semanticModel.GetDeclaredSymbol(ctor);
                            if (symbol is IMethodSymbol ctorSymbol)
                            {
                                var id = _workspaceManager.SymbolResolver.GetFullyQualifiedMethodName(ctorSymbol);
                                if (!string.IsNullOrEmpty(id)) results.Add(id);
                            }
                        }

                        return results;
                    }
                }

                // Fallback: lightweight single-file compilation
                var sourceCode = File.ReadAllText(filePath);
                var fallbackTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
                var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                    assemblyName: Path.GetFileNameWithoutExtension(filePath),
                    syntaxTrees: new[] { fallbackTree },
                    references: RoslynAnalyzerHelper.GetBasicReferences()
                );

                var fallbackModel = compilation.GetSemanticModel(fallbackTree);
                var fallbackRoot = fallbackTree.GetRoot();

                foreach (var method in fallbackRoot.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    var symbol = fallbackModel.GetDeclaredSymbol(method);
                    if (symbol is IMethodSymbol methodSymbol)
                    {
                        var id = RoslynAnalyzerHelper.GetMethodIdentifier(methodSymbol);
                        if (!string.IsNullOrEmpty(id)) results.Add(id);
                    }
                }

                foreach (var ctor in fallbackRoot.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
                {
                    var symbol = fallbackModel.GetDeclaredSymbol(ctor);
                    if (symbol is IMethodSymbol ctorSymbol)
                    {
                        var id = RoslynAnalyzerHelper.GetMethodIdentifier(ctorSymbol);
                        if (!string.IsNullOrEmpty(id)) results.Add(id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to extract method IDs from file: {FilePath}", filePath);
            }

            return results;
        }
    }
}
