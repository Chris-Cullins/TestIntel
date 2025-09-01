using Microsoft.Extensions.Logging;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public static class RoslynAnalyzerFactory
    {
        public static IRoslynAnalyzer Create(ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger<RoslynAnalyzer>();
            return new RoslynAnalyzer(logger, loggerFactory);
        }
        
        public static IRoslynAnalyzer CreateForTesting(ILoggerFactory loggerFactory)
        {
            return Create(loggerFactory);
        }
    }
}