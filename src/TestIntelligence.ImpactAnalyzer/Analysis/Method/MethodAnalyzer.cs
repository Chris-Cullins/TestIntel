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
using TestIntelligence.ImpactAnalyzer.Analysis.CallGraph;
using TestIntelligence.ImpactAnalyzer.Analysis.Utilities;
using TestIntelligence.ImpactAnalyzer.Analysis.Workspace;

namespace TestIntelligence.ImpactAnalyzer.Analysis.Method
{
    public class MethodAnalyzer : IMethodAnalyzer
    {
        private readonly ILogger<MethodAnalyzer> _logger;
        private readonly IWorkspaceManager _workspaceManager;
        private readonly ICallGraphAnalyzer _callGraphAnalyzer;

        public MethodAnalyzer(
            ILogger<MethodAnalyzer> logger,
            IWorkspaceManager workspaceManager,
            ICallGraphAnalyzer callGraphAnalyzer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
            _callGraphAnalyzer = callGraphAnalyzer ?? throw new ArgumentNullException(nameof(callGraphAnalyzer));
        }

        public async Task<IReadOnlyList<MethodInfo>> ExtractMethodsFromFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            try
            {
                if (_workspaceManager.IsInitialized)
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

        public async Task<IReadOnlyList<string>> GetAffectedMethodsAsync(string[] changedFiles, string[] changedMethods, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("Analyzing impact of {MethodCount} changed methods in {FileCount} files", changedMethods.Length, changedFiles.Length);

            var affectedMethods = new HashSet<string>(changedMethods);
            var callGraph = await _callGraphAnalyzer.BuildCallGraphAsync(changedFiles, cancellationToken).ConfigureAwait(false);

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

        public async Task<IReadOnlyList<TestCoverageResult>> FindTestsExercisingMethodAsync(string methodId, string[] solutionFiles, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("Finding tests exercising method: {MethodId} using enhanced analyzer", methodId);
            
            var callGraph = await _callGraphAnalyzer.BuildCallGraphAsync(solutionFiles, cancellationToken).ConfigureAwait(false);
            var results = callGraph.GetTestCoverageForMethod(methodId);
            
            _logger.LogInformation("Found {TestCount} tests exercising method {MethodId} with enhanced accuracy", 
                results.Count, methodId);
            
            return results.ToList();
        }

        private async Task<IReadOnlyList<MethodInfo>> ExtractMethodsUsingWorkspaceAsync(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var methods = new List<MethodInfo>();
            
            if (_workspaceManager.CompilationManager == null)
                return methods;

            var syntaxTree = await _workspaceManager.CompilationManager.GetSyntaxTreeAsync(filePath, cancellationToken);
            var semanticModel = _workspaceManager.CompilationManager.GetSemanticModel(filePath);
            
            if (syntaxTree == null || semanticModel == null)
                return methods;

            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            
            foreach (var method in methodDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
                if (symbol is not IMethodSymbol methodSymbol) continue;

                var isTest = RoslynAnalyzerHelper.IsTestMethod(methodSymbol, method);
                var methodInfo = new MethodInfo(
                    _workspaceManager.SymbolResolver?.GetFullyQualifiedMethodName(methodSymbol) ?? RoslynAnalyzerHelper.GetMethodIdentifier(methodSymbol),
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

        private async Task<IReadOnlyList<MethodInfo>> ExtractMethodsStandaloneAsync(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Fallback implementation using standalone compilation
            var methods = new List<MethodInfo>();
            
            var sourceCode = await File.ReadAllTextAsync(filePath, cancellationToken);
            var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            var root = await syntaxTree.GetRootAsync(cancellationToken);

            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                assemblyName: Path.GetFileNameWithoutExtension(filePath),
                syntaxTrees: new[] { syntaxTree },
                references: RoslynAnalyzerHelper.GetBasicReferences()
            );

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            
            foreach (var method in methodDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
                if (symbol is not IMethodSymbol methodSymbol) continue;

                var isTest = RoslynAnalyzerHelper.IsTestMethod(methodSymbol, method);
                var methodInfo = new MethodInfo(
                    RoslynAnalyzerHelper.GetMethodIdentifier(methodSymbol),
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
    }
}