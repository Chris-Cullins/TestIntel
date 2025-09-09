using System.Collections.Generic;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    /// <summary>
    /// Defines the analysis scope for incremental Roslyn analysis to avoid full-solution work.
    /// </summary>
    public class AnalysisScope
    {
        public AnalysisScope(string solutionPath)
        {
            SolutionPath = solutionPath;
            ChangedFiles = new List<string>();
            ChangedMethods = new List<string>();
            RelevantProjects = new List<string>();
            TargetTests = new List<string>();
            MaxExpansionDepth = 5;
        }

        public string SolutionPath { get; }
        public IList<string> ChangedFiles { get; }
        public IList<string> ChangedMethods { get; }
        public IList<string> RelevantProjects { get; }
        public IList<string> TargetTests { get; }
        public int MaxExpansionDepth { get; set; }
    }
}

