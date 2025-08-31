using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Build.Locator;
using TestIntelligence.CLI.Services;
using TestIntelligence.SelectionEngine.Engine;
using TestIntelligence.SelectionEngine.Interfaces;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Services;
using TestIntelligence.Core.Discovery;
using TestIntelligence.Core.Services;

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
        
        var rootCommand = new RootCommand("TestIntelligence - Intelligent test analysis and selection tool")
        {
            CreateAnalyzeCommand(host),
            CreateCategorizeCommand(host),
            CreateSelectCommand(host),
            CreateDiffCommand(host),
            CreateCallGraphCommand(host),
            CreateFindTestsCommand(host),
            CreateVersionCommand()
        };

        return await rootCommand.InvokeAsync(args);
    }

    private static IHostBuilder CreateHostBuilder(string[] args)
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
                
                // Impact Analyzer services
                services.AddTransient<IRoslynAnalyzer, RoslynAnalyzerV2>();
                services.AddTransient<IGitDiffParser, GitDiffParser>();
                services.AddTransient<ISimplifiedDiffImpactAnalyzer, SimplifiedDiffImpactAnalyzer>();
                services.AddTransient<ITestDiscovery, NUnitTestDiscovery>();
                services.AddTransient<ICallGraphService, CallGraphService>();
                services.AddTransient<ITestCoverageAnalyzer, TestCoverageAnalyzer>();
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

    private static Command CreateFindTestsCommand(IHost host)
    {
        var methodOption = new Option<string>(
            name: "--method",
            description: "Method identifier to find tests for (e.g., 'MyNamespace.MyClass.MyMethod')")
        {
            IsRequired = true
        };
        methodOption.AddAlias("-m");

        var solutionOption = new Option<string>(
            name: "--solution",
            description: "Path to solution file or directory")
        {
            IsRequired = true
        };
        solutionOption.AddAlias("-s");

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
            description: "Enable verbose output with call paths");
        verboseOption.AddAlias("-v");

        var findTestsCommand = new Command("find-tests", "Find all tests that exercise a given method")
        {
            methodOption,
            solutionOption,
            outputOption,
            formatOption,
            verboseOption
        };

        findTestsCommand.SetHandler(async (string method, string solution, string? output, string format, bool verbose) =>
        {
            var testCoverageAnalyzer = host.Services.GetRequiredService<ITestCoverageAnalyzer>();
            var outputFormatter = host.Services.GetRequiredService<IOutputFormatter>();
            
            try
            {
                Console.WriteLine($"Finding tests that exercise method: {method}");
                Console.WriteLine($"Solution path: {solution}");
                Console.WriteLine();

                var tests = await testCoverageAnalyzer.FindTestsExercisingMethodAsync(method, solution);
                
                if (!tests.Any())
                {
                    Console.WriteLine("No tests found that exercise this method.");
                    return;
                }

                Console.WriteLine($"Found {tests.Count} test(s) exercising this method:");
                Console.WriteLine();

                if (format == "json")
                {
                    var json = outputFormatter.FormatAsJson(tests);
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        await File.WriteAllTextAsync(output, json);
                        Console.WriteLine($"Results written to: {output}");
                    }
                    else
                    {
                        Console.WriteLine(json);
                    }
                }
                else
                {
                    var result = new System.Text.StringBuilder();
                    
                    foreach (var test in tests.OrderByDescending(t => t.Confidence))
                    {
                        result.AppendLine($"• {test.TestClassName}.{test.TestMethodName}");
                        result.AppendLine($"  Assembly: {test.TestAssembly}");
                        result.AppendLine($"  Type: {test.TestType}");
                        result.AppendLine($"  Confidence: {test.Confidence:F2}");
                        result.AppendLine($"  Call Depth: {test.CallDepth}");
                        
                        if (verbose)
                        {
                            result.AppendLine($"  Call Path: {test.GetCallPathDisplay()}");
                        }
                        
                        result.AppendLine();
                    }

                    var textOutput = result.ToString();
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        await File.WriteAllTextAsync(output, textOutput);
                        Console.WriteLine($"Results written to: {output}");
                    }
                    else
                    {
                        Console.WriteLine(textOutput);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error finding tests: {ex.Message}");
                if (verbose)
                {
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
        }, methodOption, solutionOption, outputOption, formatOption, verboseOption);

        return findTestsCommand;
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