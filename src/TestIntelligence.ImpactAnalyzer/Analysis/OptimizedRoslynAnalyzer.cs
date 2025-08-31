using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Models;
using TestIntelligence.ImpactAnalyzer.Caching;
using TestIntelligence.ImpactAnalyzer.Models;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public class OptimizedRoslynAnalyzer : IRoslynAnalyzer, IDisposable
    {
        private readonly ILogger<OptimizedRoslynAnalyzer> _logger;
        private readonly IEnhancedCompilationCache _compilationCache;
        private readonly ConcurrentDictionary<string, Task<SyntaxTree>> _syntaxTreeCache;
        private readonly Lazy<ImmutableArray<MetadataReference>> _basicReferences;
        private readonly SemaphoreSlim _parallelismSemaphore;

        public OptimizedRoslynAnalyzer(
            ILogger<OptimizedRoslynAnalyzer> logger,
            IEnhancedCompilationCache compilationCache,
            int maxParallelism = 4)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _compilationCache = compilationCache ?? throw new ArgumentNullException(nameof(compilationCache));
            _syntaxTreeCache = new ConcurrentDictionary<string, Task<SyntaxTree>>();
            _basicReferences = new Lazy<ImmutableArray<MetadataReference>>(CreateBasicReferences);
            _parallelismSemaphore = new SemaphoreSlim(maxParallelism, maxParallelism);
        }

        public async Task<MethodCallGraph> BuildCallGraphAsync(string[] solutionFiles, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Building optimized call graph for {FileCount} solution files", solutionFiles.Length);

            var callGraph = new ConcurrentDictionary<string, HashSet<string>>();
            var methodDefinitions = new ConcurrentDictionary<string, MethodInfo>();

            // Process files in parallel with controlled parallelism
            await Task.WhenAll(solutionFiles.Select(async solutionFile =>
            {
                await _parallelismSemaphore.WaitAsync(cancellationToken);
                try
                {
                    await ProcessFileForCallGraphAsync(solutionFile, callGraph, methodDefinitions, cancellationToken);
                }
                finally
                {
                    _parallelismSemaphore.Release();
                }
            }));

            return new MethodCallGraph(
                callGraph.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                methodDefinitions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            );
        }

        public async Task<IReadOnlyList<string>> GetAffectedMethodsAsync(string[] changedFiles, string[] changedMethods, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Analyzing impact of {MethodCount} changed methods in {FileCount} files", changedMethods.Length, changedFiles.Length);

            var affectedMethods = new HashSet<string>(changedMethods);
            var callGraph = await BuildCallGraphAsync(changedFiles, cancellationToken);

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
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var result = await _compilationCache.GetOrCreateSemanticModelAsync(filePath, async () =>
            {
                var syntaxTree = await GetOrCreateSyntaxTreeAsync(filePath, cancellationToken);
                var compilation = CSharpCompilation.Create(
                    assemblyName: Path.GetFileNameWithoutExtension(filePath),
                    syntaxTrees: new[] { syntaxTree },
                    references: _basicReferences.Value
                );

                return compilation.GetSemanticModel(syntaxTree);
            }, cancellationToken);

            return result ?? throw new InvalidOperationException($"Could not create semantic model for {filePath}");
        }

        public async Task<IReadOnlyList<TypeUsageInfo>> AnalyzeTypeUsageAsync(string[] sourceFiles, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Analyzing type usage in {FileCount} source files with optimization", sourceFiles.Length);

            var typeUsages = new ConcurrentBag<TypeUsageInfo>();

            // Process files in parallel
            await Task.WhenAll(sourceFiles.Select(async filePath =>
            {
                await _parallelismSemaphore.WaitAsync(cancellationToken);
                try
                {
                    await ProcessFileForTypeUsageAsync(filePath, typeUsages, cancellationToken);
                }
                finally
                {
                    _parallelismSemaphore.Release();
                }
            }));

            return typeUsages.ToList();
        }

        public async Task<IReadOnlyList<MethodInfo>> ExtractMethodsFromFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var syntaxTree = await GetOrCreateSyntaxTreeAsync(filePath, cancellationToken);
            var root = syntaxTree.GetRoot();

            var semanticModel = await GetSemanticModelAsync(filePath, cancellationToken);
            var methods = new List<MethodInfo>();

            var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methodDeclarations)
            {
                var methodSymbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
                if (methodSymbol == null) continue;

                var methodInfo = new MethodInfo(
                    GetMethodIdentifier(methodSymbol),
                    methodSymbol.Name,
                    methodSymbol.ContainingType.Name,
                    filePath,
                    method.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                );

                methods.Add(methodInfo);
            }

            return methods;
        }

        private async Task ProcessFileForCallGraphAsync(
            string solutionFile,
            ConcurrentDictionary<string, HashSet<string>> callGraph,
            ConcurrentDictionary<string, MethodInfo> methodDefinitions,
            CancellationToken cancellationToken)
        {
            try
            {
                var compilation = await GetOrCreateCompilationAsync(solutionFile, cancellationToken);
                if (compilation == null) return;

                foreach (var syntaxTree in compilation.SyntaxTrees)
                {
                    var root = syntaxTree.GetRoot();
                    var semanticModel = compilation.GetSemanticModel(syntaxTree);

                    var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
                    
                    foreach (var method in methodDeclarations)
                    {
                        var methodSymbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
                        if (methodSymbol == null) continue;

                        var methodId = GetMethodIdentifier(methodSymbol);
                        var methodInfo = new MethodInfo(
                            methodId,
                            methodSymbol.Name,
                            methodSymbol.ContainingType.Name,
                            syntaxTree.FilePath,
                            method.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                        );

                        methodDefinitions[methodId] = methodInfo;

                        var methodCalls = callGraph.GetOrAdd(methodId, _ => new HashSet<string>());

                        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>();
                        foreach (var invocation in invocations)
                        {
                            var invokedSymbol = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
                            if (invokedSymbol != null)
                            {
                                var invokedMethodId = GetMethodIdentifier(invokedSymbol);
                                lock (methodCalls) // HashSet is not thread-safe
                                {
                                    methodCalls.Add(invokedMethodId);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process file for call graph: {FilePath}", solutionFile);
            }
        }

        private async Task ProcessFileForTypeUsageAsync(
            string filePath,
            ConcurrentBag<TypeUsageInfo> typeUsages,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath)) return;

            try
            {
                var syntaxTree = await GetOrCreateSyntaxTreeAsync(filePath, cancellationToken);
                var root = syntaxTree.GetRoot();

                var semanticModel = await GetSemanticModelAsync(filePath, cancellationToken);

                var typeDeclarations = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>();
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

                var identifierNames = root.DescendantNodes().OfType<IdentifierNameSyntax>();
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
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process file for type usage: {FilePath}", filePath);
            }
        }

        private async Task<Compilation?> GetOrCreateCompilationAsync(string filePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath)) 
                return null;

            return await _compilationCache.GetOrCreateCompilationAsync(filePath, async () =>
            {
                var syntaxTree = await GetOrCreateSyntaxTreeAsync(filePath, cancellationToken);

                var compilation = CSharpCompilation.Create(
                    assemblyName: Path.GetFileNameWithoutExtension(filePath),
                    syntaxTrees: new[] { syntaxTree },
                    references: _basicReferences.Value
                );

                return compilation;
            }, cancellationToken);
        }

        private Task<SyntaxTree> GetOrCreateSyntaxTreeAsync(string filePath, CancellationToken cancellationToken)
        {
            return _syntaxTreeCache.GetOrAdd(filePath, _ => Task.FromResult(
                CSharpSyntaxTree.ParseText(File.ReadAllText(filePath), path: filePath)));
        }

        private static string GetMethodIdentifier(IMethodSymbol methodSymbol)
        {
            return $"{methodSymbol.ContainingType.ToDisplayString()}.{methodSymbol.Name}({string.Join(",", methodSymbol.Parameters.Select(p => p.Type.ToDisplayString()))})";
        }

        private static TypeUsageContext GetTypeUsageContext(BaseTypeDeclarationSyntax typeDecl)
        {
            switch (typeDecl)
            {
                case ClassDeclarationSyntax _:
                case InterfaceDeclarationSyntax _:
                case StructDeclarationSyntax _:
                case EnumDeclarationSyntax _:
                    return TypeUsageContext.Declaration;
                default:
                    return TypeUsageContext.Reference;
            }
        }

        private static ImmutableArray<MetadataReference> CreateBasicReferences()
        {
            var references = new List<MetadataReference>();
            
            var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (runtimePath != null)
            {
                var referenceFiles = new[]
                {
                    "System.Runtime.dll",
                    "System.Collections.dll",
                    "System.Linq.dll",
                    "System.Threading.Tasks.dll",
                    "mscorlib.dll"
                };

                foreach (var refFile in referenceFiles)
                {
                    var refPath = Path.Combine(runtimePath, refFile);
                    if (File.Exists(refPath))
                    {
                        references.Add(MetadataReference.CreateFromFile(refPath));
                    }
                }

                // Add the core object assembly
                references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            }

            return references.ToImmutableArray();
        }

        public void Dispose()
        {
            _parallelismSemaphore?.Dispose();
            _compilationCache?.Dispose();
        }
    }
}