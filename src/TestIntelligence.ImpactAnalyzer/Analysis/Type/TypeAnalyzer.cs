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

namespace TestIntelligence.ImpactAnalyzer.Analysis.Type
{
    public class TypeAnalyzer : ITypeAnalyzer
    {
        private readonly ILogger<TypeAnalyzer> _logger;
        private readonly IWorkspaceManager _workspaceManager;

        public TypeAnalyzer(ILogger<TypeAnalyzer> logger, IWorkspaceManager workspaceManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
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

        public async Task<IReadOnlyList<TypeUsageInfo>> AnalyzeTypeUsageInFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var typeUsages = new List<TypeUsageInfo>();

            if (_workspaceManager.IsInitialized && _workspaceManager.CompilationManager != null)
            {
                var semanticModel = _workspaceManager.CompilationManager.GetSemanticModel(filePath);
                var syntaxTree = await _workspaceManager.CompilationManager.GetSyntaxTreeAsync(filePath, cancellationToken);
                
                if (semanticModel != null && syntaxTree != null)
                {
                    var root = await syntaxTree.GetRootAsync(cancellationToken);
                    // Analyze types using the proper semantic model
                    return await AnalyzeTypeUsageWithSemanticModel(root, semanticModel, filePath, cancellationToken);
                }
            }

            // Fallback implementation
            return await AnalyzeTypeUsageFallbackAsync(filePath, cancellationToken);
        }

        private Task<IReadOnlyList<TypeUsageInfo>> AnalyzeTypeUsageWithSemanticModel(
            SyntaxNode root, 
            SemanticModel semanticModel, 
            string filePath, 
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var typeUsages = new List<TypeUsageInfo>();

            // Analyze type declarations
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
                    RoslynAnalyzerHelper.GetTypeUsageContext(typeDecl)
                );

                typeUsages.Add(usage);
            }

            // Analyze type references
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

            return Task.FromResult<IReadOnlyList<TypeUsageInfo>>(typeUsages);
        }

        private async Task<IReadOnlyList<TypeUsageInfo>> AnalyzeTypeUsageFallbackAsync(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Original implementation as fallback
            var typeUsages = new List<TypeUsageInfo>();

            var sourceCode = await File.ReadAllTextAsync(filePath, cancellationToken);
            var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            var root = await syntaxTree.GetRootAsync(cancellationToken);

            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                assemblyName: Path.GetFileNameWithoutExtension(filePath),
                syntaxTrees: new[] { syntaxTree },
                references: RoslynAnalyzerHelper.GetBasicReferences()
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
                    RoslynAnalyzerHelper.GetTypeUsageContext(typeDecl)
                );

                typeUsages.Add(usage);
            }

            return typeUsages;
        }
    }
}