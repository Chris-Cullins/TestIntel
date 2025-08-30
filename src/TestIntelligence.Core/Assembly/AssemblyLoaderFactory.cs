using System;

namespace TestIntelligence.Core.Assembly
{
    /// <summary>
    /// Factory for creating instances of assembly loaders with proper configuration.
    /// </summary>
    public static class AssemblyLoaderFactory
    {
        /// <summary>
        /// Creates a new CrossFrameworkAssemblyLoader with default configuration.
        /// </summary>
        /// <returns>A configured CrossFrameworkAssemblyLoader instance.</returns>
        public static ICrossFrameworkAssemblyLoader CreateDefault()
        {
            return new CrossFrameworkAssemblyLoader();
        }

        /// <summary>
        /// Creates a new CrossFrameworkAssemblyLoader with console logging.
        /// </summary>
        /// <returns>A configured CrossFrameworkAssemblyLoader instance with console logging.</returns>
        public static ICrossFrameworkAssemblyLoader CreateWithConsoleLogging()
        {
            return new CrossFrameworkAssemblyLoader(new ConsoleAssemblyLoadLogger());
        }

        /// <summary>
        /// Creates a new CrossFrameworkAssemblyLoader with a custom logger.
        /// </summary>
        /// <param name="logger">The custom logger to use.</param>
        /// <returns>A configured CrossFrameworkAssemblyLoader instance.</returns>
        public static ICrossFrameworkAssemblyLoader CreateWithLogger(IAssemblyLoadLogger logger)
        {
            return new CrossFrameworkAssemblyLoader(logger);
        }

        /// <summary>
        /// Creates a new CrossFrameworkAssemblyLoader with silent logging (null logger).
        /// </summary>
        /// <returns>A configured CrossFrameworkAssemblyLoader instance with no logging.</returns>
        public static ICrossFrameworkAssemblyLoader CreateSilent()
        {
            return new CrossFrameworkAssemblyLoader(NullAssemblyLoadLogger.Instance);
        }

        /// <summary>
        /// Creates a framework-specific loader for the specified framework version.
        /// </summary>
        /// <param name="frameworkVersion">The framework version to create a loader for.</param>
        /// <returns>A framework-specific assembly loader.</returns>
        /// <exception cref="NotSupportedException">Thrown when the framework version is not supported.</exception>
        public static IAssemblyLoader CreateFrameworkLoader(FrameworkVersion frameworkVersion)
        {
            return frameworkVersion switch
            {
                FrameworkVersion.NetFramework48 => new Loaders.Framework48LoaderCompatible(),
                FrameworkVersion.NetCore => new Loaders.NetCoreLoaderCompatible(),
                FrameworkVersion.Net5Plus => new Loaders.Net5PlusLoaderCompatible(),
                FrameworkVersion.NetStandard => new Loaders.StandardLoader(),
                _ => throw new NotSupportedException($"Framework version {frameworkVersion} is not supported")
            };
        }

        /// <summary>
        /// Attempts to create a framework-specific loader for the specified framework version.
        /// </summary>
        /// <param name="frameworkVersion">The framework version to create a loader for.</param>
        /// <param name="loader">The created loader, or null if creation failed.</param>
        /// <returns>True if the loader was created successfully, false otherwise.</returns>
        public static bool TryCreateFrameworkLoader(FrameworkVersion frameworkVersion, out IAssemblyLoader? loader)
        {
            try
            {
                loader = CreateFrameworkLoader(frameworkVersion);
                return true;
            }
            catch
            {
                loader = null;
                return false;
            }
        }
    }
}