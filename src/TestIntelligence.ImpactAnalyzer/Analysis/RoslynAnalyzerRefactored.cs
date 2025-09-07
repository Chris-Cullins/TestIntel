using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Models;
using TestIntelligence.ImpactAnalyzer.Analysis.CallGraph;
using TestIntelligence.ImpactAnalyzer.Analysis.Method;
using TestIntelligence.ImpactAnalyzer.Analysis.Type;
using TestIntelligence.ImpactAnalyzer.Analysis.Utilities;
using TestIntelligence.ImpactAnalyzer.Analysis.Workspace;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    /// <summary>
    /// Refactored RoslynAnalyzer with separated concerns and improved maintainability.
    /// Each analysis concern is now handled by a dedicated component.
    /// </summary>
    public class RoslynAnalyzerRefactored : IRoslynAnalyzer
    {
        private readonly ILogger<RoslynAnalyzerRefactored> _logger;
        private readonly IWorkspaceManager _workspaceManager;
        private readonly ICallGraphAnalyzer _callGraphAnalyzer;
        private readonly IMethodAnalyzer _methodAnalyzer;
        private readonly ITypeAnalyzer _typeAnalyzer;

        public RoslynAnalyzerRefactored(
            ILogger<RoslynAnalyzerRefactored> logger,
            IWorkspaceManager workspaceManager,
            ICallGraphAnalyzer callGraphAnalyzer,
            IMethodAnalyzer methodAnalyzer,
            ITypeAnalyzer typeAnalyzer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
            _callGraphAnalyzer = callGraphAnalyzer ?? throw new ArgumentNullException(nameof(callGraphAnalyzer));
            _methodAnalyzer = methodAnalyzer ?? throw new ArgumentNullException(nameof(methodAnalyzer));
            _typeAnalyzer = typeAnalyzer ?? throw new ArgumentNullException(nameof(typeAnalyzer));
        }

        public async Task<MethodCallGraph> BuildCallGraphAsync(string[] solutionFiles, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Building call graph for {FileCount} solution files using refactored analyzer", solutionFiles.Length);
            
            // Initialize workspace if needed
            var solutionFile = RoslynAnalyzerHelper.FindSolutionFile(solutionFiles);
            if (solutionFile != null && !_workspaceManager.IsInitialized)
            {
                await _workspaceManager.InitializeAsync(solutionFile, cancellationToken).ConfigureAwait(false);
            }

            return await _callGraphAnalyzer.BuildCallGraphAsync(solutionFiles, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<string>> GetAffectedMethodsAsync(string[] changedFiles, string[] changedMethods, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Analyzing impact of {MethodCount} changed methods in {FileCount} files using refactored analyzer", 
                changedMethods.Length, changedFiles.Length);

            return await _methodAnalyzer.GetAffectedMethodsAsync(changedFiles, changedMethods, cancellationToken).ConfigureAwait(false);
        }

        public async Task<SemanticModel> GetSemanticModelAsync(string filePath, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Getting semantic model for file: {FilePath}", filePath);

            var semanticModel = await _workspaceManager.GetSemanticModelAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (semanticModel == null)
            {
                throw new InvalidOperationException($"Could not get semantic model for file: {filePath}");
            }

            return semanticModel;
        }

        public async Task<IReadOnlyList<TypeUsageInfo>> AnalyzeTypeUsageAsync(string[] sourceFiles, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Analyzing type usage in {FileCount} source files using refactored analyzer", sourceFiles.Length);

            return await _typeAnalyzer.AnalyzeTypeUsageAsync(sourceFiles, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<MethodInfo>> ExtractMethodsFromFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Extracting methods from file: {FilePath}", filePath);

            return await _methodAnalyzer.ExtractMethodsFromFileAsync(filePath, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<TestCoverageResult>> FindTestsExercisingMethodAsync(string methodId, string[] solutionFiles, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Finding tests exercising method: {MethodId} using refactored analyzer", methodId);

            return await _methodAnalyzer.FindTestsExercisingMethodAsync(methodId, solutionFiles, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _logger.LogDebug("Disposing refactored RoslynAnalyzer");
            _workspaceManager?.Dispose();
        }
    }
}