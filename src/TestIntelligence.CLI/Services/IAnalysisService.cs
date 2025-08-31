namespace TestIntelligence.CLI.Services;

/// <summary>
/// Service for analyzing test assemblies and generating analysis reports.
/// </summary>
public interface IAnalysisService
{
    /// <summary>
    /// Analyzes the specified path for test categorization and impact analysis.
    /// </summary>
    Task AnalyzeAsync(string path, string? outputPath, string format, bool verbose);
}

/// <summary>
/// Service for categorizing tests by type.
/// </summary>
public interface ICategorizationService
{
    /// <summary>
    /// Categorizes tests in the specified path.
    /// </summary>
    Task CategorizeAsync(string path, string? outputPath);
}

/// <summary>
/// Service for intelligent test selection.
/// </summary>
public interface ISelectionService
{
    /// <summary>
    /// Selects optimal tests based on code changes and confidence level.
    /// </summary>
    Task SelectAsync(string path, string[] changes, string confidence, string? outputPath, int? maxTests, string? maxTime);
}

/// <summary>
/// Service for analyzing code structure and call graphs.
/// </summary>
public interface ICallGraphService
{
    /// <summary>
    /// Generates a method call graph analysis report for the specified path.
    /// </summary>
    Task AnalyzeCallGraphAsync(string path, string? outputPath, string format, bool verbose, int? maxMethods);
}

/// <summary>
/// Service for formatting output in different formats.
/// </summary>
public interface IOutputFormatter
{
    /// <summary>
    /// Formats the data as JSON.
    /// </summary>
    string FormatAsJson(object data);

    /// <summary>
    /// Formats the data as human-readable text.
    /// </summary>
    string FormatAsText(object data);

    /// <summary>
    /// Writes formatted data to the specified output path or console.
    /// </summary>
    Task WriteOutputAsync(object data, string format, string? outputPath);
}