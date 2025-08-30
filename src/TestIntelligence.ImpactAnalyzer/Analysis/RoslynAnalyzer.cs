using System;
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
using TestIntelligence.ImpactAnalyzer.Models;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public interface IRoslynAnalyzer
    {
        Task<MethodCallGraph> BuildCallGraphAsync(string[] solutionFiles, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<string>> GetAffectedMethodsAsync(string[] changedFiles, string[] changedMethods, CancellationToken cancellationToken = default);
        Task<SemanticModel> GetSemanticModelAsync(string filePath, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<TypeUsageInfo>> AnalyzeTypeUsageAsync(string[] sourceFiles, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<MethodInfo>> ExtractMethodsFromFileAsync(string filePath, CancellationToken cancellationToken = default);
    }

    public class RoslynAnalyzer : IRoslynAnalyzer
    {
        private readonly ILogger<RoslynAnalyzer> _logger;
        private readonly Dictionary<string, Compilation> _compilationCache;

        public RoslynAnalyzer(ILogger<RoslynAnalyzer> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _compilationCache = new Dictionary<string, Compilation>();
        }

        public async Task<MethodCallGraph> BuildCallGraphAsync(string[] solutionFiles, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Building call graph for {FileCount} solution files", solutionFiles.Length);

            var callGraph = new Dictionary<string, HashSet<string>>();
            var methodDefinitions = new Dictionary<string, MethodInfo>();

            foreach (var solutionFile in solutionFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var compilation = (await GetOrCreateCompilationAsync(solutionFile, cancellationToken));
                if (compilation == null) continue;

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

                        if (!callGraph.ContainsKey(methodId))
                            callGraph[methodId] = new HashSet<string>();

                        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>();
                        foreach (var invocation in invocations)
                        {
                            var invokedSymbol = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
                            if (invokedSymbol != null)
                            {
                                var invokedMethodId = GetMethodIdentifier(invokedSymbol);
                                callGraph[methodId].Add(invokedMethodId);
                            }
                        }
                    }
                }
            }

            return new MethodCallGraph(callGraph, methodDefinitions);
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

        public Task<SemanticModel> GetSemanticModelAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var sourceCode = File.ReadAllText(filePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            
            var compilation = CSharpCompilation.Create(
                assemblyName: Path.GetFileNameWithoutExtension(filePath),
                syntaxTrees: new[] { syntaxTree },
                references: GetBasicReferences()
            );

            return Task.FromResult(compilation.GetSemanticModel(syntaxTree));
        }

        public Task<IReadOnlyList<TypeUsageInfo>> AnalyzeTypeUsageAsync(string[] sourceFiles, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Analyzing type usage in {FileCount} source files", sourceFiles.Length);

            var typeUsages = new List<TypeUsageInfo>();

            foreach (var filePath in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(filePath)) continue;

                var sourceCode = File.ReadAllText(filePath);
                var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
                var root = syntaxTree.GetRoot();

                var compilation = CSharpCompilation.Create(
                    assemblyName: Path.GetFileNameWithoutExtension(filePath),
                    syntaxTrees: new[] { syntaxTree },
                    references: GetBasicReferences()
                );

                var semanticModel = compilation.GetSemanticModel(syntaxTree);

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

            return Task.FromResult<IReadOnlyList<TypeUsageInfo>>(typeUsages);
        }

        public Task<IReadOnlyList<MethodInfo>> ExtractMethodsFromFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var sourceCode = File.ReadAllText(filePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            var root = syntaxTree.GetRoot();

            var compilation = CSharpCompilation.Create(
                assemblyName: Path.GetFileNameWithoutExtension(filePath),
                syntaxTrees: new[] { syntaxTree },
                references: GetBasicReferences()
            );

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
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

            return Task.FromResult<IReadOnlyList<MethodInfo>>(methods);
        }

        private Task<Compilation?> GetOrCreateCompilationAsync(string filePath, CancellationToken cancellationToken)
        {
            if (_compilationCache.TryGetValue(filePath, out var cached))
                return Task.FromResult<Compilation?>(cached);

            try
            {
                if (!File.Exists(filePath)) return Task.FromResult<Compilation?>(null);

                var sourceCode = File.ReadAllText(filePath);
                var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, path: filePath);

                var compilation = CSharpCompilation.Create(
                    assemblyName: Path.GetFileNameWithoutExtension(filePath),
                    syntaxTrees: new[] { syntaxTree },
                    references: GetBasicReferences()
                );

                _compilationCache[filePath] = compilation;
                return Task.FromResult<Compilation?>(compilation);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create compilation for {FilePath}", filePath);
                return Task.FromResult<Compilation?>(null);
            }
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

        private static ImmutableArray<MetadataReference> GetBasicReferences()
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

            return references.ToImmutableArray();
        }
    }

    public class MethodCallGraph
    {
        private readonly Dictionary<string, HashSet<string>> _callGraph;
        private readonly Dictionary<string, MethodInfo> _methodDefinitions;
        private readonly Dictionary<string, HashSet<string>> _reverseGraph;

        public MethodCallGraph(Dictionary<string, HashSet<string>> callGraph, Dictionary<string, MethodInfo> methodDefinitions)
        {
            _callGraph = callGraph ?? throw new ArgumentNullException(nameof(callGraph));
            _methodDefinitions = methodDefinitions ?? throw new ArgumentNullException(nameof(methodDefinitions));
            _reverseGraph = BuildReverseGraph();
        }

        public IReadOnlyCollection<string> GetMethodCalls(string methodId)
        {
            return _callGraph.TryGetValue(methodId, out var calls) ? calls : new HashSet<string>();
        }

        public IReadOnlyCollection<string> GetMethodDependents(string methodId)
        {
            return _reverseGraph.TryGetValue(methodId, out var dependents) ? dependents : new HashSet<string>();
        }

        public IReadOnlyCollection<string> GetTransitiveDependents(string methodId)
        {
            var visited = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(methodId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!visited.Add(current)) continue;

                foreach (var dependent in GetMethodDependents(current))
                {
                    if (!visited.Contains(dependent))
                        queue.Enqueue(dependent);
                }
            }

            visited.Remove(methodId);
            return visited;
        }

        public MethodInfo? GetMethodInfo(string methodId)
        {
            return _methodDefinitions.TryGetValue(methodId, out var info) ? info : null;
        }

        public IReadOnlyCollection<string> GetAllMethods()
        {
            return _methodDefinitions.Keys;
        }

        private Dictionary<string, HashSet<string>> BuildReverseGraph()
        {
            var reverse = new Dictionary<string, HashSet<string>>();

            foreach (var kvp in _callGraph)
            {
                foreach (var calledMethod in kvp.Value)
                {
                    if (!reverse.ContainsKey(calledMethod))
                        reverse[calledMethod] = new HashSet<string>();
                    
                    reverse[calledMethod].Add(kvp.Key);
                }
            }

            return reverse;
        }
    }

    public class MethodInfo
    {
        public MethodInfo(string id, string name, string containingType, string filePath, int lineNumber)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ContainingType = containingType ?? throw new ArgumentNullException(nameof(containingType));
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            LineNumber = lineNumber;
        }

        public string Id { get; }
        public string Name { get; }
        public string ContainingType { get; }
        public string FilePath { get; }
        public int LineNumber { get; }

        public override string ToString()
        {
            return $"{ContainingType}.{Name} at {Path.GetFileName(FilePath)}:{LineNumber}";
        }
    }

    public class TypeUsageInfo
    {
        public TypeUsageInfo(string typeName, string @namespace, string filePath, int lineNumber, TypeUsageContext context)
        {
            TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
            Namespace = @namespace ?? throw new ArgumentNullException(nameof(@namespace));
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            LineNumber = lineNumber;
            Context = context;
        }

        public string TypeName { get; }
        public string Namespace { get; }
        public string FilePath { get; }
        public int LineNumber { get; }
        public TypeUsageContext Context { get; }

        public string FullTypeName => string.IsNullOrEmpty(Namespace) ? TypeName : $"{Namespace}.{TypeName}";

        public override string ToString()
        {
            return $"{Context}: {FullTypeName} at {Path.GetFileName(FilePath)}:{LineNumber}";
        }
    }

    public enum TypeUsageContext
    {
        Declaration,
        Reference,
        Inheritance,
        Implementation
    }
}