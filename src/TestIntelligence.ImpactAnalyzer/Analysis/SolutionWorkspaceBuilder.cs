using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
// using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public class SolutionWorkspace
    {
        public SolutionWorkspace(MSBuildWorkspace workspace, Solution solution, 
            IReadOnlyDictionary<string, ProjectId> projectPathToId,
            IReadOnlyDictionary<ProjectId, Compilation> compilations)
        {
            Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            Solution = solution ?? throw new ArgumentNullException(nameof(solution));
            ProjectPathToId = projectPathToId ?? throw new ArgumentNullException(nameof(projectPathToId));
            Compilations = compilations ?? throw new ArgumentNullException(nameof(compilations));
        }

        public MSBuildWorkspace Workspace { get; }
        public Solution Solution { get; }
        public IReadOnlyDictionary<string, ProjectId> ProjectPathToId { get; }
        public IReadOnlyDictionary<ProjectId, Compilation> Compilations { get; }

        public void Dispose()
        {
            Workspace?.Dispose();
        }
    }

    public class SolutionWorkspaceBuilder
    {
        private readonly ILogger<SolutionWorkspaceBuilder> _logger;
        // private static bool _msbuildRegistered = false;
        // private static readonly object _lockObject = new object();

        public SolutionWorkspaceBuilder(ILogger<SolutionWorkspaceBuilder> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<SolutionWorkspace> CreateWorkspaceAsync(string solutionPath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(solutionPath))
                throw new FileNotFoundException($"Solution file not found: {solutionPath}");

            _logger.LogInformation("Creating workspace for solution: {SolutionPath}", solutionPath);

            // Ensure MSBuild is registered (only once per process)
            // EnsureMSBuildRegistered();

            // Configure MSBuild properties to use the local .NET SDK
            var properties = new Dictionary<string, string>
            {
                // Use the current .NET SDK
                // { "MSBuildSDKsPath", null }, // Let MSBuild find the SDK automatically
                { "DesignTimeBuild", "true" }, // Optimize for analysis scenarios
                { "BuildingProject", "false" }, // We're not building, just analyzing
            };
            
            var workspace = MSBuildWorkspace.Create(properties);
            
            try
            {
                // Configure workspace to handle build issues gracefully
                workspace.WorkspaceFailed += OnWorkspaceFailed;

                var solution = await LoadSolutionAsync(workspace, solutionPath, cancellationToken);
                var projectPathToId = BuildProjectPathMapping(solution);
                var compilations = await BuildSolutionCompilationsAsync(solution, cancellationToken);

                _logger.LogInformation("Workspace created successfully with {ProjectCount} projects", 
                    solution.Projects.Count());

                return new SolutionWorkspace(workspace, solution, projectPathToId, compilations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create workspace for solution: {SolutionPath}", solutionPath);
                workspace.Dispose();
                throw;
            }
        }

        public Task<IReadOnlyList<Project>> LoadProjectsAsync(SolutionWorkspace solutionWorkspace, CancellationToken cancellationToken = default)
        {
            var projects = new List<Project>();
            
            foreach (var project in solutionWorkspace.Solution.Projects)
            {
                if (IsCSharpProject(project))
                {
                    projects.Add(project);
                    _logger.LogDebug("Loaded project: {ProjectName} ({ProjectId})", project.Name, project.Id);
                }
            }

            _logger.LogInformation("Loaded {ProjectCount} C# projects", projects.Count);
            return Task.FromResult<IReadOnlyList<Project>>(projects);
        }

        public Task<IReadOnlyList<MetadataReference>> ResolveMetadataReferencesAsync(Project project, CancellationToken cancellationToken = default)
        {
            var references = new List<MetadataReference>();
            
            // Add basic framework references
            references.AddRange(GetBasicReferences());
            
            // Add project-specific metadata references
            foreach (var reference in project.MetadataReferences)
            {
                references.Add(reference);
            }

            _logger.LogDebug("Resolved {ReferenceCount} metadata references for project {ProjectName}", 
                references.Count, project.Name);

            return Task.FromResult<IReadOnlyList<MetadataReference>>(references);
        }

        public CompilationOptions ApplyCompilationOptions(Project project)
        {
            var options = project.CompilationOptions;
            
            if (options == null)
            {
                // Create default compilation options for C#
                options = new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable,
                    allowUnsafe: false);
            }

            return options;
        }

        private async Task<Solution> LoadSolutionAsync(MSBuildWorkspace workspace, string solutionPath, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("Loading solution using MSBuild workspace: {SolutionPath}", solutionPath);
                var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
                
                _logger.LogInformation("Solution loaded with {ProjectCount} projects", solution.Projects.Count());
                
                // Log any diagnostics
                foreach (var diagnostic in workspace.Diagnostics)
                {
                    if (diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                    {
                        _logger.LogWarning("Workspace diagnostic: {Message}", diagnostic.Message);
                    }
                    else
                    {
                        _logger.LogDebug("Workspace diagnostic: {Message}", diagnostic.Message);
                    }
                }
                
                return solution;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load solution: {SolutionPath}", solutionPath);
                throw;
            }
        }

        private IReadOnlyDictionary<string, ProjectId> BuildProjectPathMapping(Solution solution)
        {
            var mapping = new Dictionary<string, ProjectId>();
            
            foreach (var project in solution.Projects)
            {
                if (!string.IsNullOrEmpty(project.FilePath))
                {
                    mapping[Path.GetFullPath(project.FilePath)] = project.Id;
                }
            }
            
            return mapping;
        }

        private async Task<IReadOnlyDictionary<ProjectId, Compilation>> BuildSolutionCompilationsAsync(Solution solution, CancellationToken cancellationToken)
        {
            var compilations = new Dictionary<ProjectId, Compilation>();
            
            _logger.LogInformation("Building compilations for {ProjectCount} projects", solution.Projects.Count());
            
            foreach (var project in solution.Projects.Where(IsCSharpProject))
            {
                try
                {
                    _logger.LogDebug("Building compilation for project: {ProjectName}", project.Name);
                    var compilation = await project.GetCompilationAsync(cancellationToken);
                    
                    if (compilation != null)
                    {
                        compilations[project.Id] = compilation;
                        
                        // Log compilation errors/warnings
                        var diagnostics = compilation.GetDiagnostics();
                        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                        var warnings = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();
                        
                        if (errors.Any())
                        {
                            _logger.LogWarning("Project {ProjectName} has {ErrorCount} compilation errors", 
                                project.Name, errors.Count);
                        }
                        
                        if (warnings.Any())
                        {
                            _logger.LogDebug("Project {ProjectName} has {WarningCount} compilation warnings", 
                                project.Name, warnings.Count);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Failed to get compilation for project: {ProjectName}", project.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to build compilation for project: {ProjectName}", project.Name);
                    // Continue with other projects
                }
            }
            
            _logger.LogInformation("Built {CompilationCount} successful compilations", compilations.Count);
            return compilations;
        }

        private void OnWorkspaceFailed(object? sender, WorkspaceDiagnosticEventArgs e)
        {
            var logLevel = e.Diagnostic.Kind switch
            {
                WorkspaceDiagnosticKind.Failure => LogLevel.Warning,
                WorkspaceDiagnosticKind.Warning => LogLevel.Debug,
                _ => LogLevel.Trace
            };
            
            _logger.Log(logLevel, "Workspace diagnostic [{Kind}]: {Message}", 
                e.Diagnostic.Kind, e.Diagnostic.Message);
        }

        private static bool IsCSharpProject(Project project)
        {
            return project.Language == LanguageNames.CSharp;
        }

        private static ImmutableArray<MetadataReference> GetBasicReferences()
        {
            var references = new List<MetadataReference>();
            
            var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (runtimePath != null)
            {
                var commonRefs = new[]
                {
                    "System.Runtime.dll",
                    "System.Collections.dll",
                    "System.Linq.dll",
                    "System.Threading.Tasks.dll",
                    "System.Text.RegularExpressions.dll",
                    "System.IO.dll",
                    "System.Reflection.dll",
                    "System.Console.dll"
                };

                foreach (var refName in commonRefs)
                {
                    var refPath = Path.Combine(runtimePath, refName);
                    if (File.Exists(refPath))
                    {
                        references.Add(MetadataReference.CreateFromFile(refPath));
                    }
                }

                // Add core reference
                references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            }

            return references.ToImmutableArray();
        }

        // private void EnsureMSBuildRegistered()
        // {
        //     lock (_lockObject)
        //     {
        //         if (!_msbuildRegistered)
        //         {
        //             try
        //             {
        //                 _logger.LogInformation("Registering MSBuild with MSBuildLocator");
        //                 
        //                 // Find the default MSBuild instance
        //                 var msbuildInstances = MSBuildLocator.QueryVisualStudioInstances().ToList();
        //                 
        //                 if (msbuildInstances.Any())
        //                 {
        //                     var latestInstance = msbuildInstances
        //                         .OrderByDescending(i => i.Version)
        //                         .First();
        //                     
        //                     _logger.LogInformation("Found MSBuild instance: {Name} {Version} at {Path}", 
        //                         latestInstance.Name, latestInstance.Version, latestInstance.MSBuildPath);
        //                         
        //                     MSBuildLocator.RegisterInstance(latestInstance);
        //                 }
        //                 else
        //                 {
        //                     _logger.LogInformation("No Visual Studio instances found, registering default MSBuild");
        //                     MSBuildLocator.RegisterDefaults();
        //                 }
        //                 
        //                 _msbuildRegistered = true;
        //                 _logger.LogInformation("MSBuild registration completed successfully");
        //             }
        //             catch (Exception ex)
        //             {
        //                 _logger.LogWarning(ex, "Failed to register MSBuild with MSBuildLocator, using defaults");
        //                 // Don't throw - let MSBuildWorkspace.Create try with system defaults
        //                 _msbuildRegistered = true; // Prevent retries
        //             }
        //         }
        //     }
        // }
    }
}