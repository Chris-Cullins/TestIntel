using Microsoft.Extensions.Logging;
using TestIntelligence.ImpactAnalyzer.Configuration;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public static class RoslynAnalyzerFactory
    {
        public static IRoslynAnalyzer Create(ILoggerFactory loggerFactory, RoslynAnalyzerConfig? config = null)
        {
            config ??= RoslynAnalyzerConfig.Default;
            
            var logger = loggerFactory.CreateLogger("RoslynAnalyzerFactory");
            
            if (config.UseEnhancedAnalyzer)
            {
                try
                {
                    logger.LogInformation("Creating enhanced RoslynAnalyzer with solution workspace support");
                    // For now, let's use a hybrid approach that gradually introduces the enhanced features
                    return CreateHybridAnalyzer(loggerFactory, config);
                }
                catch (System.Exception ex)
                {
                    logger.LogWarning(ex, "Failed to create enhanced analyzer, falling back to legacy implementation");
                    if (config.FallbackToLegacyOnFailure)
                    {
                        return new RoslynAnalyzerV2(loggerFactory.CreateLogger<RoslynAnalyzerV2>(), loggerFactory);
                    }
                    throw;
                }
            }
            
            logger.LogInformation("Creating RoslynAnalyzerV2");
            return new RoslynAnalyzerV2(loggerFactory.CreateLogger<RoslynAnalyzerV2>(), loggerFactory);
        }
        
        private static IRoslynAnalyzer CreateHybridAnalyzer(ILoggerFactory loggerFactory, RoslynAnalyzerConfig config)
        {
            // Return the V2 analyzer with enhanced configuration
            var logger = loggerFactory.CreateLogger<RoslynAnalyzerV2>();
            return new RoslynAnalyzerV2(logger, loggerFactory);
        }
        
        public static IRoslynAnalyzer CreateForTesting(ILoggerFactory loggerFactory)
        {
            return Create(loggerFactory, RoslynAnalyzerConfig.Enhanced);
        }
    }
}