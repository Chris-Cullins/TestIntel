using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;

namespace TestIntelligence.Core.Assembly.Loaders
{
    /// <summary>
    /// Assembly loader for .NET 5+ applications compatible with .NET Standard 2.0.
    /// Uses standard loading mechanisms for broad compatibility.
    /// </summary>
    public class Net5PlusLoaderCompatible : StandardLoader
    {
        /// <summary>
        /// Initializes a new instance of the Net5PlusLoaderCompatible.
        /// </summary>
        public Net5PlusLoaderCompatible() : base()
        {
            // Override the supported framework
        }

        /// <inheritdoc />
        public override FrameworkVersion SupportedFramework => FrameworkVersion.Net5Plus;

        /// <inheritdoc />
        protected override bool CanLoadFramework(FrameworkVersion frameworkVersion)
        {
            return frameworkVersion == FrameworkVersion.Net5Plus ||
                   frameworkVersion == FrameworkVersion.NetCore ||
                   frameworkVersion == FrameworkVersion.NetStandard;
        }
    }
}