using System;

namespace TestIntelligence.ImpactAnalyzer.Configuration
{
    public class RoslynAnalyzerConfig
    {
        public bool UseEnhancedAnalyzer { get; set; } = false;
        public bool EnableSolutionWorkspace { get; set; } = true;
        public bool EnableParallelAnalysis { get; set; } = true;
        public int MaxParallelDegree { get; set; } = Environment.ProcessorCount;
        public bool EnableSemanticModelCaching { get; set; } = true;
        public TimeSpan WorkspaceTimeout { get; set; } = TimeSpan.FromMinutes(5);
        public bool FallbackToLegacyOnFailure { get; set; } = true;
        
        public static RoslynAnalyzerConfig Default => new RoslynAnalyzerConfig();
        
        public static RoslynAnalyzerConfig Enhanced => new RoslynAnalyzerConfig
        {
            UseEnhancedAnalyzer = true,
            EnableSolutionWorkspace = true,
            EnableParallelAnalysis = true,
            EnableSemanticModelCaching = true,
            FallbackToLegacyOnFailure = true
        };
    }
}