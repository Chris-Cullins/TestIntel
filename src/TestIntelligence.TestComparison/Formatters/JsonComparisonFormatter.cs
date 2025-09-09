using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using TestIntelligence.TestComparison.Models;
using TestIntelligence.TestComparison.Formatters;

namespace TestIntelligence.TestComparison.Formatters;

/// <summary>
/// Formatter that creates clean JSON output for test comparison results.
/// Provides structured, machine-readable output suitable for integration with other tools.
/// </summary>
public class JsonComparisonFormatter : IComparisonFormatter
{
    private readonly JsonSerializerOptions _jsonOptions;

    public string FormatName => "json";
    public string[] SupportedExtensions => new[] { ".json" };

    public JsonComparisonFormatter()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
                new TimeSpanConverter()
            }
        };
    }

    public bool CanFormat(TestComparisonResult result) => result != null;

    public async Task<string> FormatAsync(TestComparisonResult result, FormatterOptions? options = null)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        
        options ??= new FormatterOptions();
        
        // Create a serialization-friendly model
        var outputModel = await CreateOutputModelAsync(result, options);
        
        return JsonSerializer.Serialize(outputModel, _jsonOptions);
    }

    private async Task<ComparisonJsonOutput> CreateOutputModelAsync(TestComparisonResult result, FormatterOptions options)
    {
        return await Task.FromResult(new ComparisonJsonOutput
        {
            FormatVersion = "1.0",
            GeneratedAt = DateTime.UtcNow,
            TestComparison = new TestComparisonJson
            {
                Test1Id = result.Test1Id,
                Test2Id = result.Test2Id,
                OverallSimilarity = result.OverallSimilarity,
                AnalysisDuration = result.AnalysisDuration,
                AnalysisTimestamp = result.AnalysisTimestamp,
                
                CoverageOverlap = new CoverageOverlapJson
                {
                    SharedProductionMethods = result.CoverageOverlap.SharedProductionMethods,
                    OverlapPercentage = result.CoverageOverlap.OverlapPercentage,
                    SharedMethodDetails = options.Verbose && result.CoverageOverlap.SharedMethods != null
                        ? result.CoverageOverlap.SharedMethods.Select(m => new SharedMethodJson
                        {
                            Method = m.Method,
                            ContainerName = m.ContainerName,
                            CallDepth = m.CallDepth,
                            Confidence = m.Confidence
                        }).ToArray()
                        : null,
                    UniqueToTest1 = result.CoverageOverlap.UniqueToTest1,
                    UniqueToTest2 = result.CoverageOverlap.UniqueToTest2
                },
                
                MetadataSimilarity = new MetadataSimilarityJson
                {
                    OverallScore = result.MetadataSimilarity.OverallScore,
                    CategoryAlignmentScore = result.MetadataSimilarity.CategoryAlignmentScore,
                    NamingPatternScore = result.MetadataSimilarity.NamingPatternScore,
                    TagOverlapScore = result.MetadataSimilarity.TagOverlapScore,
                    SharedTags = result.MetadataSimilarity.SharedTags?.ToArray(),
                    ExecutionTimeSimilarity = result.MetadataSimilarity.ExecutionTimeSimilarity
                },
                
                Recommendations = result.Recommendations.Select(r => new RecommendationJson
                {
                    Type = r.Type,
                    EstimatedEffortLevel = r.EstimatedEffortLevel,
                    Description = r.Description,
                    Rationale = options.RecommendationDetail >= RecommendationDetailLevel.Standard ? r.Rationale : null,
                    ConfidenceScore = r.ConfidenceScore,
                    ImpactDescription = r.ImpactDescription,
                    RisksAndConsiderations = options.RecommendationDetail >= RecommendationDetailLevel.Detailed ? r.RisksAndConsiderations : null
                }).ToArray(),
                
                Options = new ComparisonOptionsJson
                {
                    AnalysisDepth = result.Options.Depth,
                    MinimumConfidenceThreshold = result.Options.MinimumConfidenceThreshold
                },
                
                Warnings = result.Warnings?.ToArray(),
                
                Summary = result.GetSummary(),
                PrimaryRecommendation = result.GetPrimaryRecommendation()?.Description
            },
            
            Metadata = new OutputMetadataJson
            {
                IncludePerformance = options.IncludePerformance,
                IncludeTimestamp = options.IncludeTimestamp,
                Verbose = options.Verbose,
                RecommendationDetail = options.RecommendationDetail,
                GeneratedBy = "TestIntelligence.TestComparison"
            }
        });
    }

    // JSON output models for clean serialization
    
    private class ComparisonJsonOutput
    {
        public string FormatVersion { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; }
        public TestComparisonJson TestComparison { get; set; } = new();
        public OutputMetadataJson Metadata { get; set; } = new();
    }

    private class TestComparisonJson
    {
        public string Test1Id { get; set; } = string.Empty;
        public string Test2Id { get; set; } = string.Empty;
        public double OverallSimilarity { get; set; }
        public TimeSpan AnalysisDuration { get; set; }
        public DateTime AnalysisTimestamp { get; set; }
        public CoverageOverlapJson CoverageOverlap { get; set; } = new();
        public MetadataSimilarityJson MetadataSimilarity { get; set; } = new();
        public RecommendationJson[] Recommendations { get; set; } = Array.Empty<RecommendationJson>();
        public ComparisonOptionsJson Options { get; set; } = new();
        public string[]? Warnings { get; set; }
        public string Summary { get; set; } = string.Empty;
        public string? PrimaryRecommendation { get; set; }
    }

    private class CoverageOverlapJson
    {
        public int SharedProductionMethods { get; set; }
        public double OverlapPercentage { get; set; }
        public SharedMethodJson[]? SharedMethodDetails { get; set; }
        public int UniqueToTest1 { get; set; }
        public int UniqueToTest2 { get; set; }
    }

    private class SharedMethodJson
    {
        public string Method { get; set; } = string.Empty;
        public string? ContainerName { get; set; }
        public int CallDepth { get; set; }
        public double Confidence { get; set; }
    }

    private class MetadataSimilarityJson
    {
        public double OverallScore { get; set; }
        public double CategoryAlignmentScore { get; set; }
        public double NamingPatternScore { get; set; }
        public double TagOverlapScore { get; set; }
        public string[]? SharedTags { get; set; }
        public double ExecutionTimeSimilarity { get; set; }
    }

    private class RecommendationJson
    {
        public string Type { get; set; } = string.Empty;
        public EstimatedEffortLevel EstimatedEffortLevel { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? Rationale { get; set; }
        public double ConfidenceScore { get; set; }
        public string? ImpactDescription { get; set; }
        public string? RisksAndConsiderations { get; set; }
    }

    private class ComparisonOptionsJson
    {
        public AnalysisDepth AnalysisDepth { get; set; }
        public double MinimumConfidenceThreshold { get; set; }
    }

    private class OutputMetadataJson
    {
        public bool IncludePerformance { get; set; }
        public bool IncludeTimestamp { get; set; }
        public bool Verbose { get; set; }
        public RecommendationDetailLevel RecommendationDetail { get; set; }
        public string GeneratedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// Custom JSON converter for TimeSpan to format as readable duration.
    /// </summary>
    private class TimeSpanConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return TimeSpan.Parse(reader.GetString() ?? "00:00:00");
        }

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        {
            if (value.TotalDays >= 1)
            {
                writer.WriteStringValue(value.ToString(@"d\.hh\:mm\:ss\.fff"));
            }
            else if (value.TotalHours >= 1)
            {
                writer.WriteStringValue(value.ToString(@"hh\:mm\:ss\.fff"));
            }
            else if (value.TotalMinutes >= 1)
            {
                writer.WriteStringValue(value.ToString(@"mm\:ss\.fff"));
            }
            else
            {
                writer.WriteStringValue(value.ToString(@"ss\.fff") + "s");
            }
        }
    }
}