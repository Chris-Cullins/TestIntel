using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Collections
{
    /// <summary>
    /// Collection definition to ensure MSBuild-dependent tests run sequentially
    /// to avoid concurrency issues with MSBuildWorkspace
    /// </summary>
    [CollectionDefinition("MSBuild Sequential", DisableParallelization = true)]
    public class MSBuildSequentialCollection
    {
    }
}