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
    public class RoslynAnalyzerV2 : IRoslynAnalyzer
    {
        private readonly ILogger<RoslynAnalyzerV2> _logger;
        private readonly SolutionParser _solutionParser;
        private readonly ProjectParser _projectParser;
        private readonly DependencyGraphBuilder _dependencyGraphBuilder;
        private readonly SolutionWorkspaceBuilder _workspaceBuilder;
        private readonly ILoggerFactory _loggerFactory;

        private SolutionWorkspace? _currentWorkspace;
        private CompilationManager? _compilationManager;
        private SymbolResolutionEngine? _symbolResolver;
        private CallGraphBuilderV2? _callGraphBuilder;

        public RoslynAnalyzerV2(ILogger<RoslynAnalyzerV2> logger, ILoggerFactory loggerFactory)
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
            _logger.LogInformation("Building call graph for {FileCount} solution files using enhanced analyzer", solutionFiles.Length);

            // Find the solution file
            var solutionFile = FindSolutionFile(solutionFiles);
            if (solutionFile == null)
            {
                _logger.LogWarning("No solution file found, falling back to individual file analysis");
                return await BuildCallGraphFromFilesAsync(solutionFiles, cancellationToken);
            }

            try
            {
                // Initialize workspace if not already done
                await InitializeWorkspaceAsync(solutionFile, cancellationToken);

                if (_callGraphBuilder == null)
                {
                    _logger.LogError("Call graph builder not initialized");
                    throw new InvalidOperationException("Call graph builder not initialized");
                }

                return await _callGraphBuilder.BuildCallGraphAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build call graph using enhanced analyzer, falling back to file-based analysis");
                return await BuildCallGraphFromFilesAsync(solutionFiles, cancellationToken);
            }
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
            if (_compilationManager != null)
            {
                var semanticModel = _compilationManager.GetSemanticModel(filePath);
                if (semanticModel != null)
                {
                    return semanticModel;
                }
            }

            // Fallback to individual file compilation
            return await GetSemanticModelFallbackAsync(filePath, cancellationToken);
        }

        public async Task<IReadOnlyList<TypeUsageInfo>> AnalyzeTypeUsageAsync(string[] sourceFiles, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Analyzing type usage in {FileCount} source files", sourceFiles.Length);

            var typeUsages = new List<TypeUsageInfo>();

            foreach (var filePath in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(filePath)) continue;

                try
                {
                    var usages = await AnalyzeTypeUsageInFileAsync(filePath, cancellationToken);
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
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            try
            {
                if (_compilationManager != null)
                {
                    return await ExtractMethodsUsingWorkspaceAsync(filePath, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract methods using workspace, falling back to standalone analysis");
            }

            // Fallback to standalone analysis
            return await ExtractMethodsStandaloneAsync(filePath, cancellationToken);
        }

        public async Task<IReadOnlyList<TestCoverageResult>> FindTestsExercisingMethodAsync(string methodId, string[] solutionFiles, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Finding tests exercising method: {MethodId} using enhanced analyzer", methodId);
            
            var callGraph = await BuildCallGraphAsync(solutionFiles, cancellationToken);
            var results = callGraph.GetTestCoverageForMethod(methodId);
            
            _logger.LogInformation("Found {TestCount} tests exercising method {MethodId} with enhanced accuracy", 
                results.Count, methodId);
            
            return results.ToList();
        }

        private async Task InitializeWorkspaceAsync(string solutionPath, CancellationToken cancellationToken)
        {
            if (_currentWorkspace != null)
            {
                _logger.LogDebug("Workspace already initialized for solution");
                return;
            }

            _logger.LogInformation("Initializing solution workspace: {SolutionPath}", solutionPath);

            try
            {
                // Parse solution structure
                var solutionInfo = await _solutionParser.ParseSolutionAsync(solutionPath, cancellationToken);
                
                // Parse individual projects
                var projectTasks = solutionInfo.Projects.Select(async p =>
                {
                    try
                    {
                        return await _projectParser.ParseProjectAsync(p.Path, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse project: {ProjectPath}", p.Path);
                        return null;
                    }
                });
                
                var projectDetails = (await Task.WhenAll(projectTasks)).Where(p => p != null).ToList();
                
                // Build dependency graph
                var dependencyGraph = _dependencyGraphBuilder.BuildDependencyGraph(projectDetails!);
                
                // Create workspace
                _currentWorkspace = await _workspaceBuilder.CreateWorkspaceAsync(solutionPath, cancellationToken);
                
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
            // Fallback to the original file-based approach
            _logger.LogInformation("Building call graph from individual files (fallback mode)");

            var callGraph = new Dictionary<string, HashSet<string>>();
            var methodDefinitions = new Dictionary<string, MethodInfo>();

            // First pass: Extract method definitions
            foreach (var file in files)
            {
                if (!File.Exists(file)) continue;

                try
                {
                    var methods = await ExtractMethodsStandaloneAsync(file, cancellationToken);
                    foreach (var method in methods)
                    {
                        methodDefinitions[method.Id] = method;
                        if (!callGraph.ContainsKey(method.Id))
                            callGraph[method.Id] = new HashSet<string>();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process file in fallback mode: {FilePath}", file);
                }
            }

            // Second pass: Extract method calls
            foreach (var file in files)
            {
                if (!File.Exists(file)) continue;

                try
                {
                    await ExtractMethodCallsStandaloneAsync(file, callGraph, methodDefinitions, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract method calls from file: {FilePath}", file);
                }
            }

            return new MethodCallGraph(callGraph, methodDefinitions);
        }

        private Task<SemanticModel> GetSemanticModelFallbackAsync(string filePath, CancellationToken cancellationToken)
        {
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

                // Find all invocation expressions in this method
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
            }

            return Task.CompletedTask;
        }

        private Task<IReadOnlyList<TypeUsageInfo>> AnalyzeTypeUsageWithSemanticModel(SyntaxNode root, SemanticModel semanticModel, string filePath, CancellationToken cancellationToken)
        {
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