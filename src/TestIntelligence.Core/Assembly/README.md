# CrossFrameworkAssemblyLoader

The CrossFrameworkAssemblyLoader provides a unified interface for loading and analyzing test assemblies across different .NET framework versions with proper isolation strategies.

## Features

- **Framework Detection**: Automatically detects .NET Framework 4.8, .NET Core, .NET 5+, and .NET Standard assemblies
- **Isolation Strategies**: Implements appropriate isolation mechanisms for each framework version
- **Metadata Extraction**: Extracts types, methods, and attributes from loaded assemblies
- **Test Framework Support**: Recognizes common test frameworks (NUnit, xUnit, MSTest)
- **Error Handling**: Comprehensive error handling and logging support

## Quick Start

```csharp
using TestIntelligence.Core.Assembly;

// Create a loader with console logging
var loader = AssemblyLoaderFactory.CreateWithConsoleLogging();

// Load an assembly
var result = loader.LoadAssembly(@"C:\path\to\your\test.dll");

if (result.IsSuccess)
{
    var testAssembly = result.Assembly;
    
    // Get test classes
    var testClasses = testAssembly.GetTestClasses();
    Console.WriteLine($"Found {testClasses.Count} test classes");
    
    // Get all test methods
    var testMethods = testAssembly.GetAllTestMethods();
    Console.WriteLine($"Found {testMethods.Count} test methods");
    
    // Check framework references
    bool hasNUnit = testAssembly.HasTestFrameworkReference("NUnit");
    bool hasXUnit = testAssembly.HasTestFrameworkReference("xunit");
}
else
{
    Console.WriteLine($"Failed to load assembly: {string.Join(", ", result.Errors)}");
}

// Don't forget to dispose
loader.Dispose();
```

## Framework-Specific Loaders

You can also create framework-specific loaders:

```csharp
// Create a loader for a specific framework
var netCoreLoader = AssemblyLoaderFactory.CreateFrameworkLoader(FrameworkVersion.NetCore);
var testAssembly = netCoreLoader.LoadAssembly(@"C:\path\to\netcore.dll");
```

## Batch Loading

Load multiple assemblies efficiently:

```csharp
var assemblyPaths = new[]
{
    @"C:\path\to\assembly1.dll",
    @"C:\path\to\assembly2.dll",
    @"C:\path\to\assembly3.dll"
};

var results = await loader.LoadAssembliesAsync(assemblyPaths);

foreach (var kvp in results)
{
    var path = kvp.Key;
    var result = kvp.Value;
    
    if (result.IsSuccess)
    {
        Console.WriteLine($"Successfully loaded: {path}");
    }
    else
    {
        Console.WriteLine($"Failed to load {path}: {string.Join(", ", result.Errors)}");
    }
}
```

## Events

Subscribe to loading events:

```csharp
loader.AssemblyLoaded += (sender, e) =>
{
    Console.WriteLine($"Loaded: {e.TestAssembly.AssemblyName} ({e.FrameworkVersion.GetDescription()})");
};

loader.AssemblyLoadFailed += (sender, e) =>
{
    Console.WriteLine($"Failed to load: {e.AssemblyPath} - {string.Join(", ", e.Errors)}");
};
```

## Custom Logging

Implement your own logger:

```csharp
public class MyCustomLogger : IAssemblyLoadLogger
{
    public void LogInformation(string message, params object[] args)
    {
        // Your logging implementation
    }
    
    // Implement other methods...
}

var loader = AssemblyLoaderFactory.CreateWithLogger(new MyCustomLogger());
```

## Supported Framework Versions

- **.NET Framework 4.8**: Full support with standard loading
- **.NET Core**: Full support with standard loading  
- **.NET 5+**: Full support with standard loading
- **.NET Standard**: Full support as fallback loader

## Thread Safety

The CrossFrameworkAssemblyLoader is thread-safe and can be used concurrently from multiple threads.