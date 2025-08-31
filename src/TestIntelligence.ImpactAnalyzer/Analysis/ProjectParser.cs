using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public class ProjectDetails
    {
        public ProjectDetails(string projectPath, string targetFramework, IReadOnlyList<string> sourceFiles, 
            IReadOnlyList<string> projectReferences, IReadOnlyList<PackageReference> packageReferences,
            IReadOnlyList<string> assemblyReferences)
        {
            ProjectPath = projectPath ?? throw new ArgumentNullException(nameof(projectPath));
            TargetFramework = targetFramework ?? throw new ArgumentNullException(nameof(targetFramework));
            SourceFiles = sourceFiles ?? throw new ArgumentNullException(nameof(sourceFiles));
            ProjectReferences = projectReferences ?? throw new ArgumentNullException(nameof(projectReferences));
            PackageReferences = packageReferences ?? throw new ArgumentNullException(nameof(packageReferences));
            AssemblyReferences = assemblyReferences ?? throw new ArgumentNullException(nameof(assemblyReferences));
        }

        public string ProjectPath { get; }
        public string TargetFramework { get; }
        public IReadOnlyList<string> SourceFiles { get; }
        public IReadOnlyList<string> ProjectReferences { get; }
        public IReadOnlyList<PackageReference> PackageReferences { get; }
        public IReadOnlyList<string> AssemblyReferences { get; }
        public string Directory => Path.GetDirectoryName(ProjectPath) ?? string.Empty;
        public string Name => Path.GetFileNameWithoutExtension(ProjectPath);
    }

    public class PackageReference
    {
        public PackageReference(string name, string version)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Version = version ?? throw new ArgumentNullException(nameof(version));
        }

        public string Name { get; }
        public string Version { get; }

        public override string ToString() => $"{Name} {Version}";
    }

    public class ProjectParser
    {
        private readonly ILogger<ProjectParser> _logger;

        public ProjectParser(ILogger<ProjectParser> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<ProjectDetails> ParseProjectAsync(string projectPath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(projectPath))
                throw new FileNotFoundException($"Project file not found: {projectPath}");

            _logger.LogDebug("Parsing project file: {ProjectPath}", projectPath);

            var projectContent = File.ReadAllText(projectPath);
            var projectDir = Path.GetDirectoryName(projectPath) ?? string.Empty;

            try
            {
                var document = XDocument.Parse(projectContent);
                
                var targetFramework = GetTargetFramework(document);
                var sourceFiles = GetSourceFiles(document, projectDir);
                var projectReferences = GetProjectReferences(document, projectDir);
                var packageReferences = GetPackageReferences(document);
                var assemblyReferences = GetAssemblyReferences(document);

                _logger.LogDebug("Parsed project {ProjectPath}: {SourceFileCount} source files, {ProjectRefCount} project refs", 
                    projectPath, sourceFiles.Count, projectReferences.Count);

                return Task.FromResult(new ProjectDetails(projectPath, targetFramework, sourceFiles, projectReferences, 
                    packageReferences, assemblyReferences));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse project file: {ProjectPath}", projectPath);
                throw;
            }
        }

        public IReadOnlyList<string> GetSourceFiles(ProjectDetails project)
        {
            return project.SourceFiles;
        }

        public IReadOnlyList<string> GetProjectReferences(ProjectDetails project)
        {
            return project.ProjectReferences;
        }

        public IReadOnlyList<PackageReference> GetPackageReferences(ProjectDetails project)
        {
            return project.PackageReferences;
        }

        public IReadOnlyList<string> GetAssemblyReferences(ProjectDetails project)
        {
            return project.AssemblyReferences;
        }

        private static string GetTargetFramework(XDocument document)
        {
            // Look for TargetFramework or TargetFrameworks
            var targetFramework = document.Descendants("TargetFramework").FirstOrDefault()?.Value;
            if (!string.IsNullOrEmpty(targetFramework))
                return targetFramework!;

            var targetFrameworks = document.Descendants("TargetFrameworks").FirstOrDefault()?.Value;
            if (!string.IsNullOrEmpty(targetFrameworks))
            {
                // Return the first target framework if multiple are specified
                return targetFrameworks!.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "net8.0";
            }

            // Default fallback
            return "net8.0";
        }

        private IReadOnlyList<string> GetSourceFiles(XDocument document, string projectDir)
        {
            var sourceFiles = new List<string>();

            // Explicit Compile items
            var compileItems = document.Descendants("Compile")
                .Select(e => e.Attribute("Include")?.Value)
                .Where(path => !string.IsNullOrEmpty(path));

            foreach (var includePath in compileItems)
            {
                var fullPath = Path.Combine(projectDir, includePath!);
                if (File.Exists(fullPath))
                {
                    sourceFiles.Add(Path.GetFullPath(fullPath));
                }
            }

            // If no explicit Compile items, use convention-based discovery
            if (sourceFiles.Count == 0)
            {
                DiscoverSourceFiles(projectDir, sourceFiles);
            }

            return sourceFiles;
        }

        private void DiscoverSourceFiles(string projectDir, List<string> sourceFiles)
        {
            var patterns = new[] { "*.cs" };
            var excludeDirs = new[] { "bin", "obj", ".git", ".vs" };

            try
            {
                foreach (var pattern in patterns)
                {
                    var files = Directory.GetFiles(projectDir, pattern, SearchOption.AllDirectories)
                        .Where(file => !excludeDirs.Any(dir => file.Contains($"{Path.DirectorySeparatorChar}{dir}{Path.DirectorySeparatorChar}")))
                        .Select(Path.GetFullPath);

                    sourceFiles.AddRange(files);
                }
            }
            catch (Exception ex)
            {
                // Log but don't fail - we'll continue with explicit references
                throw new InvalidOperationException($"Failed to discover source files in {projectDir}", ex);
            }
        }

        private IReadOnlyList<string> GetProjectReferences(XDocument document, string projectDir)
        {
            var references = new List<string>();

            var projectRefs = document.Descendants("ProjectReference")
                .Select(e => e.Attribute("Include")?.Value)
                .Where(path => !string.IsNullOrEmpty(path));

            foreach (var referencePath in projectRefs)
            {
                var fullPath = Path.Combine(projectDir, referencePath!);
                references.Add(Path.GetFullPath(fullPath));
            }

            return references;
        }

        private IReadOnlyList<PackageReference> GetPackageReferences(XDocument document)
        {
            var packages = new List<PackageReference>();

            var packageRefs = document.Descendants("PackageReference");
            foreach (var packageRef in packageRefs)
            {
                var name = packageRef.Attribute("Include")?.Value;
                var version = packageRef.Attribute("Version")?.Value ?? 
                             packageRef.Element("Version")?.Value ?? "latest";

                if (!string.IsNullOrEmpty(name))
                {
                    packages.Add(new PackageReference(name!, version ?? "latest"));
                }
            }

            return packages;
        }

        private IReadOnlyList<string> GetAssemblyReferences(XDocument document)
        {
            var references = new List<string>();

            var assemblyRefs = document.Descendants("Reference")
                .Select(e => e.Attribute("Include")?.Value)
                .Where(name => !string.IsNullOrEmpty(name));

            references.AddRange(assemblyRefs!);

            return references;
        }
    }
}