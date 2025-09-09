using System.ComponentModel.DataAnnotations;

namespace TestIntelligence.CLI.Commands
{
    /// <summary>
    /// Command model for the compare-tests CLI command.
    /// Contains all parameters needed to compare two test methods.
    /// </summary>
    public class CompareTestsCommand
    {
        /// <summary>
        /// Gets or sets the identifier of the first test method to compare.
        /// Format: "Namespace.ClassName.MethodName"
        /// </summary>
        public string Test1 { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the identifier of the second test method to compare.
        /// Format: "Namespace.ClassName.MethodName" 
        /// </summary>
        public string Test2 { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets test method identifiers for clustering analysis.
        /// Can be a comma-separated list of test method names or patterns.
        /// </summary>
        public string? Tests { get; set; }

        /// <summary>
        /// Gets or sets the scope for multi-test analysis (class, namespace, assembly).
        /// </summary>
        public string? Scope { get; set; }

        /// <summary>
        /// Gets or sets the target identifier for scope analysis.
        /// </summary>
        public string? Target { get; set; }

        /// <summary>
        /// Gets or sets the minimum similarity threshold for clustering.
        /// Default: 0.6
        /// </summary>
        public double SimilarityThreshold { get; set; } = 0.6;

        /// <summary>
        /// Gets or sets the clustering algorithm to use.
        /// Valid values: "hierarchical", "kmeans", "dbscan"
        /// Default: "hierarchical"
        /// </summary>
        public string ClusterAlgorithm { get; set; } = "hierarchical";

        /// <summary>
        /// Gets or sets the path to the solution file (.sln) or project directory.
        /// This is required to analyze the test methods and their coverage.
        /// </summary>
        [Required]
        public string Solution { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the output format for the comparison results.
        /// Supported formats: "text", "json", "html"
        /// Default: "text"
        /// </summary>
        public string Format { get; set; } = "text";

        /// <summary>
        /// Gets or sets the output file path where results should be written.
        /// If not specified, results will be written to console.
        /// </summary>
        public string? Output { get; set; }

        /// <summary>
        /// Gets or sets the depth level for the comparison analysis.
        /// Controls how deep the call graph analysis should go.
        /// Valid values: "shallow", "medium", "deep"
        /// Default: "medium"
        /// </summary>
        public string Depth { get; set; } = "medium";

        /// <summary>
        /// Gets or sets whether to enable verbose output with detailed analysis information.
        /// </summary>
        public bool Verbose { get; set; } = false;

        /// <summary>
        /// Gets or sets whether to include performance metrics in the output.
        /// </summary>
        public bool IncludePerformance { get; set; } = false;

        /// <summary>
        /// Gets or sets the maximum time to spend on analysis (in seconds).
        /// If not specified, no timeout is applied.
        /// </summary>
        public int? TimeoutSeconds { get; set; }

        /// <summary>
        /// Validates the command parameters and returns any validation errors.
        /// </summary>
        /// <returns>List of validation error messages, empty if valid</returns>
        public List<string> Validate()
        {
            var errors = new List<string>();

            // Determine operation mode
            var isTwoTestComparison = !string.IsNullOrWhiteSpace(Test1) && !string.IsNullOrWhiteSpace(Test2);
            var isClusterAnalysis = !string.IsNullOrWhiteSpace(Tests) || !string.IsNullOrWhiteSpace(Scope);

            if (!isTwoTestComparison && !isClusterAnalysis)
            {
                errors.Add("Must specify either Test1/Test2 for pairwise comparison or Tests/Scope for clustering analysis");
            }

            if (isTwoTestComparison && isClusterAnalysis)
            {
                errors.Add("Cannot specify both pairwise comparison (Test1/Test2) and clustering analysis (Tests/Scope) in the same command");
            }

            if (isTwoTestComparison)
            {
                if (string.IsNullOrWhiteSpace(Test1))
                    errors.Add("Test1 identifier is required for pairwise comparison");

                if (string.IsNullOrWhiteSpace(Test2))
                    errors.Add("Test2 identifier is required for pairwise comparison");

                if (Test1.Equals(Test2, StringComparison.OrdinalIgnoreCase))
                    errors.Add("Cannot compare a test method with itself");
            }

            if (isClusterAnalysis)
            {
                if (string.IsNullOrWhiteSpace(Tests) && string.IsNullOrWhiteSpace(Scope))
                    errors.Add("Must specify either Tests or Scope for clustering analysis");

                if (!string.IsNullOrWhiteSpace(Scope) && string.IsNullOrWhiteSpace(Target))
                    errors.Add("Target is required when Scope is specified");

                if (SimilarityThreshold < 0.0 || SimilarityThreshold > 1.0)
                    errors.Add("SimilarityThreshold must be between 0.0 and 1.0");

                if (!IsValidClusterAlgorithm(ClusterAlgorithm))
                    errors.Add($"Invalid cluster algorithm '{ClusterAlgorithm}'. Valid values: hierarchical, kmeans, dbscan");
            }

            if (string.IsNullOrWhiteSpace(Solution))
                errors.Add("Solution path is required");

            if (!IsValidFormat(Format))
                errors.Add($"Invalid format '{Format}'. Supported formats: text, json, html");

            if (!IsValidDepth(Depth))
                errors.Add($"Invalid depth '{Depth}'. Valid values: shallow, medium, deep");

            if (TimeoutSeconds.HasValue && TimeoutSeconds.Value <= 0)
                errors.Add("Timeout seconds must be positive");

            return errors;
        }

        private static bool IsValidFormat(string format) =>
            format.Equals("text", StringComparison.OrdinalIgnoreCase) ||
            format.Equals("json", StringComparison.OrdinalIgnoreCase) ||
            format.Equals("html", StringComparison.OrdinalIgnoreCase);

        private static bool IsValidDepth(string depth) =>
            depth.Equals("shallow", StringComparison.OrdinalIgnoreCase) ||
            depth.Equals("medium", StringComparison.OrdinalIgnoreCase) ||
            depth.Equals("deep", StringComparison.OrdinalIgnoreCase);

        private static bool IsValidClusterAlgorithm(string algorithm) =>
            algorithm.Equals("hierarchical", StringComparison.OrdinalIgnoreCase) ||
            algorithm.Equals("kmeans", StringComparison.OrdinalIgnoreCase) ||
            algorithm.Equals("dbscan", StringComparison.OrdinalIgnoreCase);
    }
}