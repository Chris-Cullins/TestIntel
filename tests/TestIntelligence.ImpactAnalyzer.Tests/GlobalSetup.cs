using Microsoft.Build.Locator;
using System.Runtime.CompilerServices;

public static class GlobalSetup
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // Register MSBuild defaults to resolve assembly loading issues
        // This must be called before any MSBuildWorkspace operations
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }
}