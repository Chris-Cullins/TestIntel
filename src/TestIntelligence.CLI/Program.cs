using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Services;
using TestIntelligence.SelectionEngine.Engine;
using TestIntelligence.SelectionEngine.Interfaces;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Services;
using TestIntelligence.Core.Discovery;

namespace TestIntelligence.CLI;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();
        
        var rootCommand = new RootCommand("TestIntelligence - Intelligent test analysis and selection tool")
        {
            CreateAnalyzeCommand(host),
            CreateCategorizeCommand(host),
            CreateSelectCommand(host),
            CreateDiffCommand(host),
            CreateCallGraphCommand(host),
            CreateVersionCommand()
        };

        return await rootCommand.InvokeAsync(args);
    }

    private static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddLogging(builder => builder.AddConsole());
                services.AddTransient<ITestSelectionEngine, TestSelectionEngine>();
                services.AddTransient<IAnalysisService, AnalysisService>();
                services.AddTransient<ICategorizationService, CategorizationService>();
                services.AddTransient<ISelectionService, SelectionService>();
                services.AddTransient<IDiffAnalysisService, DiffAnalysisService>();
                services.AddTransient<IOutputFormatter, JsonOutputFormatter>();
                
                // Impact Analyzer services
                services.AddTransient<IRoslynAnalyzer, RoslynAnalyzer>();
                services.AddTransient<IGitDiffParser, GitDiffParser>();
                services.AddTransient<ISimplifiedDiffImpactAnalyzer, SimplifiedDiffImpactAnalyzer>();
                services.AddTransient<ITestDiscovery, NUnitTestDiscovery>();
                services.AddTransient<ICallGraphService, CallGraphService>();
            });
    }

    private static Command CreateAnalyzeCommand(IHost host)
    {
        var pathOption = new Option<string>(
            name: "--path",
            description: "Path to solution file or assembly")
        {
            IsRequired = true
        };
        pathOption.AddAlias("-p");

        var outputOption = new Option<string>(
            name: "--output",
            description: "Output file path (default: console)")
        {
            IsRequired = false
        };
        outputOption.AddAlias("-o");

        var formatOption = new Option<string>(
            name: "--format",
            description: "Output format: json, text",
            getDefaultValue: () => "text");
        formatOption.AddAlias("-f");

        var verboseOption = new Option<bool>(
            name: "--verbose",
            description: "Enable verbose output");
        verboseOption.AddAlias("-v");

        var analyzeCommand = new Command("analyze", "Analyze test assemblies for categorization and impact")
        {
            pathOption,
            outputOption,
            formatOption,
            verboseOption
        };

        analyzeCommand.SetHandler(async (string path, string? output, string format, bool verbose) =>
        {
            var analysisService = host.Services.GetRequiredService<IAnalysisService>();
            await analysisService.AnalyzeAsync(path, output, format, verbose);
        }, pathOption, outputOption, formatOption, verboseOption);

        return analyzeCommand;
    }

    private static Command CreateCategorizeCommand(IHost host)
    {
        var pathOption = new Option<string>(
            name: "--path",
            description: "Path to solution file or assembly")
        {
            IsRequired = true
        };
        pathOption.AddAlias("-p");

        var outputOption = new Option<string>(
            name: "--output",
            description: "Output file path (default: console)")
        {
            IsRequired = false
        };
        outputOption.AddAlias("-o");

        var categorizeCommand = new Command("categorize", "Categorize tests by type (Unit, Integration, Database, etc.)")
        {
            pathOption,
            outputOption
        };

        categorizeCommand.SetHandler(async (string path, string? output) =>
        {
            var categorizationService = host.Services.GetRequiredService<ICategorizationService>();
            await categorizationService.CategorizeAsync(path, output);
        }, pathOption, outputOption);

        return categorizeCommand;
    }

    private static Command CreateSelectCommand(IHost host)
    {
        var pathOption = new Option<string>(
            name: "--path",
            description: "Path to solution file or assembly")
        {
            IsRequired = true
        };
        pathOption.AddAlias("-p");

        var changesOption = new Option<string[]>(
            name: "--changes",
            description: "Changed files (can be specified multiple times)")
        {
            AllowMultipleArgumentsPerToken = true
        };
        changesOption.AddAlias("-c");

        var confidenceOption = new Option<string>(
            name: "--confidence",
            description: "Confidence level: Fast, Medium, High, Full",
            getDefaultValue: () => "Medium");

        var outputOption = new Option<string>(
            name: "--output",
            description: "Output file path (default: console)")
        {
            IsRequired = false
        };
        outputOption.AddAlias("-o");

        var maxTestsOption = new Option<int?>(
            name: "--max-tests",
            description: "Maximum number of tests to select");

        var maxTimeOption = new Option<string?>(
            name: "--max-time",
            description: "Maximum execution time (e.g., '5m', '30s')");

        var selectCommand = new Command("select", "Select optimal tests based on code changes and confidence level")
        {
            pathOption,
            changesOption,
            confidenceOption,
            outputOption,
            maxTestsOption,
            maxTimeOption
        };

        selectCommand.SetHandler(async (string path, string[] changes, string confidence, string? output, int? maxTests, string? maxTime) =>
        {
            var selectionService = host.Services.GetRequiredService<ISelectionService>();
            await selectionService.SelectAsync(path, changes, confidence, output, maxTests, maxTime);
        }, pathOption, changesOption, confidenceOption, outputOption, maxTestsOption, maxTimeOption);

        return selectCommand;
    }

    private static Command CreateDiffCommand(IHost host)
    {
        var solutionOption = new Option<string>(
            name: "--solution",
            description: "Path to solution file")
        {
            IsRequired = true
        };
        solutionOption.AddAlias("-s");

        var diffContentOption = new Option<string>(
            name: "--diff-content",
            description: "Git diff content as string");
        diffContentOption.AddAlias("-d");

        var diffFileOption = new Option<string>(
            name: "--diff-file",
            description: "Path to git diff file");
        diffFileOption.AddAlias("-f");

        var gitCommandOption = new Option<string>(
            name: "--git-command",
            description: "Git command to generate diff (e.g., 'diff HEAD~1')")
        {
            ArgumentHelpName = "COMMAND"
        };
        gitCommandOption.AddAlias("-g");

        var outputOption = new Option<string>(
            name: "--output",
            description: "Output file path (default: console)")
        {
            IsRequired = false
        };
        outputOption.AddAlias("-o");

        var formatOption = new Option<string>(
            name: "--format",
            description: "Output format: json, text",
            getDefaultValue: () => "text");
        formatOption.AddAlias("--fmt");

        var verboseOption = new Option<bool>(
            name: "--verbose",
            description: "Enable verbose output");
        verboseOption.AddAlias("-v");

        var diffCommand = new Command("diff", "Analyze test impact from git diff or patch files")
        {
            solutionOption,
            diffContentOption,
            diffFileOption,
            gitCommandOption,
            outputOption,
            formatOption,
            verboseOption
        };

        diffCommand.SetHandler(async (string solution, string? diffContent, string? diffFile, string? gitCommand, string? output, string format, bool verbose) =>
        {
            var diffAnalysisService = host.Services.GetRequiredService<IDiffAnalysisService>();
            await diffAnalysisService.AnalyzeDiffAsync(solution, diffContent, diffFile, gitCommand, output, format, verbose);
        }, solutionOption, diffContentOption, diffFileOption, gitCommandOption, outputOption, formatOption, verboseOption);

        return diffCommand;
    }

    private static Command CreateCallGraphCommand(IHost host)
    {
        var pathOption = new Option<string>(
            name: "--path",
            description: "Path to solution file, project directory, or source files")
        {
            IsRequired = true
        };
        pathOption.AddAlias("-p");

        var outputOption = new Option<string>(
            name: "--output",
            description: "Output file path (default: console)")
        {
            IsRequired = false
        };
        outputOption.AddAlias("-o");

        var formatOption = new Option<string>(
            name: "--format",
            description: "Output format: json, text",
            getDefaultValue: () => "text");
        formatOption.AddAlias("-f");

        var verboseOption = new Option<bool>(
            name: "--verbose",
            description: "Enable verbose output with detailed method call information");
        verboseOption.AddAlias("-v");

        var maxMethodsOption = new Option<int?>(
            name: "--max-methods",
            description: "Maximum number of methods to include in detailed output (default: 50)");
        maxMethodsOption.AddAlias("-m");

        var callGraphCommand = new Command("callgraph", "Analyze method call graph and generate dependency reports")
        {
            pathOption,
            outputOption,
            formatOption,
            verboseOption,
            maxMethodsOption
        };

        callGraphCommand.SetHandler(async (string path, string? output, string format, bool verbose, int? maxMethods) =>
        {
            var callGraphService = host.Services.GetRequiredService<ICallGraphService>();
            await callGraphService.AnalyzeCallGraphAsync(path, output, format, verbose, maxMethods);
        }, pathOption, outputOption, formatOption, verboseOption, maxMethodsOption);

        return callGraphCommand;
    }

    private static Command CreateVersionCommand()
    {
        var versionCommand = new Command("version", "Show version information");
        
        versionCommand.SetHandler(() =>
        {
            var version = typeof(Program).Assembly.GetName().Version;
            Console.WriteLine($"TestIntelligence CLI v{version}");
            Console.WriteLine("Intelligent test analysis and selection tool");
        });

        return versionCommand;
    }
}