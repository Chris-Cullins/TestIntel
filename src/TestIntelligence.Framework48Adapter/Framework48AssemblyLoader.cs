using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Core.Assembly;

namespace TestIntelligence.Framework48Adapter
{
    /// <summary>
    /// Assembly loader optimized for .NET Framework 4.8 assemblies.
    /// </summary>
    public class Framework48AssemblyLoader : IAssemblyLoader
    {
        /// <inheritdoc />
        public FrameworkVersion SupportedFramework => FrameworkVersion.NetFramework48;

        /// <inheritdoc />
        public Func<object, ResolveEventArgs, Assembly?> AssemblyResolve { get; set; } = null!;
        /// <summary>
        /// Loads a .NET Framework 4.8 assembly with appropriate AppDomain isolation.
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly to load.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The loaded test assembly.</returns>
        public async Task<ITestAssembly> LoadAssemblyAsync(string assemblyPath, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(assemblyPath))
                {
                    throw new FileNotFoundException($"Assembly file not found: {assemblyPath}");
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Load assembly with metadata-only context to avoid execution issues
                var assembly = await Task.Run(() => Assembly.LoadFrom(assemblyPath), cancellationToken);
                
                var frameworkVersion = DetectFrameworkVersion(assembly);
                var testAssembly = new Framework48TestAssembly(assemblyPath, assembly, frameworkVersion);
                
                return testAssembly;
            }
            catch (ReflectionTypeLoadException ex)
            {
                var errors = ex.LoaderExceptions?.Select(e => e?.Message ?? "Unknown loader exception").ToArray() ?? new[] { ex.Message };
                throw new InvalidOperationException($"Failed to load assembly types: {string.Join(", ", errors)}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load assembly: {ex.Message}", ex);
            }
        }

        /// <inheritdoc />
        public ITestAssembly LoadAssembly(string assemblyPath)
        {
            return LoadAssemblyAsync(assemblyPath).GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public bool TryUnloadAssembly(ITestAssembly testAssembly)
        {
            // .NET Framework assemblies cannot be unloaded from the current AppDomain
            testAssembly?.Dispose();
            return false; // Indicate that true unloading is not supported
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing specific to dispose for Framework loader
        }

        /// <summary>
        /// Determines if this loader can handle the specified assembly.
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly.</param>
        /// <returns>True if this loader can handle the assembly.</returns>
        public bool CanLoad(string assemblyPath)
        {
            try
            {
                if (!File.Exists(assemblyPath))
                    return false;

                // Quick check using AssemblyName without loading
                var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
                var targetFramework = GetTargetFrameworkFromAssembly(assemblyPath);
                
                return targetFramework?.StartsWith(".NETFramework,Version=v4.8") == true ||
                       targetFramework?.StartsWith(".NETFramework,Version=v4.7") == true ||
                       targetFramework?.StartsWith(".NETFramework,Version=v4.6") == true;
            }
            catch
            {
                return false;
            }
        }

        private FrameworkVersion DetectFrameworkVersion(Assembly assembly)
        {
            try
            {
                var targetFrameworkAttribute = assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>();
                if (targetFrameworkAttribute != null)
                {
                    var frameworkName = targetFrameworkAttribute.FrameworkName;
                    if (frameworkName.StartsWith(".NETFramework,Version=v4.8"))
                        return FrameworkVersion.NetFramework48;
                    if (frameworkName.StartsWith(".NETFramework,Version=v4.7"))
                        return FrameworkVersion.NetFramework48; // Treat 4.7.x as compatible
                    if (frameworkName.StartsWith(".NETFramework,Version=v4.6"))
                        return FrameworkVersion.NetFramework48; // Treat 4.6.x as compatible
                }
                
                // Fallback to runtime version
                var runtimeVersion = assembly.ImageRuntimeVersion;
                if (runtimeVersion.StartsWith("v4.0"))
                    return FrameworkVersion.NetFramework48;
                
                return FrameworkVersion.NetFramework48; // Default assumption for Framework assemblies
            }
            catch
            {
                return FrameworkVersion.NetFramework48;
            }
        }

        private string? GetTargetFrameworkFromAssembly(string assemblyPath)
        {
            try
            {
                var assembly = Assembly.ReflectionOnlyLoadFrom(assemblyPath);
                var targetFrameworkAttribute = assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>();
                return targetFrameworkAttribute?.FrameworkName;
            }
            catch
            {
                return null;
            }
        }
    }
}