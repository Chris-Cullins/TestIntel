using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestIntelligence.Core.Services
{
    /// <summary>
    /// Service for resolving assembly paths from various sources like solutions, projects, and directories.
    /// Provides centralized logic for finding compiled assemblies in different configurations and frameworks.
    /// </summary>
    public interface IAssemblyPathResolver
    {
        /// <summary>
        /// Finds test assemblies in a solution by searching through project output directories.
        /// </summary>
        /// <param name="solutionPath">Path to the solution file</param>
        /// <returns>List of test assembly file paths</returns>
        Task<IReadOnlyList<string>> FindTestAssembliesInSolutionAsync(string solutionPath);

        /// <summary>
        /// Finds all assemblies in a solution (both production and test).
        /// </summary>
        /// <param name="solutionPath">Path to the solution file</param>
        /// <returns>List of all assembly file paths</returns>
        Task<IReadOnlyList<string>> FindAllAssembliesInSolutionAsync(string solutionPath);

        /// <summary>
        /// Resolves the output assembly path for a given project file.
        /// </summary>
        /// <param name="projectPath">Path to the project file (.csproj)</param>
        /// <param name="preferredConfiguration">Preferred build configuration (Debug/Release), defaults to Debug</param>
        /// <param name="preferredFramework">Preferred target framework, null to auto-detect</param>
        /// <returns>Path to the output assembly, even if it doesn't exist yet</returns>
        string ResolveAssemblyPath(string projectPath, string? preferredConfiguration = null, string? preferredFramework = null);

        /// <summary>
        /// Finds existing assembly files in a directory and its subdirectories.
        /// </summary>
        /// <param name="directoryPath">Directory to search in</param>
        /// <param name="searchPattern">Search pattern for assembly files, defaults to "*.dll"</param>
        /// <param name="includeTestAssemblies">Whether to include test assemblies in the results</param>
        /// <returns>List of found assembly file paths</returns>
        IReadOnlyList<string> FindAssembliesInDirectory(string directoryPath, string searchPattern = "*.dll", bool includeTestAssemblies = true);

        /// <summary>
        /// Determines if an assembly path represents a test assembly based on naming conventions.
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly file</param>
        /// <returns>True if the assembly appears to be a test assembly</returns>
        bool IsTestAssembly(string assemblyPath);
    }
}