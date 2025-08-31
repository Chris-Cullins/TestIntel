using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public class DependencyGraph
    {
        public DependencyGraph(IReadOnlyList<string> compilationOrder, IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies)
        {
            CompilationOrder = compilationOrder ?? throw new ArgumentNullException(nameof(compilationOrder));
            Dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        }

        public IReadOnlyList<string> CompilationOrder { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Dependencies { get; }
    }

    public class CircularDependencyException : Exception
    {
        public CircularDependencyException(IReadOnlyList<string> cycle) 
            : base($"Circular dependency detected: {string.Join(" -> ", cycle)}")
        {
            Cycle = cycle ?? throw new ArgumentNullException(nameof(cycle));
        }

        public IReadOnlyList<string> Cycle { get; }
    }

    public class DependencyGraphBuilder
    {
        private readonly ILogger<DependencyGraphBuilder> _logger;

        public DependencyGraphBuilder(ILogger<DependencyGraphBuilder> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public DependencyGraph BuildDependencyGraph(IEnumerable<ProjectDetails> projects)
        {
            if (projects == null)
                throw new ArgumentNullException(nameof(projects));

            var projectList = projects.ToList();
            _logger.LogInformation("Building dependency graph for {ProjectCount} projects", projectList.Count);

            // Build dependency map
            var dependencies = BuildDependencyMap(projectList);
            
            // Detect circular dependencies
            var circularDeps = DetectCircularDependencies(dependencies);
            if (circularDeps.Any())
            {
                var firstCycle = circularDeps.First();
                throw new CircularDependencyException(firstCycle);
            }

            // Get topological order
            var compilationOrder = GetCompilationOrder(dependencies);

            _logger.LogInformation("Dependency graph built successfully with compilation order: {Order}", 
                string.Join(" -> ", compilationOrder.Select(p => System.IO.Path.GetFileNameWithoutExtension(p))));

            return new DependencyGraph(compilationOrder, dependencies);
        }

        public IReadOnlyList<string> GetCompilationOrder(IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies)
        {
            var result = new List<string>();
            var visited = new HashSet<string>();
            var visiting = new HashSet<string>();

            foreach (var project in dependencies.Keys)
            {
                if (!visited.Contains(project))
                {
                    VisitProject(project, dependencies, visited, visiting, result);
                }
            }

            // Reverse to get correct compilation order (dependencies first)
            result.Reverse();
            return result;
        }

        public IReadOnlyList<IReadOnlyList<string>> DetectCircularDependencies(IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies)
        {
            var cycles = new List<IReadOnlyList<string>>();
            var visited = new HashSet<string>();
            var visiting = new HashSet<string>();
            var path = new List<string>();

            foreach (var project in dependencies.Keys)
            {
                if (!visited.Contains(project))
                {
                    DetectCyclesRecursive(project, dependencies, visited, visiting, path, cycles);
                }
            }

            return cycles;
        }

        private IReadOnlyDictionary<string, IReadOnlyList<string>> BuildDependencyMap(IReadOnlyList<ProjectDetails> projects)
        {
            var dependencies = new Dictionary<string, IReadOnlyList<string>>();
            var projectPaths = new HashSet<string>(projects.Select(p => p.ProjectPath));

            foreach (var project in projects)
            {
                // Filter project references to only include projects that are part of this solution
                var validReferences = project.ProjectReferences
                    .Where(refPath => projectPaths.Contains(refPath))
                    .ToList();

                dependencies[project.ProjectPath] = validReferences;

                _logger.LogDebug("Project {ProjectName} depends on: {Dependencies}", 
                    project.Name, string.Join(", ", validReferences.Select(r => System.IO.Path.GetFileNameWithoutExtension(r))));
            }

            return dependencies;
        }

        private void VisitProject(string project, IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies, 
            HashSet<string> visited, HashSet<string> visiting, List<string> result)
        {
            if (visiting.Contains(project))
            {
                // This indicates a circular dependency, but we'll let the dedicated detection method handle it
                return;
            }

            if (visited.Contains(project))
                return;

            visiting.Add(project);

            if (dependencies.TryGetValue(project, out var projectDependencies))
            {
                foreach (var dependency in projectDependencies)
                {
                    VisitProject(dependency, dependencies, visited, visiting, result);
                }
            }

            visiting.Remove(project);
            visited.Add(project);
            result.Add(project);
        }

        private void DetectCyclesRecursive(string project, IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies,
            HashSet<string> visited, HashSet<string> visiting, List<string> path, List<IReadOnlyList<string>> cycles)
        {
            if (visiting.Contains(project))
            {
                // Found a cycle
                var cycleStart = path.IndexOf(project);
                if (cycleStart >= 0)
                {
                    var cycle = path.Skip(cycleStart).Concat(new[] { project }).ToList();
                    cycles.Add(cycle);
                }
                return;
            }

            if (visited.Contains(project))
                return;

            visiting.Add(project);
            path.Add(project);

            if (dependencies.TryGetValue(project, out var projectDependencies))
            {
                foreach (var dependency in projectDependencies)
                {
                    DetectCyclesRecursive(dependency, dependencies, visited, visiting, path, cycles);
                }
            }

            path.RemoveAt(path.Count - 1);
            visiting.Remove(project);
            visited.Add(project);
        }
    }
}