# Test Comparison Feature - Phase 2 Implementation Plan

## Phase Overview
**Duration**: 3-4 weeks  
**Goal**: Advanced Analysis & Multi-Test Support with execution path similarity and test clustering  
**Success Criteria**: Working execution path analysis, multi-test clustering, and HTML report generation  
**Prerequisites**: Phase 1 must be complete with all tests passing

## Phase 2 Deliverables
- Execution path similarity analysis using graph-based comparison
- Multi-test clustering with hierarchical algorithms
- Rich HTML report generation with interactive elements
- Performance optimizations for large test sets

## Week 5-6: Execution Path Analysis

### 5.1 Execution Path Similarity Infrastructure  
**Estimated Time**: 3-4 days  
**Definition of Done**: Graph-based execution path comparison working with existing ExecutionTrace data

#### Tasks:
1. **Extend ISimilarityCalculator Interface**
   ```csharp
   public interface ISimilarityCalculator
   {
       // Existing methods...
       
       /// <summary>
       /// Calculates similarity between execution paths using graph topology analysis.
       /// </summary>
       /// <param name="trace1">Execution trace from first test</param>
       /// <param name="trace2">Execution trace from second test</param>
       /// <param name="options">Path comparison configuration</param>
       /// <returns>Similarity score between 0.0 and 1.0</returns>
       double CalculateExecutionPathSimilarity(
           ExecutionTrace trace1, 
           ExecutionTrace trace2, 
           PathComparisonOptions? options = null);
   }
   ```

2. **Create ExecutionPathSimilarity Data Model**
   ```csharp
   /// <summary>
   /// Results of execution path similarity analysis between two tests.
   /// </summary>
   public class ExecutionPathSimilarity
   {
       public double JaccardSimilarity { get; init; }
       public double CosineSimilarity { get; init; }
       public int SharedExecutionNodes { get; init; }
       public int TotalUniqueNodes { get; init; }
       public required IReadOnlyList<PathDivergencePoint> DivergencePoints { get; init; }
       public double StructuralSimilarity { get; init; }
       public double SequentialSimilarity { get; init; }
   }

   public class PathDivergencePoint
   {
       public required string Method { get; init; }
       public required string Branch { get; init; }
       public required string DivergenceType { get; init; } // "conditional", "loop", "exception"
       public int Depth { get; init; }
   }
   ```

3. **Create PathComparisonOptions Configuration**
   ```csharp
   public class PathComparisonOptions
   {
       public bool IncludeStructuralSimilarity { get; init; } = true;
       public bool IncludeSequentialSimilarity { get; init; } = true;
       public double SequentialWeight { get; init; } = 0.6;
       public double StructuralWeight { get; init; } = 0.4;
       public int MaxAnalysisDepth { get; init; } = 20;
       public bool IgnoreFrameworkCalls { get; init; } = true;
   }
   ```

#### Acceptance Criteria:
- [ ] Data models support all required similarity metrics
- [ ] Configuration options provide flexibility for different use cases
- [ ] Models integrate cleanly with existing ExecutionTrace infrastructure  
- [ ] JSON serialization works correctly for all new models
- [ ] XML documentation complete for all public interfaces

### 5.2 Graph-Based Similarity Algorithms
**Estimated Time**: 4-5 days  
**Definition of Done**: Accurate graph topology and sequential similarity calculations

#### Tasks:
1. **Implement ExecutionPathAnalyzer Service**
   ```csharp
   public class ExecutionPathAnalyzer
   {
       private readonly ILogger<ExecutionPathAnalyzer> _logger;

       public double CalculateStructuralSimilarity(
           ExecutionTrace trace1, 
           ExecutionTrace trace2, 
           PathComparisonOptions options)
       {
           // Convert execution traces to graph representations
           var graph1 = BuildExecutionGraph(trace1);
           var graph2 = BuildExecutionGraph(trace2);
           
           // Calculate graph similarity using:
           // 1. Node overlap (Jaccard on method sets)
           // 2. Edge overlap (Jaccard on call relationships) 
           // 3. Graph structure metrics (depth, branching factor)
           
           return CombineStructuralMetrics(graph1, graph2, options);
       }

       public double CalculateSequentialSimilarity(
           ExecutionTrace trace1,
           ExecutionTrace trace2,
           PathComparisonOptions options)
       {
           // Calculate sequence-based similarity:
           // 1. Longest Common Subsequence (LCS) of method calls
           // 2. Edit distance between call sequences
           // 3. N-gram similarity for call patterns
           
           return CombineSequentialMetrics(trace1, trace2, options);
       }

       public IReadOnlyList<PathDivergencePoint> FindDivergencePoints(
           ExecutionTrace trace1,
           ExecutionTrace trace2)
       {
           // Identify where execution paths diverge:
           // 1. Conditional branches taken differently
           // 2. Loop iterations that differ
           // 3. Exception handling paths
           // 4. Method call order differences
       }
   }
   ```

2. **Create ExecutionGraph Data Structure**
   ```csharp
   public class ExecutionGraph
   {
       public IReadOnlySet<string> Nodes { get; init; } = new HashSet<string>();
       public IReadOnlySet<ExecutionEdge> Edges { get; init; } = new HashSet<ExecutionEdge>();
       public IReadOnlyDictionary<string, int> NodeDepths { get; init; } = new Dictionary<string, int>();
       public IReadOnlyDictionary<string, int> CallFrequencies { get; init; } = new Dictionary<string, int>();
       
       public double CalculateAverageDepth() => NodeDepths.Values.DefaultIfEmpty(0).Average();
       public double CalculateBranchingFactor() => Edges.Count / (double)Math.Max(1, Nodes.Count - 1);
   }

   public record ExecutionEdge(string FromMethod, string ToMethod, int CallDepth, double Weight);
   ```

3. **Implement Comprehensive Unit Tests**
   ```csharp
   public class ExecutionPathAnalyzerTests
   {
       [Fact]
       public void CalculateStructuralSimilarity_IdenticalTraces_Returns100Percent()
       {
           var trace = CreateSampleExecutionTrace();
           var similarity = _analyzer.CalculateStructuralSimilarity(trace, trace, new PathComparisonOptions());
           similarity.Should().Be(1.0);
       }

       [Fact]
       public void CalculateStructuralSimilarity_CompletelyDifferentTraces_ReturnsZero()
       [Fact]
       public void CalculateSequentialSimilarity_SameSequenceDifferentStructure_ReturnsHighScore()
       [Fact]
       public void FindDivergencePoints_WithConditionalBranches_IdentifiesCorrectDivergence()
       
       // Performance tests
       [Fact]
       public void CalculateExecutionPathSimilarity_LargeTraces_CompletesUnder2Seconds()
       {
           var largeTrace1 = CreateLargeExecutionTrace(1000); // 1000 method calls
           var largeTrace2 = CreateLargeExecutionTrace(1000);
           
           var stopwatch = Stopwatch.StartNew();
           var similarity = _analyzer.CalculateExecutionPathSimilarity(largeTrace1, largeTrace2, new());
           stopwatch.Stop();
           
           stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
       }
   }
   ```

#### Acceptance Criteria:
- [ ] Structural similarity algorithm correctly measures graph topology overlap
- [ ] Sequential similarity captures method call order patterns
- [ ] Divergence point detection identifies meaningful execution differences
- [ ] Performance meets requirements (<2 seconds for typical execution traces)
- [ ] Unit tests achieve >90% code coverage
- [ ] Algorithms handle edge cases (empty traces, cyclic calls, deep recursion)

### 5.3 Integration with TestComparisonService
**Estimated Time**: 2-3 days  
**Definition of Done**: ExecutionPathSimilarity integrated into main comparison workflow

#### Tasks:
1. **Update TestComparisonService**
   ```csharp
   public class TestComparisonService : ITestComparisonService
   {
       private readonly ITestExecutionTracer _executionTracer; // New dependency
       private readonly ExecutionPathAnalyzer _pathAnalyzer;   // New dependency

       public async Task<TestComparisonResult> CompareTestsAsync(
           string test1Id, 
           string test2Id, 
           string solutionPath, 
           ComparisonOptions options, 
           CancellationToken cancellationToken = default)
       {
           // Existing coverage analysis...
           var coverageOverlap = await AnalyzeCoverageOverlapAsync(...);
           
           // NEW: Add execution path analysis
           ExecutionPathSimilarity? pathSimilarity = null;
           if (options.Depth >= AnalysisDepth.Medium)
           {
               var trace1 = await _executionTracer.TraceTestExecutionAsync(test1Id, solutionPath, cancellationToken);
               var trace2 = await _executionTracer.TraceTestExecutionAsync(test2Id, solutionPath, cancellationToken);
               
               pathSimilarity = AnalyzeExecutionPathSimilarity(trace1, trace2, options.PathComparison);
           }
           
           // Update overall similarity calculation to include path similarity
           var overallSimilarity = CalculateOverallSimilarity(coverageOverlap, pathSimilarity, metadataSimilarity);
           
           return new TestComparisonResult
           {
               // Existing properties...
               ExecutionPathSimilarity = pathSimilarity,
               // Updated overall similarity
               OverallSimilarity = overallSimilarity
           };
       }
   }
   ```

2. **Update TestComparisonResult Model**
   ```csharp
   public class TestComparisonResult
   {
       // Existing properties...
       public ExecutionPathSimilarity? ExecutionPathSimilarity { get; init; }
       // Overall similarity now includes path analysis
   }
   ```

3. **Create Integration Tests**
   ```csharp
   public class ExecutionPathIntegrationTests : IClassFixture<TestIntelligenceFixture>
   {
       [Fact]
       public async Task CompareTests_WithExecutionTraces_IncludesPathSimilarity()
       {
           var result = await _comparisonService.CompareTestsAsync(
               "TestIntelligence.Core.Tests.Discovery.NUnitTestDiscoveryTests.DiscoverTestsAsync_ValidAssembly_ReturnsAllTests",
               "TestIntelligence.Core.Tests.Discovery.NUnitTestDiscoveryTests.DiscoverTestsAsync_NoTests_ReturnsEmpty",
               "TestIntelligence.sln",
               new ComparisonOptions { Depth = AnalysisDepth.Medium },
               CancellationToken.None);
               
           result.ExecutionPathSimilarity.Should().NotBeNull();
           result.ExecutionPathSimilarity!.JaccardSimilarity.Should().BeInRange(0, 1);
           result.ExecutionPathSimilarity.SharedExecutionNodes.Should().BeGreaterOrEqualTo(0);
       }
   }
   ```

#### Acceptance Criteria:
- [ ] Execution path analysis integrates seamlessly with existing comparison workflow
- [ ] Performance impact is minimal (adds <1 second to analysis time)
- [ ] Integration tests pass with real TestIntelligence test methods
- [ ] Overall similarity calculation properly weights all factors
- [ ] Error handling gracefully manages execution tracing failures

## Week 6-7: Multi-Test Clustering

### 6.1 Test Clustering Infrastructure
**Estimated Time**: 3-4 days  
**Definition of Done**: Hierarchical clustering algorithm working with similarity matrices

#### Tasks:
1. **Extend ITestComparisonService Interface**
   ```csharp
   public interface ITestComparisonService
   {
       // Existing methods...
       
       /// <summary>
       /// Analyzes multiple tests to identify clusters of similar tests.
       /// </summary>
       /// <param name="testIds">Collection of test method identifiers</param>
       /// <param name="solutionPath">Path to solution file</param>
       /// <param name="options">Clustering configuration options</param>
       /// <param name="cancellationToken">Cancellation token</param>
       /// <returns>Test cluster analysis with groupings and statistics</returns>
       Task<TestClusterAnalysis> AnalyzeTestClustersAsync(
           IEnumerable<string> testIds, 
           string solutionPath, 
           ClusteringOptions options, 
           CancellationToken cancellationToken = default);
   }
   ```

2. **Create TestClusterAnalysis Data Models**
   ```csharp
   /// <summary>
   /// Results of clustering analysis on multiple test methods.
   /// </summary>
   public class TestClusterAnalysis
   {
       public required IReadOnlyList<TestCluster> Clusters { get; init; }
       public required IReadOnlyList<string> UnclusteredTests { get; init; }
       public ClusteringStatistics Statistics { get; init; }
       public ClusteringOptions Options { get; init; }
       public DateTime AnalysisTimestamp { get; init; }
   }

   public class TestCluster
   {
       public int ClusterId { get; init; }
       public required IReadOnlyList<string> TestIds { get; init; }
       public double IntraClusterSimilarity { get; init; }
       public double CohesionScore { get; init; }
       public required IReadOnlyList<OptimizationRecommendation> Recommendations { get; init; }
       public ClusterCharacteristics Characteristics { get; init; }
   }

   public class ClusterCharacteristics
   {
       public required IReadOnlySet<string> CommonMethods { get; init; }
       public required IReadOnlySet<TestCategory> Categories { get; init; }
       public required IReadOnlySet<string> CommonTags { get; init; }
       public double AverageExecutionTime { get; init; }
       public string SuggestedName { get; init; } = string.Empty;
   }

   public class ClusteringStatistics
   {
       public int TotalTests { get; init; }
       public int NumberOfClusters { get; init; }
       public double SilhouetteScore { get; init; }
       public double AverageIntraClusterSimilarity { get; init; }
       public double AverageInterClusterSimilarity { get; init; }
   }
   ```

3. **Create ClusteringOptions Configuration**
   ```csharp
   public class ClusteringOptions
   {
       public double SimilarityThreshold { get; init; } = 0.6;
       public ClusteringAlgorithm Algorithm { get; init; } = ClusteringAlgorithm.Hierarchical;
       public int MaxClusters { get; init; } = 20;
       public int MinClusterSize { get; init; } = 2;
       public double MinIntraClusterSimilarity { get; init; } = 0.5;
       public ComparisonOptions ComparisonOptions { get; init; } = new();
   }

   public enum ClusteringAlgorithm
   {
       Hierarchical,
       KMeans,
       DBSCAN
   }
   ```

#### Acceptance Criteria:
- [ ] Data models support comprehensive clustering analysis
- [ ] Configuration options provide flexibility for different clustering scenarios
- [ ] Models integrate with existing TestComparisonResult infrastructure
- [ ] JSON serialization works for all clustering data structures
- [ ] XML documentation complete for all public interfaces

### 6.2 Clustering Algorithm Implementation  
**Estimated Time**: 4-5 days  
**Definition of Done**: Working hierarchical clustering with quality metrics

#### Tasks:
1. **Create TestClusteringService**
   ```csharp
   public class TestClusteringService
   {
       private readonly ITestComparisonService _comparisonService;
       private readonly ILogger<TestClusteringService> _logger;

       public async Task<TestClusterAnalysis> PerformHierarchicalClusteringAsync(
           IEnumerable<string> testIds,
           string solutionPath,
           ClusteringOptions options,
           CancellationToken cancellationToken)
       {
           // 1. Build similarity matrix for all test pairs
           var similarityMatrix = await BuildSimilarityMatrixAsync(testIds, solutionPath, options, cancellationToken);
           
           // 2. Perform hierarchical clustering using complete linkage
           var clusters = PerformHierarchicalClustering(similarityMatrix, options.SimilarityThreshold);
           
           // 3. Calculate cluster quality metrics
           var statistics = CalculateClusteringStatistics(clusters, similarityMatrix);
           
           // 4. Generate recommendations for each cluster
           var enrichedClusters = await EnrichClustersWithRecommendationsAsync(clusters, solutionPath, cancellationToken);
           
           return new TestClusterAnalysis
           {
               Clusters = enrichedClusters,
               Statistics = statistics,
               Options = options,
               AnalysisTimestamp = DateTime.UtcNow,
               UnclusteredTests = FindUnclusteredTests(testIds, clusters)
           };
       }

       private async Task<SimilarityMatrix> BuildSimilarityMatrixAsync(
           IEnumerable<string> testIds,
           string solutionPath,
           ClusteringOptions options,
           CancellationToken cancellationToken)
       {
           var testIdList = testIds.ToList();
           var matrix = new SimilarityMatrix(testIdList.Count);
           
           // Calculate pairwise similarities (can be parallelized)
           var tasks = new List<Task>();
           for (int i = 0; i < testIdList.Count; i++)
           {
               for (int j = i + 1; j < testIdList.Count; j++)
               {
                   var task = CalculateAndStoreSimilarity(i, j, testIdList, solutionPath, matrix, options, cancellationToken);
                   tasks.Add(task);
               }
           }
           
           await Task.WhenAll(tasks);
           return matrix;
       }
   }
   ```

2. **Implement SimilarityMatrix Data Structure**
   ```csharp
   public class SimilarityMatrix
   {
       private readonly double[,] _matrix;
       private readonly IReadOnlyList<string> _testIds;

       public SimilarityMatrix(IReadOnlyList<string> testIds)
       {
           _testIds = testIds;
           _matrix = new double[testIds.Count, testIds.Count];
           
           // Initialize diagonal to 1.0 (self-similarity)
           for (int i = 0; i < testIds.Count; i++)
           {
               _matrix[i, i] = 1.0;
           }
       }

       public double GetSimilarity(int index1, int index2)
       {
           return _matrix[Math.Min(index1, index2), Math.Max(index1, index2)];
       }

       public void SetSimilarity(int index1, int index2, double similarity)
       {
           _matrix[Math.Min(index1, index2), Math.Max(index1, index2)] = similarity;
           _matrix[Math.Max(index1, index2), Math.Min(index1, index2)] = similarity;
       }
       
       public IEnumerable<(int Index1, int Index2, double Similarity)> GetHighSimilarityPairs(double threshold)
       {
           for (int i = 0; i < _testIds.Count; i++)
           {
               for (int j = i + 1; j < _testIds.Count; j++)
               {
                   var similarity = _matrix[i, j];
                   if (similarity >= threshold)
                   {
                       yield return (i, j, similarity);
                   }
               }
           }
       }
   }
   ```

3. **Create Comprehensive Unit Tests**
   ```csharp
   public class TestClusteringServiceTests
   {
       [Fact]
       public async Task PerformHierarchicalClustering_WithSimilarTests_CreatesClusters()
       {
           var testIds = new[]
           {
               "Test.A1", "Test.A2", "Test.A3", // Should cluster together
               "Test.B1", "Test.B2",           // Should cluster together  
               "Test.C1"                       // Should remain unclustered
           };
           
           // Mock similarity service to return high similarity for A tests and B tests
           var result = await _clusteringService.PerformHierarchicalClusteringAsync(
               testIds, "test.sln", new ClusteringOptions { SimilarityThreshold = 0.7 }, CancellationToken.None);
               
           result.Clusters.Should().HaveCount(2);
           result.UnclusteredTests.Should().Contain("Test.C1");
       }

       [Fact] 
       public async Task BuildSimilarityMatrix_LargeTestSet_CompletesInReasonableTime()
       {
           // Test with 50 test methods (1225 pairwise comparisons)
           var testIds = GenerateTestIds(50);
           
           var stopwatch = Stopwatch.StartNew();
           await _clusteringService.PerformHierarchicalClusteringAsync(
               testIds, "test.sln", new ClusteringOptions(), CancellationToken.None);
           stopwatch.Stop();
           
           // Should complete within 30 seconds for 50 tests
           stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
       }
   }
   ```

#### Acceptance Criteria:
- [ ] Hierarchical clustering algorithm correctly groups similar tests
- [ ] Similarity matrix calculation is efficient and accurate
- [ ] Cluster quality metrics (silhouette score) are implemented correctly
- [ ] Performance meets requirements (50 tests clustered in <30 seconds)
- [ ] Unit tests achieve >90% code coverage
- [ ] Algorithm handles edge cases (all similar, all different, single test)

### 6.3 CLI Integration for Multi-Test Analysis
**Estimated Time**: 2-3 days  
**Definition of Done**: CLI supports pattern-based test selection and clustering

#### Tasks:
1. **Extend CompareTestsCommand for Clustering**
   ```csharp
   public class CompareTestsCommand
   {
       // Existing properties...
       
       [Option("--tests", Required = false,
               HelpText = "Pattern or list of test method identifiers for clustering analysis")]
       public IEnumerable<string> Tests { get; set; } = Array.Empty<string>();

       [Option("--scope", Required = false,
               HelpText = "Analysis scope (class, namespace, assembly)")]
       public string? Scope { get; set; }

       [Option("--target", Required = false,
               HelpText = "Target identifier for scope analysis")]
       public string? Target { get; set; }

       [Option("--similarity-threshold", Default = 0.6,
               HelpText = "Minimum similarity score for clustering")]
       public double SimilarityThreshold { get; set; } = 0.6;

       [Option("--cluster-algorithm", Default = "hierarchical",
               HelpText = "Clustering algorithm (hierarchical, kmeans, dbscan)")]
       public string ClusterAlgorithm { get; set; } = "hierarchical";
   }
   ```

2. **Update CompareTestsCommandHandler**
   ```csharp
   public async Task<int> HandleAsync(CompareTestsCommand command, CancellationToken cancellationToken)
   {
       try
       {
           if (!string.IsNullOrEmpty(command.Test1Id) && !string.IsNullOrEmpty(command.Test2Id))
           {
               // Handle two-test comparison (existing functionality)
               return await HandleTwoTestComparisonAsync(command, cancellationToken);
           }
           else if (command.Tests.Any() || !string.IsNullOrEmpty(command.Scope))
           {
               // Handle multi-test clustering analysis (new functionality)
               return await HandleClusterAnalysisAsync(command, cancellationToken);
           }
           else
           {
               _logger.LogError("Must specify either --test1/--test2 for comparison or --tests/--scope for clustering");
               return 1;
           }
       }
       catch (Exception ex)
       {
           _logger.LogError(ex, "Error during test comparison analysis");
           return 1;
       }
   }

   private async Task<int> HandleClusterAnalysisAsync(CompareTestsCommand command, CancellationToken cancellationToken)
   {
       // Resolve test IDs from patterns or scope
       var testIds = await ResolveTestIdsAsync(command, cancellationToken);
       
       if (!testIds.Any())
       {
           _logger.LogWarning("No tests found matching the specified criteria");
           return 0;
       }

       // Perform clustering analysis
       var clusteringOptions = new ClusteringOptions
       {
           SimilarityThreshold = command.SimilarityThreshold,
           Algorithm = ParseClusteringAlgorithm(command.ClusterAlgorithm)
       };

       var clusterResult = await _comparisonService.AnalyzeTestClustersAsync(
           testIds, command.SolutionPath, clusteringOptions, cancellationToken);

       // Format and output results
       await FormatAndOutputClusterResults(clusterResult, command);
       return 0;
   }
   ```

3. **Create Cluster Output Formatters**
   ```csharp
   public class TextClusterFormatter
   {
       public string FormatClusterAnalysis(TestClusterAnalysis analysis)
       {
           var sb = new StringBuilder();
           sb.AppendLine("Test Cluster Analysis Results");
           sb.AppendLine("============================");
           sb.AppendLine();
           
           sb.AppendLine($"📊 Analysis Summary:");
           sb.AppendLine($"  • Total tests analyzed: {analysis.Statistics.TotalTests}");
           sb.AppendLine($"  • Clusters identified: {analysis.Statistics.NumberOfClusters}");
           sb.AppendLine($"  • Unclustered tests: {analysis.UnclusteredTests.Count}");
           sb.AppendLine($"  • Overall quality score: {analysis.Statistics.SilhouetteScore:F2}");
           sb.AppendLine();
           
           foreach (var cluster in analysis.Clusters)
           {
               FormatClusterSection(sb, cluster);
           }
           
           if (analysis.UnclusteredTests.Any())
           {
               FormatUnclusteredSection(sb, analysis.UnclusteredTests);
           }
           
           return sb.ToString();
       }
   }
   ```

#### Acceptance Criteria:
- [ ] CLI supports both two-test comparison and multi-test clustering
- [ ] Pattern-based test selection works correctly
- [ ] Scope-based analysis (class, namespace) functions properly
- [ ] Text and JSON output formats for cluster results
- [ ] Help documentation updated for new options
- [ ] End-to-end CLI tests pass for clustering scenarios

## Week 7-8: HTML Report Generation

### 7.1 HTML Report Infrastructure
**Estimated Time**: 3-4 days  
**Definition of Done**: Interactive HTML reports with visual similarity data

#### Tasks:
1. **Create HTML Template Infrastructure**
   ```csharp
   public class HtmlReportGenerator
   {
       private readonly string _templatePath;
       
       public async Task<string> GenerateComparisonReportAsync(TestComparisonResult result)
       {
           var template = await LoadTemplateAsync("comparison-report.html");
           var data = PrepareTemplateData(result);
           return await RenderTemplateAsync(template, data);
       }
       
       public async Task<string> GenerateClusterReportAsync(TestClusterAnalysis analysis)
       {
           var template = await LoadTemplateAsync("cluster-report.html");
           var data = PrepareClusterTemplateData(analysis);
           return await RenderTemplateAsync(template, data);
       }
   }
   ```

2. **Create HTML Templates**

   **comparison-report.html**:
   ```html
   <!DOCTYPE html>
   <html>
   <head>
       <title>Test Comparison Report</title>
       <script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
       <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.1.3/dist/css/bootstrap.min.css" rel="stylesheet">
       <style>
           .similarity-score { font-size: 2rem; font-weight: bold; }
           .high-similarity { color: #28a745; }
           .medium-similarity { color: #ffc107; }
           .low-similarity { color: #dc3545; }
           .method-overlap { margin: 10px 0; }
           .shared-method { background-color: #e3f2fd; padding: 5px; margin: 2px; border-radius: 3px; }
       </style>
   </head>
   <body>
       <div class="container">
           <h1>Test Comparison Report</h1>
           
           <div class="row">
               <div class="col-md-6">
                   <h3>Test 1: {{Test1Id}}</h3>
               </div>
               <div class="col-md-6">
                   <h3>Test 2: {{Test2Id}}</h3>
               </div>
           </div>
           
           <div class="text-center">
               <div class="similarity-score {{SimilarityClass}}">
                   {{OverallSimilarity}}% Similarity
               </div>
           </div>
           
           <div class="row mt-4">
               <div class="col-md-6">
                   <canvas id="overlapChart"></canvas>
               </div>
               <div class="col-md-6">
                   <canvas id="pathSimilarityChart"></canvas>
               </div>
           </div>
           
           <!-- Method overlap visualization -->
           <div class="mt-4">
               <h4>Shared Methods</h4>
               <div class="method-overlap">
                   {{#SharedMethods}}
                   <span class="shared-method" title="Confidence: {{Confidence}}">{{Method}}</span>
                   {{/SharedMethods}}
               </div>
           </div>
           
           <!-- Recommendations section -->
           <div class="mt-4">
               <h4>Optimization Recommendations</h4>
               {{#Recommendations}}
               <div class="alert alert-info">
                   <strong>{{Type}}</strong>: {{Description}}
                   <br><small>Confidence: {{ConfidenceScore}}% | Effort: {{EstimatedEffortLevel}}/5</small>
               </div>
               {{/Recommendations}}
           </div>
       </div>
       
       <script>
           // Chart.js visualization code for overlap charts
           const overlapData = {{{OverlapChartData}}};
           const pathData = {{{PathChartData}}};
           
           new Chart(document.getElementById('overlapChart'), overlapData);
           new Chart(document.getElementById('pathSimilarityChart'), pathData);
       </script>
   </body>
   </html>
   ```

3. **Create Visualization Data Preparation**
   ```csharp
   public class ReportDataPreparer
   {
       public object PrepareComparisonData(TestComparisonResult result)
       {
           return new
           {
               Test1Id = result.Test1Id,
               Test2Id = result.Test2Id,
               OverallSimilarity = $"{result.OverallSimilarity:P1}",
               SimilarityClass = GetSimilarityClass(result.OverallSimilarity),
               SharedMethods = result.CoverageOverlap.SharedMethods.Select(m => new
               {
                   Method = m.Method,
                   Confidence = $"{m.Confidence:P1}"
               }),
               Recommendations = result.Recommendations,
               OverlapChartData = JsonSerializer.Serialize(CreateOverlapChartData(result.CoverageOverlap)),
               PathChartData = JsonSerializer.Serialize(CreatePathChartData(result.ExecutionPathSimilarity))
           };
       }
       
       private object CreateOverlapChartData(CoverageOverlapAnalysis overlap)
       {
           return new
           {
               type = "doughnut",
               data = new
               {
                   labels = new[] { "Shared Methods", "Unique to Test 1", "Unique to Test 2" },
                   datasets = new[]
                   {
                       new
                       {
                           data = new[] { overlap.SharedProductionMethods, overlap.UniqueToTest1, overlap.UniqueToTest2 },
                           backgroundColor = new[] { "#36A2EB", "#FF6384", "#FFCE56" }
                       }
                   }
               },
               options = new
               {
                   responsive = true,
                   plugins = new
                   {
                       title = new { display = true, text = "Method Coverage Overlap" }
                   }
               }
           };
       }
   }
   ```

#### Acceptance Criteria:
- [ ] HTML templates render correctly in all major browsers
- [ ] Interactive charts display similarity data accurately  
- [ ] Visual elements enhance report readability
- [ ] Template system supports both comparison and cluster reports
- [ ] Charts are responsive and accessible

### 7.2 Interactive Features and Export
**Estimated Time**: 2-3 days  
**Definition of Done**: Rich interactive features and export capabilities

#### Tasks:
1. **Add Interactive Features**
   ```javascript
   // Add to HTML template
   function toggleMethodDetails(methodName) {
       const detailsPanel = document.getElementById(`details-${methodName}`);
       detailsPanel.style.display = detailsPanel.style.display === 'none' ? 'block' : 'none';
   }
   
   function filterMethods(confidenceThreshold) {
       const methods = document.querySelectorAll('.shared-method');
       methods.forEach(method => {
           const confidence = parseFloat(method.getAttribute('data-confidence'));
           method.style.display = confidence >= confidenceThreshold ? 'inline-block' : 'none';
       });
   }
   
   // Export functionality
   function exportToPDF() {
       window.print();
   }
   
   function exportToJSON() {
       const data = {{{JsonData}}};
       const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
       const url = URL.createObjectURL(blob);
       const a = document.createElement('a');
       a.href = url;
       a.download = 'test-comparison-results.json';
       document.body.appendChild(a);
       a.click();
       document.body.removeChild(a);
       URL.revokeObjectURL(url);
   }
   ```

2. **Create Cluster Visualization**
   ```html
   <!-- cluster-report.html excerpt -->
   <div class="cluster-visualization">
       <canvas id="clusterNetwork"></canvas>
   </div>
   
   <script>
       // D3.js or similar for cluster network visualization
       function renderClusterNetwork(clusterData) {
           // Create force-directed graph showing test relationships
           // Node size based on number of methods
           // Edge thickness based on similarity scores
           // Color coding by cluster membership
       }
   </script>
   ```

#### Acceptance Criteria:
- [ ] Interactive features work smoothly across browsers
- [ ] Export to PDF preserves all visual elements
- [ ] Export to JSON maintains data integrity  
- [ ] Cluster network visualization accurately represents relationships
- [ ] Performance is acceptable for large cluster analyses

### 7.3 HTML Report Integration with CLI
**Estimated Time**: 1-2 days  
**Definition of Done**: CLI generates HTML reports seamlessly

#### Tasks:
1. **Update CLI Command Handler**
   ```csharp
   private async Task FormatAndOutputResults(TestComparisonResult result, CompareTestsCommand command)
   {
       string output;
       
       switch (command.Format.ToLowerInvariant())
       {
           case "html":
               output = await _htmlReportGenerator.GenerateComparisonReportAsync(result);
               break;
           case "json":
               output = _jsonFormatter.FormatComparison(result);
               break;
           default:
               output = _textFormatter.FormatComparison(result);
               break;
       }
       
       if (!string.IsNullOrEmpty(command.OutputPath))
       {
           await File.WriteAllTextAsync(command.OutputPath, output);
           _logger.LogInformation($"Results written to {command.OutputPath}");
       }
       else
       {
           Console.WriteLine(output);
       }
   }
   ```

2. **Add HTML Format to Help Text**
   ```csharp
   [Option("--format", Default = "text",
           HelpText = "Output format (text, json, html)")]
   public string Format { get; set; } = "text";
   ```

#### Acceptance Criteria:
- [ ] HTML format option works correctly in CLI
- [ ] Generated HTML files open properly in browsers
- [ ] File output path handling works for HTML reports
- [ ] Help documentation reflects HTML option
- [ ] Error handling covers HTML generation failures

## Phase 2 Validation & Documentation

### Performance Benchmarking
**Tasks**:
- Test clustering performance with various test set sizes (10, 50, 100+ tests)
- Validate execution path analysis performance requirements
- Ensure HTML report generation completes in reasonable time

**Acceptance Criteria**:
- [ ] 50-test clustering analysis completes in <30 seconds
- [ ] Execution path analysis adds <1 second to comparison time  
- [ ] HTML report generation completes in <5 seconds
- [ ] Memory usage stays within acceptable bounds for large analyses

### Integration Testing
**Tasks**:
- End-to-end testing with real TestIntelligence test methods
- Validate integration with existing Phase 1 functionality
- Test error scenarios and edge cases

**Acceptance Criteria**:
- [ ] All existing functionality continues to work correctly
- [ ] New features integrate seamlessly with Phase 1 components
- [ ] Error handling gracefully manages execution trace failures
- [ ] Edge cases (empty clusters, single test sets) handled properly

### Documentation Updates
**Tasks**:
- Update CLAUDE.md with new CLI commands and examples
- Create comprehensive usage examples for clustering analysis
- Document HTML report features and interpretation

**Acceptance Criteria**:
- [ ] Documentation reflects all new functionality
- [ ] Examples work as documented
- [ ] Help text is comprehensive and accurate

## Success Metrics for Phase 2

### Functional Requirements ✅
- [ ] Graph-based execution path similarity analysis working accurately
- [ ] Multi-test hierarchical clustering with quality metrics
- [ ] Interactive HTML reports with visual similarity data
- [ ] Pattern-based test selection and scope analysis
- [ ] Integration with existing comparison infrastructure

### Performance Requirements ⚡
- [ ] Execution path analysis completes in <2 seconds for typical traces
- [ ] 50-test clustering analysis completes in <30 seconds  
- [ ] HTML report generation completes in <5 seconds
- [ ] Memory usage optimized for large test sets

### Quality Requirements 🛡️
- [ ] All unit tests pass (>95% success rate)
- [ ] Integration tests validate real-world scenarios
- [ ] Code coverage >85% for all new components
- [ ] HTML reports work across major browsers
- [ ] Performance benchmarks consistently met

This implementation plan builds upon Phase 1's foundation while adding sophisticated analysis capabilities that position the Test Comparison feature as a comprehensive test optimization tool.