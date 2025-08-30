using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;

namespace TestIntelligence.Core.Assembly.Loaders
{
    /// <summary>
    /// Assembly loader for .NET Core applications compatible with .NET Standard 2.0.
    /// Uses standard loading mechanisms for broad compatibility.
    /// </summary>
    public class NetCoreLoaderCompatible : StandardLoader
    {
        /// <summary>
        /// Initializes a new instance of the NetCoreLoaderCompatible.
        /// </summary>
        public NetCoreLoaderCompatible() : base()
        {
            // Override the supported framework
        }

        /// <inheritdoc />
        public override FrameworkVersion SupportedFramework => FrameworkVersion.NetCore;

        /// <inheritdoc />
        protected override bool CanLoadFramework(FrameworkVersion frameworkVersion)
        {
            return frameworkVersion == FrameworkVersion.NetCore ||
                   frameworkVersion == FrameworkVersion.NetStandard;
        }
    }
}