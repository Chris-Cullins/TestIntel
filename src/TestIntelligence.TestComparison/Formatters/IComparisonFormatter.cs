using System.Threading.Tasks;
using TestIntelligence.TestComparison.Models;

namespace TestIntelligence.TestComparison.Formatters;

/// <summary>
/// Interface for formatting test comparison results into different output formats.
/// </summary>
public interface IComparisonFormatter
{
    /// <summary>
    /// Gets the format name that this formatter supports (e.g., "text", "json").
    /// </summary>
    string FormatName { get; }

    /// <summary>
    /// Gets the file extensions supported by this formatter (e.g., [".txt", ".log"]).
    /// </summary>
    string[] SupportedExtensions { get; }

    /// <summary>
    /// Formats a test comparison result into a string representation.
    /// </summary>
    /// <param name="result">The comparison result to format</param>
    /// <param name="options">Optional formatting options</param>
    /// <returns>The formatted string representation</returns>
    Task<string> FormatAsync(TestComparisonResult result, FormatterOptions? options = null);

    /// <summary>
    /// Validates that this formatter can handle the provided result.
    /// </summary>
    /// <param name="result">The comparison result to validate</param>
    /// <returns>True if the formatter can handle this result, false otherwise</returns>
    bool CanFormat(TestComparisonResult result);
}

/// <summary>
/// Options for controlling formatter behavior.
/// </summary>
public class FormatterOptions
{
    /// <summary>
    /// Gets or sets whether to include verbose details in the output.
    /// </summary>
    public bool Verbose { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to include performance metrics in the output.
    /// </summary>
    public bool IncludePerformance { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to use colored output (for text formatters that support it).
    /// </summary>
    public bool UseColors { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum width for text output formatting.
    /// </summary>
    public int MaxWidth { get; set; } = 120;

    /// <summary>
    /// Gets or sets whether to include timestamps in the output.
    /// </summary>
    public bool IncludeTimestamp { get; set; } = true;

    /// <summary>
    /// Gets or sets the level of detail for recommendations.
    /// </summary>
    public RecommendationDetailLevel RecommendationDetail { get; set; } = RecommendationDetailLevel.Standard;
}

/// <summary>
/// Enum for controlling the level of detail in recommendation output.
/// </summary>
public enum RecommendationDetailLevel
{
    /// <summary>
    /// Show only basic recommendation information.
    /// </summary>
    Minimal,

    /// <summary>
    /// Show standard recommendation information with confidence and impact.
    /// </summary>
    Standard,

    /// <summary>
    /// Show detailed recommendation information including rationale and examples.
    /// </summary>
    Detailed
}