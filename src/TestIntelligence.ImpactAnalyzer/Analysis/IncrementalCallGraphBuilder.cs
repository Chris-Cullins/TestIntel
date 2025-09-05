using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Models;
using TestIntelligence.ImpactAnalyzer.Models;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    /// <summary>
    /// High-performance incremental call graph builder that only analyzes code relevant to specific queries.
    /// This avoids the expensive upfront analysis of entire solutions.
    /// </summary>
    public class IncrementalCallGraphBuilder
    {
        private readonly ICompilationManager _compilationManager;
        private readonly ISymbolResolutionEngine _symbolResolver;
        private readonly ISymbolIndex _symbolIndex;
        private readonly ILogger<IncrementalCallGraphBuilder> _logger;
        private readonly ILoggerFactory _loggerFactory;
        
        // Cache for method-specific call graphs to avoid rebuilding
        private readonly ConcurrentDictionary<string, MethodCallGraph> _methodSubgraphs = new();
        
        // Cache for file analysis results
        private readonly ConcurrentDictionary<string, FileAnalysisResult> _fileAnalysisCache = new();

        public IncrementalCallGraphBuilder(
            ICompilationManager compilationManager, 
            ISymbolResolutionEngine symbolResolver,
            ISymbolIndex symbolIndex,
            ILogger<IncrementalCallGraphBuilder> logger,
            ILoggerFactory loggerFactory)
        {
            _compilationManager = compilationManager ?? throw new ArgumentNullException(nameof(compilationManager));
            _symbolResolver = symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver));
            _symbolIndex = symbolIndex ?? throw new ArgumentNullException(nameof(symbolIndex));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        /// <summary>
        /// Builds a focused call graph for a specific method, analyzing only relevant files.
        /// This is dramatically faster than BuildCallGraphAsync() for targeted queries.
        /// </summary>
        public async Task<MethodCallGraph> BuildCallGraphForMethodAsync(string targetMethodId, int maxDepth = 5, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Building incremental call graph for method: {MethodId} (max depth: {MaxDepth})", targetMethodId, maxDepth);
            var startTime = DateTime.UtcNow;

            // Check cache first
            if (_methodSubgraphs.TryGetValue($"{targetMethodId}:{maxDepth}", out var cachedGraph))
            {
                _logger.LogDebug("Returning cached call graph for method: {MethodId}", targetMethodId);
                return cachedGraph;
            }

            try
            {
                // Step 1: Find files containing the target method (fast index lookup)
                var targetFiles = await _symbolIndex.FindFilesContainingMethodAsync(targetMethodId, cancellationToken);
                if (!targetFiles.Any())
                {
                    _logger.LogWarning("Target method {MethodId} not found in symbol index", targetMethodId);
                    return new MethodCallGraph(new Dictionary<string, HashSet<string>>(), new Dictionary<string, MethodInfo>());
                }

                _logger.LogDebug("Found target method in {FileCount} files: {Files}", targetFiles.Count, string.Join(", ", targetFiles.Take(3)));

                // Step 2: Build initial call graph from target files
                var focusedGraph = await BuildPartialCallGraphAsync(targetFiles, cancellationToken);

                // Step 3: Expand incrementally based on discovered dependencies
                var expandedGraph = await ExpandCallGraphIncrementallyAsync(focusedGraph, targetMethodId, maxDepth, cancellationToken);

                // Cache the result
                var cacheKey = $"{targetMethodId}:{maxDepth}";
                _methodSubgraphs[cacheKey] = expandedGraph;

                var elapsed = DateTime.UtcNow - startTime;
                _logger.LogInformation("Incremental call graph built in {ElapsedMs}ms: {MethodCount} methods, {CallCount} calls for target {MethodId}", 
                    elapsed.TotalMilliseconds, 
                    expandedGraph.MethodDefinitions.Count, 
                    expandedGraph.CallGraph.Values.Sum(calls => calls.Count),
                    targetMethodId);

                return expandedGraph;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build incremental call graph for method: {MethodId}", targetMethodId);
                
                // Return empty graph instead of throwing
                return new MethodCallGraph(new Dictionary<string, HashSet<string>>(), new Dictionary<string, MethodInfo>());
            }
        }

        /// <summary>
        /// Builds call graph for multiple methods efficiently using batch processing
        /// </summary>
        public async Task<MethodCallGraph> BuildCallGraphForMethodsAsync(IEnumerable<string> targetMethodIds, int maxDepth = 5, CancellationToken cancellationToken = default)
        {
            var methodIdList = targetMethodIds.ToList();
            _logger.LogInformation("Building incremental call graph for {MethodCount} methods", methodIdList.Count);

            // Find all files containing any of the target methods
            var allTargetFiles = new HashSet<string>();
            var methodToFiles = new Dictionary<string, List<string>>();

            foreach (var methodId in methodIdList)
            {
                var files = await _symbolIndex.FindFilesContainingMethodAsync(methodId, cancellationToken);
                methodToFiles[methodId] = files.ToList();
                foreach (var file in files)
                {
                    allTargetFiles.Add(file);
                }
            }

            _logger.LogDebug("Found {FileCount} unique files containing target methods", allTargetFiles.Count);

            // Build partial call graph for all relevant files
            var combinedGraph = await BuildPartialCallGraphAsync(allTargetFiles, cancellationToken);

            // Expand for each target method and combine results
            var finalCallGraph = new Dictionary<string, HashSet<string>>();
            var finalMethodDefinitions = new Dictionary<string, MethodInfo>();

            foreach (var methodId in methodIdList)
            {
                var expandedGraph = await ExpandCallGraphIncrementallyAsync(combinedGraph, methodId, maxDepth, cancellationToken);
                
                // Merge results
                foreach (var kvp in expandedGraph.CallGraph)
                {
                    if (!finalCallGraph.ContainsKey(kvp.Key))
                        finalCallGraph[kvp.Key] = new HashSet<string>();
                    
                    foreach (var call in kvp.Value)
                        finalCallGraph[kvp.Key].Add(call);
                }

                foreach (var kvp in expandedGraph.MethodDefinitions)
                {
                    finalMethodDefinitions[kvp.Key] = kvp.Value;
                }
            }

            return new MethodCallGraph(finalCallGraph, finalMethodDefinitions);
        }

        /// <summary>
        /// Builds a partial call graph from specific files only
        /// </summary>
        private async Task<MethodCallGraph> BuildPartialCallGraphAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken)
        {
            var callGraph = new ConcurrentDictionary<string, HashSet<string>>();
            var methodDefinitions = new ConcurrentDictionary<string, MethodInfo>();

            // Process files in parallel, but with controlled concurrency to avoid overwhelming the system
            var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2);
            var tasks = filePaths.Select(async filePath =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    await ProcessFileIncrementalAsync(filePath, callGraph, methodDefinitions, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            // Convert to regular collections
            var finalCallGraph = callGraph.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            var finalMethodDefinitions = methodDefinitions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            _logger.LogDebug("Built partial call graph from {FileCount} files: {MethodCount} methods, {CallCount} calls",
                filePaths.Count(), finalMethodDefinitions.Count, finalCallGraph.Values.Sum(calls => calls.Count));

            return new MethodCallGraph(finalCallGraph, finalMethodDefinitions);
        }

        /// <summary>
        /// Expands call graph incrementally around a target method
        /// </summary>
        private async Task<MethodCallGraph> ExpandCallGraphIncrementallyAsync(
            MethodCallGraph baseGraph, 
            string targetMethodId, 
            int maxDepth, 
            CancellationToken cancellationToken)
        {
            var expandedCallGraph = new Dictionary<string, HashSet<string>>();
            var expandedMethodDefinitions = new Dictionary<string, MethodInfo>();
            var visitedMethods = new HashSet<string>();
            var methodsToExpand = new Queue<(string methodId, int depth)>();

            // Start with the target method
            methodsToExpand.Enqueue((targetMethodId, 0));

            while (methodsToExpand.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                var (currentMethodId, depth) = methodsToExpand.Dequeue();
                
                if (depth > maxDepth || visitedMethods.Contains(currentMethodId))
                    continue;

                visitedMethods.Add(currentMethodId);

                // Add method definition if available in base graph
                if (baseGraph.MethodDefinitions.TryGetValue(currentMethodId, out var methodInfo))
                {
                    expandedMethodDefinitions[currentMethodId] = methodInfo;
                }

                // Add calls from this method
                if (baseGraph.CallGraph.TryGetValue(currentMethodId, out var calls))
                {
                    if (!expandedCallGraph.ContainsKey(currentMethodId))
                        expandedCallGraph[currentMethodId] = new HashSet<string>();

                    foreach (var call in calls)
                    {
                        expandedCallGraph[currentMethodId].Add(call);
                        
                        // Queue called method for expansion if within depth limit
                        if (depth < maxDepth)
                        {
                            methodsToExpand.Enqueue((call, depth + 1));
                        }
                    }
                }

                // Find methods that call this one (reverse lookup)
                var dependents = GetMethodDependents(baseGraph, currentMethodId);
                foreach (var dependent in dependents)
                {
                    if (!expandedCallGraph.ContainsKey(dependent))
                        expandedCallGraph[dependent] = new HashSet<string>();
                    
                    expandedCallGraph[dependent].Add(currentMethodId);

                    // Queue dependent for expansion if within depth limit  
                    if (depth < maxDepth)
                    {
                        methodsToExpand.Enqueue((dependent, depth + 1));
                    }
                }

                // If we don't have enough information in the base graph, fetch additional files
                if (!baseGraph.MethodDefinitions.ContainsKey(currentMethodId) && depth < maxDepth)
                {
                    await ExpandWithAdditionalFilesAsync(currentMethodId, expandedCallGraph, expandedMethodDefinitions, cancellationToken);
                }
            }

            _logger.LogDebug("Expanded call graph for {MethodId}: {MethodCount} methods visited", 
                targetMethodId, visitedMethods.Count);

            return new MethodCallGraph(expandedCallGraph, expandedMethodDefinitions);
        }

        /// <summary>
        /// Finds additional files that might contain relevant methods and analyzes them
        /// </summary>
        private async Task ExpandWithAdditionalFilesAsync(
            string methodId, 
            Dictionary<string, HashSet<string>> callGraph,
            Dictionary<string, MethodInfo> methodDefinitions,
            CancellationToken cancellationToken)
        {
            try
            {
                // Find additional files that might contain this method or its dependencies
                var additionalFiles = await _symbolIndex.FindFilesContainingMethodAsync(methodId, cancellationToken);
                
                foreach (var filePath in additionalFiles)
                {
                    if (_fileAnalysisCache.ContainsKey(filePath))
                        continue; // Already processed

                    var tempCallGraph = new ConcurrentDictionary<string, HashSet<string>>();
                    var tempMethodDefinitions = new ConcurrentDictionary<string, MethodInfo>();

                    await ProcessFileIncrementalAsync(filePath, tempCallGraph, tempMethodDefinitions, cancellationToken);

                    // Merge results
                    foreach (var kvp in tempCallGraph)
                    {
                        if (!callGraph.ContainsKey(kvp.Key))
                            callGraph[kvp.Key] = new HashSet<string>();
                        
                        foreach (var call in kvp.Value)
                            callGraph[kvp.Key].Add(call);
                    }

                    foreach (var kvp in tempMethodDefinitions)
                    {
                        methodDefinitions[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to expand with additional files for method: {MethodId}", methodId);
            }
        }

        /// <summary>
        /// Processes a single file with caching to avoid redundant analysis
        /// </summary>
        private async Task ProcessFileIncrementalAsync(
            string filePath, 
            ConcurrentDictionary<string, HashSet<string>> callGraph,
            ConcurrentDictionary<string, MethodInfo> methodDefinitions,
            CancellationToken cancellationToken)
        {
            // Check if we've already processed this file
            if (_fileAnalysisCache.TryGetValue(filePath, out var cachedResult))
            {
                // Merge cached results
                foreach (var kvp in cachedResult.CallGraph)
                {
                    callGraph.TryAdd(kvp.Key, kvp.Value);
                }
                
                foreach (var kvp in cachedResult.MethodDefinitions)
                {
                    methodDefinitions.TryAdd(kvp.Key, kvp.Value);
                }
                return;
            }

            try
            {
                var syntaxTree = await _compilationManager.GetSyntaxTreeAsync(filePath, cancellationToken);
                if (syntaxTree == null)
                {
                    _logger.LogDebug("Could not get syntax tree for file: {FilePath}", filePath);
                    return;
                }

                var semanticModel = _compilationManager.GetSemanticModel(filePath);
                if (semanticModel == null)
                {
                    _logger.LogDebug("Could not get semantic model for file: {FilePath}", filePath);
                    return;
                }

                var root = syntaxTree.GetRoot(cancellationToken);
                var fileCallGraph = new Dictionary<string, HashSet<string>>();
                var fileMethodDefinitions = new Dictionary<string, MethodInfo>();

                // Extract method declarations
                var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
                foreach (var method in methodDeclarations)
                {
                    try
                    {
                        await ProcessMethodDeclarationIncrementalAsync(method, semanticModel, filePath, fileCallGraph, fileMethodDefinitions, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to process method declaration in {FilePath}", filePath);
                    }
                }

                // Cache the results
                var analysisResult = new FileAnalysisResult(fileCallGraph, fileMethodDefinitions);
                _fileAnalysisCache[filePath] = analysisResult;

                // Merge with concurrent collections
                foreach (var kvp in fileCallGraph)
                {
                    callGraph.TryAdd(kvp.Key, kvp.Value);
                }
                
                foreach (var kvp in fileMethodDefinitions)
                {
                    methodDefinitions.TryAdd(kvp.Key, kvp.Value);
                }

                _logger.LogTrace("Processed file incrementally: {FilePath} ({MethodCount} methods)", filePath, fileMethodDefinitions.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process file incrementally: {FilePath}", filePath);
            }
        }

        private Task ProcessMethodDeclarationIncrementalAsync(
            MethodDeclarationSyntax method, 
            SemanticModel semanticModel, 
            string filePath,
            Dictionary<string, HashSet<string>> callGraph,
            Dictionary<string, MethodInfo> methodDefinitions,
            CancellationToken cancellationToken)
        {
            var methodSymbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
            if (methodSymbol == null)
                return Task.CompletedTask;

            var methodId = _symbolResolver.GetFullyQualifiedMethodName(methodSymbol);
            var isTest = IsTestMethod(methodSymbol, method);
            
            var methodInfo = new MethodInfo(
                methodId,
                methodSymbol.Name,
                methodSymbol.ContainingType.Name,
                filePath,
                method.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                isTest
            );

            methodDefinitions[methodId] = methodInfo;

            // Initialize call list
            if (!callGraph.ContainsKey(methodId))
            {
                callGraph[methodId] = new HashSet<string>();
            }

            // Find method calls using optimized visitor
            var visitor = new EnhancedMethodCallVisitor(semanticModel, _compilationManager, _symbolResolver, 
                _loggerFactory.CreateLogger<EnhancedMethodCallVisitor>(), filePath);
            
            visitor.Visit(method);

            // Add discovered calls
            foreach (var methodCall in visitor.MethodCalls)
            {
                callGraph[methodId].Add(methodCall.CalledMethodId);
            }
            
            return Task.CompletedTask;
        }

        private static List<string> GetMethodDependents(MethodCallGraph callGraph, string targetMethodId)
        {
            var dependents = new List<string>();
            
            foreach (var kvp in callGraph.CallGraph)
            {
                if (kvp.Value.Contains(targetMethodId))
                {
                    dependents.Add(kvp.Key);
                }
            }
            
            return dependents;
        }

        private static bool IsTestMethod(IMethodSymbol methodSymbol, MethodDeclarationSyntax methodSyntax)
        {
            // Check for test attributes
            var testAttributes = new[] { "Test", "TestMethod", "Fact", "Theory" };
            
            foreach (var attributeList in methodSyntax.AttributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    var attributeName = attribute.Name.ToString();
                    if (testAttributes.Any(ta => attributeName.EndsWith(ta, StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
            }

            // Check method name patterns
            var methodName = methodSymbol.Name;
            return methodName.EndsWith("Test", StringComparison.OrdinalIgnoreCase) ||
                   methodName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Clears caches to free memory
        /// </summary>
        public void ClearCaches()
        {
            _methodSubgraphs.Clear();
            _fileAnalysisCache.Clear();
            _logger.LogInformation("Incremental call graph caches cleared");
        }

        private class FileAnalysisResult
        {
            public Dictionary<string, HashSet<string>> CallGraph { get; }
            public Dictionary<string, MethodInfo> MethodDefinitions { get; }

            public FileAnalysisResult(Dictionary<string, HashSet<string>> callGraph, Dictionary<string, MethodInfo> methodDefinitions)
            {
                CallGraph = callGraph;
                MethodDefinitions = methodDefinitions;
            }
        }
    }
}