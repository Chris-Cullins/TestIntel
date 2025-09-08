using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestIntelligence.TestComparison.Models;

namespace TestIntelligence.TestComparison.Formatters;

/// <summary>
/// Formatter that creates rich, human-readable text output for test comparison results.
/// Includes visual elements like progress bars, icons, and structured layouts.
/// </summary>
public class TextComparisonFormatter : IComparisonFormatter
{
    public string FormatName => "text";
    public string[] SupportedExtensions => new[] { ".txt", ".log", ".report" };

    // Color codes for console output
    private const string Reset = "\u001b[0m";
    private const string Bold = "\u001b[1m";
    private const string Red = "\u001b[31m";
    private const string Green = "\u001b[32m";
    private const string Yellow = "\u001b[33m";
    private const string Blue = "\u001b[34m";
    private const string Magenta = "\u001b[35m";
    private const string Cyan = "\u001b[36m";
    private const string Gray = "\u001b[37m";
    
    public bool CanFormat(TestComparisonResult result) => result != null;

    public Task<string> FormatAsync(TestComparisonResult result, FormatterOptions? options = null)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        
        options ??= new FormatterOptions();
        var output = new StringBuilder();
        
        // Header with title and basic info
        AppendHeader(output, result, options);
        
        // Overall similarity section
        AppendOverallSimilarity(output, result, options);
        
        // Coverage overlap details
        AppendCoverageOverlap(output, result, options);
        
        // Metadata similarity
        AppendMetadataSimilarity(output, result, options);
        
        // Recommendations
        AppendRecommendations(output, result, options);
        
        // Performance metrics if requested
        if (options.IncludePerformance)
        {
            AppendPerformanceMetrics(output, result, options);
        }
        
        // Warnings if any
        AppendWarnings(output, result, options);
        
        // Footer
        AppendFooter(output, result, options);
        
        return Task.FromResult(output.ToString());
    }

    private void AppendHeader(StringBuilder output, TestComparisonResult result, FormatterOptions options)
    {
        var color = options.UseColors;
        
        output.AppendLine(CreateSeparator('═', "Test Comparison Report", options.MaxWidth, color ? Cyan : null));
        output.AppendLine();
        
        // Test identifiers
        output.AppendLine($"📋 {ColorText("Test 1:", Bold, color)} {result.Test1Id}");
        output.AppendLine($"📋 {ColorText("Test 2:", Bold, color)} {result.Test2Id}");
        
        if (options.IncludeTimestamp)
        {
            output.AppendLine($"🕒 {ColorText("Analysis Time:", Bold, color)} {result.AnalysisTimestamp:yyyy-MM-dd HH:mm:ss} UTC");
        }
        
        output.AppendLine($"⏱️  {ColorText("Duration:", Bold, color)} {result.AnalysisDuration.TotalMilliseconds:F0}ms");
        output.AppendLine();
    }

    private void AppendOverallSimilarity(StringBuilder output, TestComparisonResult result, FormatterOptions options)
    {
        var color = options.UseColors;
        
        output.AppendLine(CreateSeparator('─', "Overall Similarity", options.MaxWidth / 2, color ? Blue : null));
        
        // Similarity score with visual bar
        var similarityBar = CreateProgressBar(result.OverallSimilarity, 30, color);
        var similarityColor = GetSimilarityColor(result.OverallSimilarity, color);
        
        output.AppendLine($"📊 {ColorText("Similarity Score:", Bold, color)} {ColorText($"{result.OverallSimilarity:P1}", similarityColor, color)}");
        output.AppendLine($"   {similarityBar}");
        output.AppendLine();
        
        // Summary description
        var description = GetSimilarityDescription(result.OverallSimilarity);
        output.AppendLine($"💡 {ColorText("Analysis:", Bold, color)} {description}");
        output.AppendLine($"   {result.GetSummary()}");
        output.AppendLine();
    }

    private void AppendCoverageOverlap(StringBuilder output, TestComparisonResult result, FormatterOptions options)
    {
        var color = options.UseColors;
        
        output.AppendLine(CreateSeparator('─', "Coverage Overlap Analysis", options.MaxWidth / 2, color ? Green : null));
        
        var overlap = result.CoverageOverlap;
        var overlapBar = CreateProgressBar(overlap.OverlapPercentage / 100.0, 25, color);
        var overlapColor = GetOverlapColor(overlap.OverlapPercentage, color);
        
        output.AppendLine($"🔗 {ColorText("Shared Methods:", Bold, color)} {overlap.SharedProductionMethods}");
        output.AppendLine($"📈 {ColorText("Overlap Percentage:", Bold, color)} {ColorText($"{overlap.OverlapPercentage:F1}%", overlapColor, color)}");
        output.AppendLine($"   {overlapBar}");
        
        if (options.Verbose && overlap.SharedMethods?.Any() == true)
        {
            output.AppendLine();
            output.AppendLine($"🔍 {ColorText("Shared Production Methods:", Bold, color)}");
            
            var methodsToShow = overlap.SharedMethods.Take(options.Verbose ? 10 : 5);
            foreach (var method in methodsToShow)
            {
                output.AppendLine($"   • {method.Method} (confidence: {method.Confidence:P0}, depth: {method.CallDepth})");
                if (options.Verbose && !string.IsNullOrEmpty(method.ContainerName))
                {
                    output.AppendLine($"     📁 {method.ContainerName}");
                }
            }
            
            if (overlap.SharedMethods.Count > (options.Verbose ? 10 : 5))
            {
                var remaining = overlap.SharedMethods.Count - (options.Verbose ? 10 : 5);
                output.AppendLine($"   ... and {remaining} more methods");
            }
        }
        
        output.AppendLine();
    }

    private void AppendMetadataSimilarity(StringBuilder output, TestComparisonResult result, FormatterOptions options)
    {
        var color = options.UseColors;
        
        output.AppendLine(CreateSeparator('─', "Metadata Similarity", options.MaxWidth / 2, color ? Magenta : null));
        
        var metadata = result.MetadataSimilarity;
        
        // Category alignment
        var categoryBar = CreateProgressBar(metadata.CategoryAlignmentScore, 20, color);
        output.AppendLine($"📂 {ColorText("Category Alignment:", Bold, color)} {metadata.CategoryAlignmentScore:P1} {categoryBar}");
        
        // Tag similarity
        var tagBar = CreateProgressBar(metadata.TagOverlapScore, 20, color);
        output.AppendLine($"🏷️  {ColorText("Tag Similarity:", Bold, color)} {metadata.TagOverlapScore:P1} {tagBar}");
        
        // Naming similarity
        var nameBar = CreateProgressBar(metadata.NamingPatternScore, 20, color);
        output.AppendLine($"📝 {ColorText("Naming Similarity:", Bold, color)} {metadata.NamingPatternScore:P1} {nameBar}");
        
        if (options.Verbose && metadata.SharedTags?.Any() == true)
        {
            output.AppendLine();
            output.AppendLine($"🔗 {ColorText("Shared Tags:", Bold, color)} {string.Join(", ", metadata.SharedTags)}");
        }
        
        output.AppendLine();
    }

    private void AppendRecommendations(StringBuilder output, TestComparisonResult result, FormatterOptions options)
    {
        if (result.Recommendations.Count == 0) return;
        
        var color = options.UseColors;
        
        output.AppendLine(CreateSeparator('─', "Optimization Recommendations", options.MaxWidth / 2, color ? Yellow : null));
        
        for (int i = 0; i < result.Recommendations.Count; i++)
        {
            var rec = result.Recommendations[i];
            var effortIcon = GetEffortIcon(rec.EstimatedEffortLevel);
            var effortColor = GetEffortColor(rec.EstimatedEffortLevel, color);
            
            output.AppendLine($"{effortIcon} {ColorText($"{i + 1}. {rec.Type}:", Bold, color)} {rec.Description}");
            
            // Confidence and effort
            var confidenceBar = CreateProgressBar(rec.ConfidenceScore, 15, color);
            output.AppendLine($"   📊 Confidence: {rec.ConfidenceScore:P0} {confidenceBar}");
            output.AppendLine($"   🎯 Effort: {ColorText(rec.EstimatedEffortLevel.ToString(), effortColor, color)}");
            
            // Detailed information based on options
            if (options.RecommendationDetail >= RecommendationDetailLevel.Standard && !string.IsNullOrWhiteSpace(rec.Rationale))
            {
                output.AppendLine($"   💭 Rationale: {rec.Rationale}");
            }
            
            if (options.RecommendationDetail >= RecommendationDetailLevel.Detailed)
            {
                if (!string.IsNullOrWhiteSpace(rec.ImpactDescription))
                {
                    output.AppendLine($"   📈 Impact: {rec.ImpactDescription}");
                }
                if (!string.IsNullOrWhiteSpace(rec.RisksAndConsiderations))
                {
                    output.AppendLine($"   ⚠️  Risks: {rec.RisksAndConsiderations}");
                }
            }
            
            output.AppendLine();
        }
    }

    private void AppendPerformanceMetrics(StringBuilder output, TestComparisonResult result, FormatterOptions options)
    {
        var color = options.UseColors;
        
        output.AppendLine(CreateSeparator('─', "Performance Analysis", options.MaxWidth / 2, color ? Gray : null));
        
        output.AppendLine($"⏱️  {ColorText("Analysis Duration:", Bold, color)} {result.AnalysisDuration:mm\\:ss\\.fff}");
        output.AppendLine($"🔍 {ColorText("Analysis Depth:", Bold, color)} {result.Options.Depth}");
        
        output.AppendLine();
    }

    private void AppendWarnings(StringBuilder output, TestComparisonResult result, FormatterOptions options)
    {
        if (result.Warnings?.Any() != true) return;
        
        var color = options.UseColors;
        
        output.AppendLine(CreateSeparator('─', "Warnings", options.MaxWidth / 2, color ? Yellow : null));
        
        foreach (var warning in result.Warnings)
        {
            output.AppendLine($"⚠️  {ColorText(warning, Yellow, color)}");
        }
        
        output.AppendLine();
    }

    private void AppendFooter(StringBuilder output, TestComparisonResult result, FormatterOptions options)
    {
        var color = options.UseColors;
        
        output.AppendLine(CreateSeparator('═', null, options.MaxWidth, color ? Cyan : null));
        output.AppendLine($"✨ {ColorText("Analysis completed successfully", Green, color)}");
        
        var primaryRecommendation = result.GetPrimaryRecommendation();
        if (primaryRecommendation != null)
        {
            var icon = GetEffortIcon(primaryRecommendation.EstimatedEffortLevel);
            output.AppendLine($"{icon} {ColorText("Primary Recommendation:", Bold, color)} {primaryRecommendation.Description}");
        }
    }

    // Helper methods for visual formatting

    private string CreateProgressBar(double value, int width, bool useColors = true)
    {
        var clampedValue = Math.Max(0, Math.Min(1, value));
        var filledWidth = (int)(clampedValue * width);
        
        var filled = new string('█', filledWidth);
        var empty = new string('░', width - filledWidth);
        
        var color = useColors ? GetProgressColor(clampedValue) : null;
        return $"[{ColorText(filled, color, useColors)}{empty}]";
    }

    private string CreateSeparator(char character, string? title, int width, string? color = null)
    {
        if (string.IsNullOrEmpty(title))
        {
            return new string(character, width);
        }
        
        var titleWithSpaces = $" {title} ";
        var remainingWidth = Math.Max(0, width - titleWithSpaces.Length);
        var leftWidth = remainingWidth / 2;
        var rightWidth = remainingWidth - leftWidth;
        
        var separator = new string(character, leftWidth) + titleWithSpaces + new string(character, rightWidth);
        return ColorText(separator, color, !string.IsNullOrEmpty(color));
    }

    private string ColorText(string text, string? color, bool useColors)
    {
        if (!useColors || string.IsNullOrEmpty(color))
            return text;
        
        return $"{color}{text}{Reset}";
    }

    private string GetSimilarityColor(double similarity, bool useColors)
    {
        if (!useColors) return string.Empty;
        
        return similarity switch
        {
            >= 0.8 => Green,
            >= 0.6 => Yellow,
            >= 0.4 => Yellow,
            _ => Red
        };
    }

    private string GetOverlapColor(double overlapPercentage, bool useColors)
    {
        if (!useColors) return string.Empty;
        
        return overlapPercentage switch
        {
            >= 70 => Green,
            >= 40 => Yellow,
            _ => Red
        };
    }

    private string GetProgressColor(double value)
    {
        return value switch
        {
            >= 0.7 => Green,
            >= 0.4 => Yellow,
            _ => Red
        };
    }

    private string GetEffortColor(EstimatedEffortLevel effort, bool useColors)
    {
        if (!useColors) return string.Empty;
        
        return effort switch
        {
            EstimatedEffortLevel.High => Red,
            EstimatedEffortLevel.Medium => Yellow,
            EstimatedEffortLevel.Low => Green,
            _ => Gray
        };
    }

    private static string GetEffortIcon(EstimatedEffortLevel effort)
    {
        return effort switch
        {
            EstimatedEffortLevel.High => "🔴",
            EstimatedEffortLevel.Medium => "🟡",
            EstimatedEffortLevel.Low => "🟢",
            _ => "⚪"
        };
    }

    private static string GetSimilarityDescription(double similarity)
    {
        return similarity switch
        {
            >= 0.8 => "Tests are highly similar and likely have significant overlap",
            >= 0.6 => "Tests have substantial similarities with some overlap",
            >= 0.4 => "Tests have moderate similarities worth reviewing",
            >= 0.2 => "Tests have limited similarities but some commonalities exist",
            _ => "Tests appear to be largely independent with minimal overlap"
        };
    }
}