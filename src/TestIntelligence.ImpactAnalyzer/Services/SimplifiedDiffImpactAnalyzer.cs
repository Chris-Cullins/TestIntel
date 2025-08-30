using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.Core.Discovery;
using TestIntelligence.Core.Models;
using TestIntelligence.Core.Assembly;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Models;

namespace TestIntelligence.ImpactAnalyzer.Services
{
    public interface ISimplifiedDiffImpactAnalyzer
    {
        Task<SimplifiedTestImpactResult> AnalyzeDiffImpactAsync(string diffContent, string solutionPath, CancellationToken cancellationToken = default);
        Task<SimplifiedTestImpactResult> AnalyzeDiffFileImpactAsync(string diffFilePath, string solutionPath, CancellationToken cancellationToken = default);
        Task<SimplifiedTestImpactResult> AnalyzeGitDiffImpactAsync(string gitCommand, string solutionPath, CancellationToken cancellationToken = default);
    }

    public class SimplifiedDiffImpactAnalyzer : ISimplifiedDiffImpactAnalyzer
    {
        private readonly ILogger<SimplifiedDiffImpactAnalyzer> _logger;
        private readonly IGitDiffParser _diffParser;
        private readonly IRoslynAnalyzer _roslynAnalyzer;

        public SimplifiedDiffImpactAnalyzer(
            ILogger<SimplifiedDiffImpactAnalyzer> logger,
            IGitDiffParser diffParser,
            IRoslynAnalyzer roslynAnalyzer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _diffParser = diffParser ?? throw new ArgumentNullException(nameof(diffParser));
            _roslynAnalyzer = roslynAnalyzer ?? throw new ArgumentNullException(nameof(roslynAnalyzer));
        }

        public async Task<SimplifiedTestImpactResult> AnalyzeDiffImpactAsync(string diffContent, string solutionPath, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Analyzing test impact from diff content for solution: {SolutionPath}", solutionPath);

            try
            {
                // Parse the diff to get code changes
                var changeSet = await _diffParser.ParseDiffAsync(diffContent);
                
                // Analyze the impact of these changes
                return await AnalyzeChangeSetImpactAsync(changeSet, solutionPath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze diff impact for solution: {SolutionPath}", solutionPath);
                return SimplifiedTestImpactResult.Empty;
            }
        }

        public async Task<SimplifiedTestImpactResult> AnalyzeDiffFileImpactAsync(string diffFilePath, string solutionPath, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Analyzing test impact from diff file: {DiffFilePath} for solution: {SolutionPath}", diffFilePath, solutionPath);

            try
            {
                // Parse the diff file to get code changes
                var changeSet = await _diffParser.ParseDiffFileAsync(diffFilePath);
                
                // Analyze the impact of these changes
                return await AnalyzeChangeSetImpactAsync(changeSet, solutionPath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze diff file impact for {DiffFilePath}: {Message}", diffFilePath, ex.Message);
                return SimplifiedTestImpactResult.Empty;
            }
        }

        public async Task<SimplifiedTestImpactResult> AnalyzeGitDiffImpactAsync(string gitCommand, string solutionPath, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Analyzing test impact from git command: {GitCommand} for solution: {SolutionPath}", gitCommand, solutionPath);

            try
            {
                // Execute git command and parse the diff output
                var changeSet = await _diffParser.ParseDiffFromCommandAsync(gitCommand);
                
                // Analyze the impact of these changes
                return await AnalyzeChangeSetImpactAsync(changeSet, solutionPath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze git diff impact for command {GitCommand}: {Message}", gitCommand, ex.Message);
                return SimplifiedTestImpactResult.Empty;
            }
        }

        private async Task<SimplifiedTestImpactResult> AnalyzeChangeSetImpactAsync(CodeChangeSet changeSet, string solutionPath, CancellationToken cancellationToken)
        {
            if (!changeSet.Changes.Any())
            {
                _logger.LogInformation("No code changes detected, returning empty impact result");
                return SimplifiedTestImpactResult.Empty;
            }

            _logger.LogInformation("Analyzing impact of {ChangeCount} code changes", changeSet.Changes.Count);

            // Extract changed files and methods
            var changedFiles = changeSet.GetChangedFiles().ToArray();
            var changedMethods = changeSet.GetChangedMethods().ToArray();
            var changedTypes = changeSet.GetChangedTypes().ToArray();

            // Find source files in the solution
            var sourceFiles = await FindSourceFilesInSolutionAsync(solutionPath, changedFiles);
            
            // Get affected methods through call graph analysis
            var affectedMethods = await _roslynAnalyzer.GetAffectedMethodsAsync(sourceFiles.ToArray(), changedMethods, cancellationToken);

            // Create simplified impact result
            var impactedTests = CreateSimplifiedTestReferences(changedMethods, changedTypes, affectedMethods.ToList());

            _logger.LogInformation("Found {TestCount} potentially impacted test references from {ChangeCount} code changes", impactedTests.Count, changeSet.Changes.Count);

            return new SimplifiedTestImpactResult(
                impactedTests,
                changeSet,
                affectedMethods.ToList(),
                DateTime.UtcNow
            );
        }

        private Task<List<string>> FindSourceFilesInSolutionAsync(string solutionPath, IEnumerable<string> changedFiles)
        {
            var solutionDir = Path.GetDirectoryName(solutionPath) ?? Directory.GetCurrentDirectory();
            var sourceFiles = new List<string>();

            foreach (var relativeFile in changedFiles)
            {
                var absolutePath = Path.IsPathRooted(relativeFile) 
                    ? relativeFile 
                    : Path.Combine(solutionDir, relativeFile);

                if (File.Exists(absolutePath))
                {
                    sourceFiles.Add(absolutePath);
                }
                else
                {
                    _logger.LogWarning("Changed file not found: {FilePath}", absolutePath);
                }
            }

            return Task.FromResult(sourceFiles);
        }

        private List<SimplifiedTestReference> CreateSimplifiedTestReferences(
            IEnumerable<string> changedMethods, 
            IEnumerable<string> changedTypes, 
            List<string> affectedMethods)
        {
            var testReferences = new List<SimplifiedTestReference>();
            var methodSet = new HashSet<string>(changedMethods.Concat(affectedMethods), StringComparer.OrdinalIgnoreCase);
            var typeSet = new HashSet<string>(changedTypes, StringComparer.OrdinalIgnoreCase);

            // Create high-confidence test references based on method names
            foreach (var method in methodSet)
            {
                var testName = $"{method}Test";
                var testReference = new SimplifiedTestReference(
                    testName,
                    "AutoGenerated",
                    "Tests",
                    "Generated.Tests",
                    0.8,
                    "Method name similarity"
                );
                testReferences.Add(testReference);
            }

            // Create medium-confidence test references based on type names
            foreach (var type in typeSet)
            {
                var testName = $"{type}Tests";
                var testReference = new SimplifiedTestReference(
                    $"{type}IntegrationTest",
                    $"{type}Tests",
                    "Tests.Integration",
                    "Generated.Tests.Integration",
                    0.6,
                    "Type name similarity"
                );
                testReferences.Add(testReference);
            }

            return testReferences.OrderByDescending(t => t.Confidence).ToList();
        }
    }

    public class SimplifiedTestReference
    {
        public SimplifiedTestReference(
            string methodName,
            string typeName,
            string @namespace,
            string assemblyName,
            double confidence,
            string impactReasons)
        {
            MethodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
            TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
            Namespace = @namespace ?? throw new ArgumentNullException(nameof(@namespace));
            AssemblyName = assemblyName ?? throw new ArgumentNullException(nameof(assemblyName));
            Confidence = Math.Min(1.0, Math.Max(0.0, confidence));
            ImpactReasons = impactReasons ?? throw new ArgumentNullException(nameof(impactReasons));
        }

        public string MethodName { get; }
        public string TypeName { get; }
        public string Namespace { get; }
        public string AssemblyName { get; }
        public double Confidence { get; }
        public string ImpactReasons { get; }

        public string GetUniqueId()
        {
            return $"{Namespace}.{TypeName}.{MethodName}";
        }

        public override string ToString()
        {
            return $"[{Confidence:P0}] {TypeName}.{MethodName}";
        }
    }

    public class SimplifiedTestImpactResult
    {
        public static readonly SimplifiedTestImpactResult Empty = new SimplifiedTestImpactResult(
            new List<SimplifiedTestReference>(),
            new CodeChangeSet(new List<CodeChange>()),
            new List<string>(),
            DateTime.UtcNow
        );

        public SimplifiedTestImpactResult(
            IReadOnlyList<SimplifiedTestReference> impactedTests,
            CodeChangeSet codeChanges,
            IReadOnlyList<string> affectedMethods,
            DateTime analyzedAt)
        {
            ImpactedTests = impactedTests ?? throw new ArgumentNullException(nameof(impactedTests));
            CodeChanges = codeChanges ?? throw new ArgumentNullException(nameof(codeChanges));
            AffectedMethods = affectedMethods ?? throw new ArgumentNullException(nameof(affectedMethods));
            AnalyzedAt = analyzedAt;
        }

        public IReadOnlyList<SimplifiedTestReference> ImpactedTests { get; }
        public CodeChangeSet CodeChanges { get; }
        public IReadOnlyList<string> AffectedMethods { get; }
        public DateTime AnalyzedAt { get; }

        public int TotalChanges => CodeChanges.Changes.Count;
        public int TotalFiles => CodeChanges.GetChangedFiles().Count();
        public int TotalMethods => CodeChanges.GetChangedMethods().Count();
        public int TotalImpactedTests => ImpactedTests.Count;

        public override string ToString()
        {
            return $"Test Impact: {TotalImpactedTests} tests impacted by {TotalChanges} changes across {TotalFiles} files";
        }
    }
}