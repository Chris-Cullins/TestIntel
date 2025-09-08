using System.CommandLine;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Build.Locator;
using TestIntelligence.Categorizer;
using TestIntelligence.CLI.Services;
using TestIntelligence.CLI.Models;
using TestIntelligence.SelectionEngine.Engine;
using TestIntelligence.SelectionEngine.Interfaces;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Services;
using TestIntelligence.Core.Discovery;
using TestIntelligence.Core.Interfaces;
using TestIntelligence.Core.Services;
using TestIntelligence.Core.Assembly;
using TestIntelligence.CLI.Commands;
using TestIntelligence.CLI.Progress;
using TestIntelligence.TestComparison.Services;
using TestIntelligence.TestComparison.Algorithms;
using TestIntelligence.TestComparison.Formatters;

namespace TestIntelligence.CLI;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Initialize MSBuildLocator to register the correct MSBuild version
        // This must be done before any MSBuildWorkspace operations
        try
        {
            if (!MSBuildLocator.IsRegistered)
            {
                // Find the best MSBuild instance (should pick up .NET 8 SDK)
                var msbuildInstance = MSBuildLocator.QueryVisualStudioInstances()
                    .OrderByDescending(instance => instance.Version)
                    .FirstOrDefault() 
                    ?? MSBuildLocator.QueryVisualStudioInstances().FirstOrDefault();

                if (msbuildInstance != null)
                {
                    Console.WriteLine($"Registering MSBuild from: {msbuildInstance.MSBuildPath} (v{msbuildInstance.Version})");
                    MSBuildLocator.RegisterInstance(msbuildInstance);
                }
                else
                {
                    Console.WriteLine("Warning: Could not locate MSBuild instance, MSBuild workspace may not work properly");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to initialize MSBuildLocator: {ex.Message}");
            Console.WriteLine("MSBuild workspace functionality may be limited, but file-based analysis will still work");
        }

        var host = CreateHostBuilder(args).Build();
        
        // Use CommandFactory to create all commands
        var commandFactory = host.Services.GetRequiredService<ICommandFactory>();
        var rootCommand = commandFactory.CreateRootCommand(host);

        return await rootCommand.InvokeAsync(args);
    }

    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddLogging(builder => 
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Debug);
                });
                // Test categorization - Singleton for shared configuration
                services.AddTestCategorizer();
                
                // CLI Command services - Transient for command isolation
                services.AddTransient<IAnalysisService, RefactoredAnalysisService>();
                services.AddTransient<ICategorizationService, CategorizationService>();
                services.AddTransient<ISelectionService, SelectionService>();
                services.AddTransient<IDiffAnalysisService, DiffAnalysisService>();
                services.AddTransient<IOutputFormatter, JsonOutputFormatter>();
                
                // Analysis service components - Transient for focused operations
                services.AddTransient<IAnalysisCoordinatorService, AnalysisCoordinatorService>();
                services.AddTransient<IAssemblyDiscoveryService, AssemblyDiscoveryService>();
                services.AddTransient<ITestAnalysisService, TestAnalysisService>();
                services.AddTransient<IProjectAnalysisService, ProjectAnalysisService>();
                
                // Configuration - Singleton for shared app config
                services.AddSingleton<IConfigurationService, ConfigurationService>();
                
                // Analyzers and engines - Scoped for caching during operations
                services.AddScoped<IRoslynAnalyzer, RoslynAnalyzer>();
                services.AddScoped<ITestSelectionEngine, TestSelectionEngine>();
                services.AddScoped<ITestCoverageAnalyzer, TestCoverageAnalyzer>();
                services.AddScoped<ITestExecutionTracer, TestExecutionTracer>();
                services.AddScoped<ICallGraphService, CallGraphService>();
                services.AddScoped<ICoverageAnalysisService, CoverageAnalysisService>();
                services.AddScoped<ICodeChangeCoverageAnalyzer, CodeChangeCoverageAnalyzer>();
                
                // Register focused interfaces using the same scoped implementation
                services.AddScoped<ITestCoverageQuery>(provider => provider.GetRequiredService<ITestCoverageAnalyzer>());
                services.AddScoped<ITestCoverageMapBuilder>(provider => provider.GetRequiredService<ITestCoverageAnalyzer>());
                services.AddScoped<ITestCoverageStatistics>(provider => provider.GetRequiredService<ITestCoverageAnalyzer>());
                services.AddScoped<ITestCoverageCacheManager>(provider => provider.GetRequiredService<ITestCoverageAnalyzer>());
                
                // Utilities - Transient for lightweight operations
                services.AddTransient<IGitDiffParser, GitDiffParser>();
                services.AddTransient<ISimplifiedDiffImpactAnalyzer, SimplifiedDiffImpactAnalyzer>();
                services.AddTransient<ITestDiscovery, NUnitTestDiscovery>();
                
                // Assembly and Path services - Singleton for pure utilities
                services.AddSingleton<IAssemblyPathResolver, AssemblyPathResolver>();
                services.AddTransient<CrossFrameworkAssemblyLoader>();
                services.AddTransient<CacheManagementService>();
                
                // Command handlers
                services.AddTransient<AnalyzeCommandHandler>();
                services.AddTransient<CategorizeCommandHandler>();
                services.AddTransient<SelectCommandHandler>();
                services.AddTransient<DiffCommandHandler>();
                services.AddTransient<CallGraphCommandHandler>();
                services.AddTransient<FindTestsCommandHandler>();
                services.AddTransient<TraceExecutionCommandHandler>();
                services.AddTransient<AnalyzeCoverageCommandHandler>();
                services.AddTransient<ConfigCommandHandler>();
                services.AddTransient<CacheCommandHandler>();
                services.AddTransient<VersionCommandHandler>();
                services.AddTransient<CompareTestsCommandHandler>();
                services.AddTransient<ICommandFactory, CommandFactory>();
                
                // Test Comparison services
                services.AddScoped<ITestComparisonService, TestComparisonService>();
                services.AddScoped<TestCoverageComparisonService>();
                services.AddScoped<ISimilarityCalculator, SimilarityCalculator>();
                services.AddScoped<OptimizationRecommendationEngine>();
                
                // Comparison formatters
                services.AddTransient<IComparisonFormatter, TextComparisonFormatter>();
                services.AddTransient<IComparisonFormatter, JsonComparisonFormatter>();
                
                // Progress reporting
                services.AddTransient<IProgressReporter, ConsoleProgressBar>();
            });
    }
}