using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    public class CallGraphBuilderV2
    {
        private readonly ICompilationManager _compilationManager;
        private readonly SymbolResolutionEngine _symbolResolver;
        private readonly ILogger<CallGraphBuilderV2> _logger;
        private readonly ILoggerFactory _loggerFactory;

        public CallGraphBuilderV2(ICompilationManager compilationManager, SymbolResolutionEngine symbolResolver, ILogger<CallGraphBuilderV2> logger, ILoggerFactory loggerFactory)
        {
            _compilationManager = compilationManager ?? throw new ArgumentNullException(nameof(compilationManager));
            _symbolResolver = symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        public async Task<MethodCallGraph> BuildCallGraphAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Building enhanced call graph with cross-project support");

            var allSourceFiles = _compilationManager.GetAllSourceFiles();
            _logger.LogInformation("Analyzing {FileCount} source files across all projects", allSourceFiles.Count);

            var callGraph = new ConcurrentDictionary<string, HashSet<string>>();
            var methodDefinitions = new ConcurrentDictionary<string, MethodInfo>();

            // Process files in parallel for better performance
            var tasks = allSourceFiles.Select(async filePath =>
            {
                await ProcessFileAsync(filePath, callGraph, methodDefinitions, cancellationToken);
            });

            await Task.WhenAll(tasks);

            // Convert concurrent collections to regular collections
            var finalCallGraph = callGraph.ToDictionary(
                kvp => kvp.Key, 
                kvp => kvp.Value
            );

            var finalMethodDefinitions = methodDefinitions.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value
            );

            _logger.LogInformation("Call graph built successfully: {MethodCount} methods, {CallCount} total calls", 
                finalMethodDefinitions.Count, finalCallGraph.Values.Sum(calls => calls.Count));

            return new MethodCallGraph(finalCallGraph, finalMethodDefinitions);
        }

        public async Task<MethodCallGraph> BuildCallGraphForMethodAsync(string targetMethodId, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Building focused call graph for method: {MethodId}", targetMethodId);

            var callGraph = new Dictionary<string, HashSet<string>>();
            var methodDefinitions = new Dictionary<string, MethodInfo>();
            var visitedMethods = new HashSet<string>();

            // Build reverse call graph to find what calls the target method
            var fullCallGraph = await BuildCallGraphAsync(cancellationToken);
            await BuildFocusedCallGraphRecursive(targetMethodId, fullCallGraph, callGraph, methodDefinitions, visitedMethods, 0, 10);

            _logger.LogInformation("Focused call graph built: {MethodCount} methods analyzed for {TargetMethod}", 
                methodDefinitions.Count, targetMethodId);

            return new MethodCallGraph(callGraph, methodDefinitions);
        }

        private async Task ProcessFileAsync(string filePath, ConcurrentDictionary<string, HashSet<string>> callGraph, 
            ConcurrentDictionary<string, MethodInfo> methodDefinitions, CancellationToken cancellationToken)
        {
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

                // Extract method definitions first
                var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
                foreach (var method in methodDeclarations)
                {
                    try
                    {
                        await ProcessMethodDeclarationAsync(method, semanticModel, filePath, callGraph, methodDefinitions, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to process method declaration in {FilePath} at line {Line}", 
                            filePath, method.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
                    }
                }

                _logger.LogTrace("Processed file: {FilePath} with {MethodCount} methods", filePath, methodDeclarations.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process file: {FilePath}", filePath);
            }
        }

        private async Task ProcessMethodDeclarationAsync(MethodDeclarationSyntax method, SemanticModel semanticModel, 
            string filePath, ConcurrentDictionary<string, HashSet<string>> callGraph, 
            ConcurrentDictionary<string, MethodInfo> methodDefinitions, CancellationToken cancellationToken)
        {
            var methodSymbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
            if (methodSymbol == null)
                return;

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

            methodDefinitions.TryAdd(methodId, methodInfo);

            // Initialize call list for this method
            if (!callGraph.ContainsKey(methodId))
            {
                callGraph[methodId] = new HashSet<string>();
            }

            // Use enhanced visitor to find all method calls
            var visitor = new EnhancedMethodCallVisitor(semanticModel, _compilationManager, _symbolResolver, 
                _loggerFactory.CreateLogger<EnhancedMethodCallVisitor>(), filePath);
            
            visitor.Visit(method);

            // Add all discovered calls to the call graph
            foreach (var methodCall in visitor.MethodCalls)
            {
                callGraph[methodId].Add(methodCall.CalledMethodId);

                // Debug logging for specific methods we're interested in tracking
                if (methodSymbol.Name == "ToString" || methodCall.CalledMethodId.Contains("ToString"))
                {
                    _logger.LogDebug("Method {CallerMethod} calls {CalledMethod} via {CallType} at line {LineNumber}", 
                        methodId, methodCall.CalledMethodId, methodCall.CallType, methodCall.LineNumber);
                }
            }

            // Handle interface implementations and virtual method overrides
            await HandlePolymorphicCallsAsync(methodSymbol, methodId, callGraph);
        }

        private Task HandlePolymorphicCallsAsync(IMethodSymbol methodSymbol, string methodId, 
            ConcurrentDictionary<string, HashSet<string>> callGraph)
        {
            try
            {
                // Handle interface implementations
                if (methodSymbol.ContainingType.TypeKind == TypeKind.Interface)
                {
                    var implementations = _symbolResolver.ResolveInterfaceImplementations(methodSymbol);
                    foreach (var implementation in implementations)
                    {
                        var implId = _symbolResolver.GetFullyQualifiedMethodName(implementation);
                        
                        // Add bidirectional relationship for interface calls
                        if (!callGraph.ContainsKey(implId))
                            callGraph[implId] = new HashSet<string>();
                        
                        callGraph[implId].Add(methodId); // Implementation "calls" interface method conceptually
                    }
                }

                // Handle virtual method overrides
                if (methodSymbol.IsVirtual || methodSymbol.IsOverride || methodSymbol.IsAbstract)
                {
                    var overrides = _symbolResolver.ResolveVirtualOverrides(methodSymbol);
                    foreach (var override_ in overrides)
                    {
                        var overrideId = _symbolResolver.GetFullyQualifiedMethodName(override_);
                        
                        // Add relationship between base and derived methods
                        if (!callGraph.ContainsKey(overrideId))
                            callGraph[overrideId] = new HashSet<string>();
                        
                        callGraph[overrideId].Add(methodId); // Override "calls" base method conceptually
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to handle polymorphic calls for method: {MethodId}", methodId);
            }
            
            return Task.CompletedTask;
        }

        private async Task BuildFocusedCallGraphRecursive(string methodId, MethodCallGraph fullCallGraph, 
            Dictionary<string, HashSet<string>> focusedCallGraph, Dictionary<string, MethodInfo> methodDefinitions,
            HashSet<string> visitedMethods, int depth, int maxDepth)
        {
            if (depth > maxDepth || visitedMethods.Contains(methodId))
                return;

            visitedMethods.Add(methodId);

            // Add method definition if available
            var methodInfo = fullCallGraph.GetMethodInfo(methodId);
            if (methodInfo != null)
            {
                methodDefinitions[methodId] = methodInfo;
            }

            // Get all methods that depend on this method (reverse lookup)
            var dependents = fullCallGraph.GetMethodDependents(methodId);
            if (dependents.Any())
            {
                foreach (var dependent in dependents)
                {
                    if (!focusedCallGraph.ContainsKey(dependent))
                        focusedCallGraph[dependent] = new HashSet<string>();
                    
                    focusedCallGraph[dependent].Add(methodId);

                    // Recursively build for dependents
                    await BuildFocusedCallGraphRecursive(dependent, fullCallGraph, focusedCallGraph, methodDefinitions, visitedMethods, depth + 1, maxDepth);
                }
            }

            // Also include what this method calls (forward lookup)
            var calls = fullCallGraph.GetMethodCalls(methodId);
            if (calls.Any())
            {
                if (!focusedCallGraph.ContainsKey(methodId))
                    focusedCallGraph[methodId] = new HashSet<string>();
                
                foreach (var call in calls)
                {
                    focusedCallGraph[methodId].Add(call);
                    
                    // Add called method definition
                    var calledMethodInfo = fullCallGraph.GetMethodInfo(call);
                    if (calledMethodInfo != null)
                    {
                        methodDefinitions[call] = calledMethodInfo;
                    }
                }
            }
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
            if (methodName.EndsWith("Test", StringComparison.OrdinalIgnoreCase) ||
                methodName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase))
                return true;

            // Check if containing type is in a test assembly/project
            var containingType = methodSymbol.ContainingType;
            var typeName = containingType.Name;
            if (typeName.EndsWith("Test", StringComparison.OrdinalIgnoreCase) ||
                typeName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
    }
}