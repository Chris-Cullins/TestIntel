using System.CommandLine;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Build.Locator;
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
                services.AddTransient<ITestSelectionEngine, TestSelectionEngine>();
                services.AddTransient<IAnalysisService, AnalysisService>();
                services.AddTransient<ICategorizationService, CategorizationService>();
                services.AddTransient<ISelectionService, SelectionService>();
                services.AddTransient<IDiffAnalysisService, DiffAnalysisService>();
                services.AddTransient<IOutputFormatter, JsonOutputFormatter>();
                services.AddTransient<IConfigurationService, ConfigurationService>();
                
                // Impact Analyzer services
                services.AddTransient<IRoslynAnalyzer, RoslynAnalyzer>();
                services.AddTransient<IGitDiffParser, GitDiffParser>();
                services.AddTransient<ISimplifiedDiffImpactAnalyzer, SimplifiedDiffImpactAnalyzer>();
                services.AddTransient<ITestDiscovery, NUnitTestDiscovery>();
                services.AddTransient<ICallGraphService, CallGraphService>();
                services.AddTransient<ITestCoverageAnalyzer, TestCoverageAnalyzer>();
                services.AddTransient<ITestExecutionTracer, TestExecutionTracer>();
                services.AddTransient<ICodeChangeCoverageAnalyzer, CodeChangeCoverageAnalyzer>();
                services.AddTransient<ICoverageAnalysisService, CoverageAnalysisService>();
                
                // Assembly and Cache services
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
                services.AddTransient<ICommandFactory, CommandFactory>();
            });
    }
}