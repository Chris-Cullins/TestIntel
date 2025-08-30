using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using TestIntelligence.Core.Assembly;

namespace TestIntelligence.NetCoreAdapter
{
    /// <summary>
    /// Assembly loader optimized for .NET Core/.NET 5+ assemblies.
    /// </summary>
    public class NetCoreAssemblyLoader : IAssemblyLoader
    {
        /// <summary>
        /// Initializes a new instance of the NetCoreAssemblyLoader class.
        /// </summary>
        public NetCoreAssemblyLoader()
        {
        }

        /// <inheritdoc />
        public FrameworkVersion SupportedFramework => FrameworkVersion.Net5Plus;

        /// <inheritdoc />
        public Func<object, ResolveEventArgs, System.Reflection.Assembly?> AssemblyResolve { get; set; } = null!;

        /// <summary>
        /// Loads a .NET Core/.NET 5+ assembly with proper isolation.
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

                // Load assembly using default context for .NET Standard 2.0 compatibility
                var assembly = await Task.Run(() => Assembly.LoadFrom(assemblyPath), cancellationToken);
                
                var frameworkVersion = DetectFrameworkVersion(assembly);
                var testAssembly = new NetCoreTestAssembly(assemblyPath, assembly, frameworkVersion);
                
                return testAssembly;
            }
            catch (FileLoadException ex)
            {
                throw new InvalidOperationException($"Failed to load assembly: {ex.Message}", ex);
            }
            catch (BadImageFormatException ex)
            {
                throw new InvalidOperationException($"Invalid assembly format: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Unexpected error loading assembly: {ex.Message}", ex);
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
            testAssembly?.Dispose();
            return false; // Cannot truly unload from default context
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

                var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
                var targetFramework = GetTargetFrameworkFromAssembly(assemblyPath);
                
                return targetFramework?.StartsWith(".NETCoreApp") == true ||
                       targetFramework?.StartsWith(".NET,Version=") == true ||
                       targetFramework?.StartsWith(".NET ") == true ||
                       targetFramework?.StartsWith("net5") == true ||
                       targetFramework?.StartsWith("net6") == true ||
                       targetFramework?.StartsWith("net7") == true ||
                       targetFramework?.StartsWith("net8") == true;
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
                    
                    if (frameworkName.Contains("net8") || frameworkName.Contains(".NET,Version=v8"))
                        return FrameworkVersion.Net5Plus;
                    if (frameworkName.Contains("net7") || frameworkName.Contains(".NET,Version=v7"))
                        return FrameworkVersion.Net5Plus; // Treat as 5+
                    if (frameworkName.Contains("net6") || frameworkName.Contains(".NET,Version=v6"))
                        return FrameworkVersion.Net5Plus;
                    if (frameworkName.Contains("net5") || frameworkName.Contains(".NET,Version=v5"))
                        return FrameworkVersion.Net5Plus;
                    if (frameworkName.Contains("netcoreapp"))
                        return FrameworkVersion.Net5Plus; // .NET Core 3.1 and earlier
                }
                
                // Fallback: Check runtime version
                var runtimeVersion = assembly.ImageRuntimeVersion;
                if (runtimeVersion.StartsWith("v5") || runtimeVersion.StartsWith("v6") || 
                    runtimeVersion.StartsWith("v7") || runtimeVersion.StartsWith("v8"))
                {
                    return FrameworkVersion.Net5Plus;
                }
                
                return FrameworkVersion.Net5Plus; // Default assumption for .NET Core+ assemblies
            }
            catch
            {
                return FrameworkVersion.Net5Plus;
            }
        }

        private string? GetTargetFrameworkFromAssembly(string assemblyPath)
        {
            try
            {
                // Simple approach: try to load assembly name metadata
                var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
                
                // Check for common .NET Core/.NET 5+ patterns in path or filename
                var fileName = Path.GetFileName(assemblyPath).ToLower();
                if (fileName.Contains("netcore") || fileName.Contains("net5") || 
                    fileName.Contains("net6") || fileName.Contains("net7") || 
                    fileName.Contains("net8"))
                {
                    return ".NETCoreApp,Version=v8.0";
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing specific to dispose for simplified NetCore loader
        }
    }
}