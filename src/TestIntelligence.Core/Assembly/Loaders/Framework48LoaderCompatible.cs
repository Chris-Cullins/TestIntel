using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;

namespace TestIntelligence.Core.Assembly.Loaders
{
    /// <summary>
    /// Assembly loader for .NET Framework 4.8 applications compatible with .NET Standard 2.0.
    /// Uses standard loading mechanisms since AppDomain isolation is not available in .NET Standard.
    /// </summary>
    public class Framework48LoaderCompatible : StandardLoader
    {
        /// <summary>
        /// Initializes a new instance of the Framework48LoaderCompatible.
        /// </summary>
        public Framework48LoaderCompatible() : base()
        {
            // Override the supported framework
        }

        /// <inheritdoc />
        public override FrameworkVersion SupportedFramework => FrameworkVersion.NetFramework48;

        /// <inheritdoc />
        protected override bool CanLoadFramework(FrameworkVersion frameworkVersion)
        {
            return frameworkVersion == FrameworkVersion.NetFramework48;
        }
    }
}