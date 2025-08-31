using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public class SolutionInfo
    {
        public SolutionInfo(string path, IReadOnlyList<ProjectInfo> projects)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Projects = projects ?? throw new ArgumentNullException(nameof(projects));
        }

        public string Path { get; }
        public IReadOnlyList<ProjectInfo> Projects { get; }
        public string Directory => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
    }

    public class ProjectInfo
    {
        public ProjectInfo(string name, string path, Guid id, IReadOnlyList<string> projectReferences)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Id = id;
            ProjectReferences = projectReferences ?? throw new ArgumentNullException(nameof(projectReferences));
        }

        public string Name { get; }
        public string Path { get; }
        public Guid Id { get; }
        public IReadOnlyList<string> ProjectReferences { get; }
        public string Directory => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
    }

    public class SolutionParser
    {
        private readonly ILogger<SolutionParser> _logger;

        public SolutionParser(ILogger<SolutionParser> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<SolutionInfo> ParseSolutionAsync(string solutionPath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(solutionPath))
                throw new FileNotFoundException($"Solution file not found: {solutionPath}");

            _logger.LogInformation("Parsing solution file: {SolutionPath}", solutionPath);

            var solutionContent = File.ReadAllText(solutionPath);
            var projects = new List<ProjectInfo>();
            var solutionDirectory = System.IO.Path.GetDirectoryName(solutionPath) ?? string.Empty;

            var lines = solutionContent.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                
                // Parse project declarations: Project("{project-type-guid}") = "ProjectName", "relativePath", "{project-guid}"
                if (line.StartsWith("Project("))
                {
                    var projectInfo = ParseProjectLine(line, solutionDirectory);
                    if (projectInfo != null && IsCSharpProject(projectInfo.Path))
                    {
                        projects.Add(projectInfo);
                        _logger.LogDebug("Found C# project: {ProjectName} at {ProjectPath}", 
                            projectInfo.Name, projectInfo.Path);
                    }
                }
            }

            _logger.LogInformation("Parsed solution with {ProjectCount} C# projects", projects.Count);
            return Task.FromResult(new SolutionInfo(solutionPath, projects));
        }

        public IReadOnlyList<string> GetProjectPaths(SolutionInfo solution)
        {
            return solution.Projects.Select(p => p.Path).ToList();
        }

        public IReadOnlyDictionary<string, IReadOnlyList<string>> GetProjectDependencies(SolutionInfo solution)
        {
            var dependencies = new Dictionary<string, IReadOnlyList<string>>();
            
            foreach (var project in solution.Projects)
            {
                dependencies[project.Path] = project.ProjectReferences;
            }
            
            return dependencies;
        }

        private ProjectInfo? ParseProjectLine(string line, string solutionDirectory)
        {
            try
            {
                // Project("{project-type-guid}") = "ProjectName", "relativePath", "{project-guid}"
                var parts = line.Split('=');
                if (parts.Length != 2) return null;

                var rightPart = parts[1].Trim();
                var quotedParts = ExtractQuotedStrings(rightPart);
                
                if (quotedParts.Count < 3) return null;

                var projectName = quotedParts[0];
                var relativePath = quotedParts[1];
                var projectGuidStr = quotedParts[2];

                // Convert relative path to absolute
                var projectPath = System.IO.Path.Combine(solutionDirectory, relativePath.Replace('\\', System.IO.Path.DirectorySeparatorChar));
                projectPath = System.IO.Path.GetFullPath(projectPath);

                // Parse project GUID
                if (!Guid.TryParse(projectGuidStr.Trim('{', '}'), out var projectGuid))
                    return null;

                return new ProjectInfo(projectName, projectPath, projectGuid, new List<string>());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse project line: {Line}", line);
                return null;
            }
        }

        private static List<string> ExtractQuotedStrings(string text)
        {
            var results = new List<string>();
            var inQuotes = false;
            var currentString = string.Empty;

            for (int i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                
                if (ch == '"')
                {
                    if (inQuotes)
                    {
                        // End of quoted string
                        results.Add(currentString);
                        currentString = string.Empty;
                        inQuotes = false;
                    }
                    else
                    {
                        // Start of quoted string
                        inQuotes = true;
                    }
                }
                else if (inQuotes)
                {
                    currentString += ch;
                }
            }

            return results;
        }

        private static bool IsCSharpProject(string projectPath)
        {
            return projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
        }
    }
}