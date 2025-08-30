namespace TestIntelligence.Core.Assembly
{
    /// <summary>
    /// Represents the different .NET framework versions that can be detected and loaded.
    /// </summary>
    public enum FrameworkVersion
    {
        /// <summary>
        /// Unknown or unsupported framework version.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// .NET Framework 4.8 and earlier versions.
        /// </summary>
        NetFramework48 = 1,

        /// <summary>
        /// .NET Core 2.x and 3.x versions.
        /// </summary>
        NetCore = 2,

        /// <summary>
        /// .NET 5.0 and later versions.
        /// </summary>
        Net5Plus = 3,

        /// <summary>
        /// .NET Standard libraries that can run on multiple frameworks.
        /// </summary>
        NetStandard = 4
    }

    /// <summary>
    /// Extension methods for FrameworkVersion enum.
    /// </summary>
    public static class FrameworkVersionExtensions
    {
        /// <summary>
        /// Determines if the framework version requires special isolation.
        /// </summary>
        public static bool RequiresIsolation(this FrameworkVersion version)
        {
            return version == FrameworkVersion.NetFramework48;
        }

        /// <summary>
        /// Gets a human-readable description of the framework version.
        /// </summary>
        public static string GetDescription(this FrameworkVersion version)
        {
            return version switch
            {
                FrameworkVersion.NetFramework48 => ".NET Framework 4.8",
                FrameworkVersion.NetCore => ".NET Core",
                FrameworkVersion.Net5Plus => ".NET 5+",
                FrameworkVersion.NetStandard => ".NET Standard",
                _ => "Unknown Framework"
            };
        }

        /// <summary>
        /// Determines if the framework supports assembly unloading.
        /// </summary>
        public static bool SupportsUnloading(this FrameworkVersion version)
        {
            return version == FrameworkVersion.Net5Plus || version == FrameworkVersion.NetCore;
        }
    }
}