using System;
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TestIntelligence.CLI.Commands
{
    /// <summary>
    /// Factory for creating CLI commands with proper dependency injection.
    /// This class extracts command creation logic from Program.cs.
    /// </summary>
    public class CommandFactory : ICommandFactory
    {
        public RootCommand CreateRootCommand(IHost host)
        {
            var rootCommand = new RootCommand("TestIntelligence - Intelligent test analysis and selection tool");

            // Create individual commands and add them to the root command
            rootCommand.Add(CreateAnalyzeCommand(host));
            rootCommand.Add(CreateCategorizeCommand(host));
            rootCommand.Add(CreateSelectCommand(host));
            rootCommand.Add(CreateDiffCommand(host));
            rootCommand.Add(CreateCallGraphCommand(host));
            rootCommand.Add(CreateFindTestsCommand(host));
            rootCommand.Add(CreateTraceExecutionCommand(host));
            rootCommand.Add(CreateAnalyzeCoverageCommand(host));
            rootCommand.Add(CreateConfigCommand(host));
            rootCommand.Add(CreateCacheCommand(host));
            rootCommand.Add(CreateVersionCommand(host));
            rootCommand.Add(CreateCompareTestsCommand(host));

            return rootCommand;
        }

        private Command CreateAnalyzeCommand(IHost host)
        {
            var pathOption = new Option<string>("--path", "Path to solution file or directory") { IsRequired = true };
            pathOption.AddAlias("-p");
            
            var outputOption = new Option<string>("--output", "Output file path (optional)");
            outputOption.AddAlias("-o");
            
            var formatOption = new Option<string>("--format", () => "text", "Output format: json, text");
            formatOption.AddAlias("-f");
            
            var verboseOption = new Option<bool>("--verbose", "Enable verbose output");
            verboseOption.AddAlias("-v");

            var command = new Command("analyze", "Analyze test assemblies for categorization and impact")
            {
                pathOption, outputOption, formatOption, verboseOption
            };

            command.SetHandler(async (string path, string output, string format, bool verbose) =>
            {
                var handler = host.Services.GetRequiredService<AnalyzeCommandHandler>();
                var context = new CommandContext(host.Services);
                context.SetParameter("path", path);
                context.SetParameter("output", output ?? string.Empty);
                context.SetParameter("format", format);
                context.SetParameter("verbose", verbose);
                
                var exitCode = await handler.ExecuteAsync(context);
                Environment.ExitCode = exitCode;
            }, pathOption, outputOption, formatOption, verboseOption);

            return command;
        }

        private Command CreateCategorizeCommand(IHost host)
        {
            var pathOption = new Option<string>("--path", "Path to solution file or directory") { IsRequired = true };
            pathOption.AddAlias("-p");
            
            var outputOption = new Option<string>("--output", "Output file path (optional)");
            outputOption.AddAlias("-o");

            var command = new Command("categorize", "Categorize tests by type (Unit, Integration, Database, etc.)")
            {
                pathOption, outputOption
            };

            command.SetHandler(async (string path, string output) =>
            {
                var handler = host.Services.GetRequiredService<CategorizeCommandHandler>();
                var context = new CommandContext(host.Services);
                context.SetParameter("path", path);
                context.SetParameter("output", output ?? string.Empty);
                
                var exitCode = await handler.ExecuteAsync(context);
                Environment.ExitCode = exitCode;
            }, pathOption, outputOption);

            return command;
        }

        private Command CreateSelectCommand(IHost host)
        {
            var pathOption = new Option<string>("--path", "Path to solution file or directory") { IsRequired = true };
            pathOption.AddAlias("-p");
            
            var changesOption = new Option<string[]>("--changes", "Changed file paths") { AllowMultipleArgumentsPerToken = true };
            changesOption.AddAlias("-c");
            
            var confidenceOption = new Option<string>("--confidence", () => "Medium", "Confidence level: Fast, Medium, High, Full");
            
            var outputOption = new Option<string>("--output", "Output file path (optional)");
            outputOption.AddAlias("-o");
            
            var maxTestsOption = new Option<int?>("--max-tests", "Maximum number of tests to select");
            var maxTimeOption = new Option<string>("--max-time", "Maximum execution time limit");

            var command = new Command("select", "Select optimal tests based on code changes and confidence level")
            {
                pathOption, changesOption, confidenceOption, outputOption, maxTestsOption, maxTimeOption
            };

            command.SetHandler(async (string path, string[] changes, string confidence, string output, int? maxTests, string maxTime) =>
            {
                var handler = host.Services.GetRequiredService<SelectCommandHandler>();
                var context = new CommandContext(host.Services);
                context.SetParameter("path", path);
                context.SetParameter("changes", changes);
                context.SetParameter("confidence", confidence);
                context.SetParameter("output", output);
                context.SetParameter("maxTests", maxTests.HasValue ? (object)maxTests.Value : (object)DBNull.Value);
                context.SetParameter("maxTime", maxTime ?? string.Empty);
                
                var exitCode = await handler.ExecuteAsync(context);
                Environment.ExitCode = exitCode;
            }, pathOption, changesOption, confidenceOption, outputOption, maxTestsOption, maxTimeOption);

            return command;
        }

        private Command CreateDiffCommand(IHost host)
        {
            var solutionOption = new Option<string>("--solution", "Path to solution file") { IsRequired = true };
            solutionOption.AddAlias("-s");
            
            var diffContentOption = new Option<string>("--diff-content", "Git diff content as string");
            diffContentOption.AddAlias("-d");
            
            var diffFileOption = new Option<string>("--diff-file", "Path to git diff file");
            diffFileOption.AddAlias("-f");
            
            var gitCommandOption = new Option<string>("--git-command", "Git command to generate diff (e.g., 'diff HEAD~1')");
            gitCommandOption.AddAlias("-g");
            
            var outputOption = new Option<string>("--output", "Output file path (default: console)");
            outputOption.AddAlias("-o");
            
            var formatOption = new Option<string>("--format", () => "text", "Output format: json, text");
            formatOption.AddAlias("--fmt");
            
            var verboseOption = new Option<bool>("--verbose", "Enable verbose output");
            verboseOption.AddAlias("-v");

            var command = new Command("diff", "Analyze test impact from git diff or patch files")
            {
                solutionOption, diffContentOption, diffFileOption, gitCommandOption, outputOption, formatOption, verboseOption
            };

            command.SetHandler(async (string solution, string diffContent, string diffFile, string gitCommand, string output, string format, bool verbose) =>
            {
                var handler = host.Services.GetRequiredService<DiffCommandHandler>();
                var context = new CommandContext(host.Services);
                context.SetParameter("solution", solution);
                context.SetParameter("diff-content", diffContent);
                context.SetParameter("diff-file", diffFile);
                context.SetParameter("git-command", gitCommand);
                context.SetParameter("output", output);
                context.SetParameter("format", format);
                context.SetParameter("verbose", verbose);
                
                var exitCode = await handler.ExecuteAsync(context);
                Environment.ExitCode = exitCode;
            }, solutionOption, diffContentOption, diffFileOption, gitCommandOption, outputOption, formatOption, verboseOption);

            return command;
        }

        private Command CreateCallGraphCommand(IHost host)
        {
            var pathOption = new Option<string>("--path", "Path to solution file, project directory, or source files") { IsRequired = true };
            pathOption.AddAlias("-p");
            
            var outputOption = new Option<string>("--output", "Output file path (default: console)");
            outputOption.AddAlias("-o");
            
            var formatOption = new Option<string>("--format", () => "text", "Output format: json, text");
            formatOption.AddAlias("-f");
            
            var verboseOption = new Option<bool>("--verbose", "Enable verbose output with detailed method call information");
            verboseOption.AddAlias("-v");
            
            var maxMethodsOption = new Option<int?>("--max-methods", "Maximum number of methods to include in detailed output (default: 50)");
            maxMethodsOption.AddAlias("-m");

            var command = new Command("callgraph", "Analyze method call graph and generate dependency reports")
            {
                pathOption, outputOption, formatOption, verboseOption, maxMethodsOption
            };

            command.SetHandler(async (string path, string output, string format, bool verbose, int? maxMethods) =>
            {
                var handler = host.Services.GetRequiredService<CallGraphCommandHandler>();
                var context = new CommandContext(host.Services);
                context.SetParameter("path", path);
                context.SetParameter("output", output);
                context.SetParameter("format", format);
                context.SetParameter("verbose", verbose);
                context.SetParameter("max-methods", maxMethods.HasValue ? (object)maxMethods.Value : 50);
                
                var exitCode = await handler.ExecuteAsync(context);
                Environment.ExitCode = exitCode;
            }, pathOption, outputOption, formatOption, verboseOption, maxMethodsOption);

            return command;
        }

        private Command CreateFindTestsCommand(IHost host)
        {
            var methodOption = new Option<string>("--method", "Method identifier to find tests for (e.g., 'MyNamespace.MyClass.MyMethod')") { IsRequired = true };
            methodOption.AddAlias("-m");
            
            var solutionOption = new Option<string>("--solution", "Path to solution file or directory") { IsRequired = true };
            solutionOption.AddAlias("-s");
            
            var outputOption = new Option<string>("--output", "Output file path (default: console)");
            outputOption.AddAlias("-o");
            
            var formatOption = new Option<string>("--format", () => "text", "Output format: json, text");
            formatOption.AddAlias("-f");
            
            var verboseOption = new Option<bool>("--verbose", "Enable verbose output with call paths");
            verboseOption.AddAlias("-v");

            var command = new Command("find-tests", "Find all tests that exercise a given method")
            {
                methodOption, solutionOption, outputOption, formatOption, verboseOption
            };

            command.SetHandler(async (string method, string solution, string output, string format, bool verbose) =>
            {
                var handler = host.Services.GetRequiredService<FindTestsCommandHandler>();
                var context = new CommandContext(host.Services);
                context.SetParameter("method", method);
                context.SetParameter("solution", solution);
                context.SetParameter("output", output);
                context.SetParameter("format", format);
                context.SetParameter("verbose", verbose);
                
                var exitCode = await handler.ExecuteAsync(context);
                Environment.ExitCode = exitCode;
            }, methodOption, solutionOption, outputOption, formatOption, verboseOption);

            return command;
        }

        private Command CreateTraceExecutionCommand(IHost host)
        {
            var testOption = new Option<string>("--test", "Test method identifier to trace execution for (e.g., 'MyNamespace.MyTestClass.MyTestMethod')") { IsRequired = true };
            testOption.AddAlias("-t");
            
            var solutionOption = new Option<string>("--solution", "Path to solution file or directory") { IsRequired = true };
            solutionOption.AddAlias("-s");
            
            var outputOption = new Option<string>("--output", "Output file path (default: console)");
            outputOption.AddAlias("-o");
            
            var formatOption = new Option<string>("--format", () => "text", "Output format: json, text");
            formatOption.AddAlias("-f");
            
            var verboseOption = new Option<bool>("--verbose", "Enable verbose output with call paths and method details");
            verboseOption.AddAlias("-v");
            
            var maxDepthOption = new Option<int>("--max-depth", () => 20, "Maximum call depth to trace (default: 20)");
            maxDepthOption.AddAlias("-d");

            var command = new Command("trace-execution", "Trace all production code executed by a test method")
            {
                testOption, solutionOption, outputOption, formatOption, verboseOption, maxDepthOption
            };

            command.SetHandler(async (string test, string solution, string output, string format, bool verbose, int maxDepth) =>
            {
                var handler = host.Services.GetRequiredService<TraceExecutionCommandHandler>();
                var context = new CommandContext(host.Services);
                context.SetParameter("test", test);
                context.SetParameter("solution", solution);
                context.SetParameter("output", output);
                context.SetParameter("format", format);
                context.SetParameter("verbose", verbose);
                context.SetParameter("max-depth", maxDepth);
                
                var exitCode = await handler.ExecuteAsync(context);
                Environment.ExitCode = exitCode;
            }, testOption, solutionOption, outputOption, formatOption, verboseOption, maxDepthOption);

            return command;
        }

        private Command CreateAnalyzeCoverageCommand(IHost host)
        {
            var solutionOption = new Option<string>("--solution", "Path to solution file") { IsRequired = true };
            solutionOption.AddAlias("-s");
            
            var testsOption = new Option<string[]>("--tests", "Test method IDs to analyze (can be specified multiple times)") 
            { 
                IsRequired = true, 
                AllowMultipleArgumentsPerToken = true 
            };
            testsOption.AddAlias("-t");
            
            var diffContentOption = new Option<string>("--diff-content", "Git diff content as string");
            diffContentOption.AddAlias("-d");
            
            var diffFileOption = new Option<string>("--diff-file", "Path to git diff file");
            diffFileOption.AddAlias("-f");
            
            var gitCommandOption = new Option<string>("--git-command", "Git command to generate diff (e.g., 'diff HEAD~1')");
            gitCommandOption.AddAlias("-g");
            
            var outputOption = new Option<string>("--output", "Output file path (default: console)");
            outputOption.AddAlias("-o");
            
            var formatOption = new Option<string>("--format", () => "text", "Output format: json, text");
            formatOption.AddAlias("--fmt");
            
            var verboseOption = new Option<bool>("--verbose", "Enable verbose output with detailed coverage information");
            verboseOption.AddAlias("-v");

            var command = new Command("analyze-coverage", "Analyze how well specific tests cover code changes")
            {
                solutionOption, testsOption, diffContentOption, diffFileOption, gitCommandOption, outputOption, formatOption, verboseOption
            };

            command.SetHandler(async (string solution, string[] tests, string diffContent, string diffFile, string gitCommand, string output, string format, bool verbose) =>
            {
                var handler = host.Services.GetRequiredService<AnalyzeCoverageCommandHandler>();
                var context = new CommandContext(host.Services);
                context.SetParameter("solution", solution);
                context.SetParameter("tests", tests);
                context.SetParameter("diff-content", diffContent);
                context.SetParameter("diff-file", diffFile);
                context.SetParameter("git-command", gitCommand);
                context.SetParameter("output", output);
                context.SetParameter("format", format);
                context.SetParameter("verbose", verbose);
                
                var exitCode = await handler.ExecuteAsync(context);
                Environment.ExitCode = exitCode;
            }, solutionOption, testsOption, diffContentOption, diffFileOption, gitCommandOption, outputOption, formatOption, verboseOption);

            return command;
        }

        private Command CreateConfigCommand(IHost host)
        {
            var initCommand = CreateConfigInitCommand(host);
            var verifyCommand = CreateConfigVerifyCommand(host);

            var command = new Command("config", "Manage TestIntelligence configuration")
            {
                initCommand, verifyCommand
            };

            return command;
        }

        private Command CreateConfigInitCommand(IHost host)
        {
            var pathOption = new Option<string>("--path", "Path to solution directory where configuration file should be created");
            pathOption.AddAlias("-p");

            var command = new Command("init", "Create a default testintel.config file")
            {
                pathOption
            };

            command.SetHandler(async (string path) =>
            {
                var handler = host.Services.GetRequiredService<ConfigCommandHandler>();
                var context = new CommandContext(host.Services);
                context.SetParameter("subcommand", "init");
                context.SetParameter("path", path);
                
                var exitCode = await handler.ExecuteAsync(context);
                Environment.ExitCode = exitCode;
            }, pathOption);

            return command;
        }

        private Command CreateConfigVerifyCommand(IHost host)
        {
            var pathOption = new Option<string>("--path", "Path to solution file or directory") { IsRequired = true };
            pathOption.AddAlias("-p");
            
            var formatOption = new Option<string>("--format", () => "text", "Output format (text or json)");
            formatOption.AddAlias("-f");
            
            var outputOption = new Option<string>("--output", "Output file path (optional)");
            outputOption.AddAlias("-o");

            var command = new Command("verify", "Verify which projects would be included/excluded based on configuration")
            {
                pathOption, formatOption, outputOption
            };

            command.SetHandler(async (string path, string format, string output) =>
            {
                var handler = host.Services.GetRequiredService<ConfigCommandHandler>();
                var context = new CommandContext(host.Services);
                context.SetParameter("subcommand", "verify");
                context.SetParameter("path", path);
                context.SetParameter("format", format);
                context.SetParameter("output", output);
                
                var exitCode = await handler.ExecuteAsync(context);
                Environment.ExitCode = exitCode;
            }, pathOption, formatOption, outputOption);

            return command;
        }

        private Command CreateCacheCommand(IHost host)
        {
            var solutionOption = new Option<string>("--solution", "Path to the solution file (.sln)") { IsRequired = true };
            solutionOption.AddAlias("-s");
            
            var actionOption = new Option<string>("--action", "Cache action to perform") { IsRequired = true };
            actionOption.AddAlias("-a");
            actionOption.FromAmong("status", "clear", "init", "warm-up", "stats");
            
            var cacheDirectoryOption = new Option<string>("--cache-dir", "Custom cache directory (optional)");
            cacheDirectoryOption.AddAlias("-d");
            
            var formatOption = new Option<string>("--format", () => "text", "Output format");
            formatOption.AddAlias("-f");
            formatOption.FromAmong("text", "json");
            
            var verboseOption = new Option<bool>("--verbose", () => false, "Enable verbose output");
            verboseOption.AddAlias("-v");

            var command = new Command("cache", "Manage persistent cache for large solutions")
            {
                solutionOption, actionOption, cacheDirectoryOption, formatOption, verboseOption
            };

            command.SetHandler(async (string solution, string action, string cacheDir, string format, bool verbose) =>
            {
                var handler = host.Services.GetRequiredService<CacheCommandHandler>();
                var context = new CommandContext(host.Services);
                context.SetParameter("solution", solution);
                context.SetParameter("action", action);
                context.SetParameter("cache-dir", cacheDir);
                context.SetParameter("format", format);
                context.SetParameter("verbose", verbose);
                
                var exitCode = await handler.ExecuteAsync(context);
                Environment.ExitCode = exitCode;
            }, solutionOption, actionOption, cacheDirectoryOption, formatOption, verboseOption);

            return command;
        }

        private Command CreateVersionCommand(IHost host)
        {
            var command = new Command("version", "Show version information");

            command.SetHandler(async () =>
            {
                var handler = host.Services.GetRequiredService<VersionCommandHandler>();
                var context = new CommandContext(host.Services);
                
                var exitCode = await handler.ExecuteAsync(context);
                Environment.ExitCode = exitCode;
            });

            return command;
        }

        private Command CreateCompareTestsCommand(IHost host)
        {
            var test1Option = new Option<string>("--test1", "First test method identifier to compare (format: Namespace.ClassName.MethodName)") { IsRequired = true };
            test1Option.AddAlias("-t1");
            
            var test2Option = new Option<string>("--test2", "Second test method identifier to compare (format: Namespace.ClassName.MethodName)") { IsRequired = true };
            test2Option.AddAlias("-t2");
            
            var solutionOption = new Option<string>("--solution", "Path to solution file (.sln) or project directory") { IsRequired = true };
            solutionOption.AddAlias("-s");
            
            var formatOption = new Option<string>("--format", () => "text", "Output format: text, json");
            formatOption.AddAlias("-f");
            formatOption.FromAmong("text", "json");
            
            var outputOption = new Option<string>("--output", "Output file path (default: console)");
            outputOption.AddAlias("-o");
            
            var depthOption = new Option<string>("--depth", () => "medium", "Analysis depth: shallow, medium, deep");
            depthOption.AddAlias("-d");
            depthOption.FromAmong("shallow", "medium", "deep");
            
            var verboseOption = new Option<bool>("--verbose", () => false, "Enable verbose output with detailed analysis");
            verboseOption.AddAlias("-v");
            
            var includePerformanceOption = new Option<bool>("--include-performance", () => false, "Include performance metrics in output");
            
            var command = new Command("compare-tests", "Compare two test methods and analyze their overlap and similarities")
            {
                test1Option, test2Option, solutionOption, formatOption, outputOption, 
                depthOption, verboseOption, includePerformanceOption
            };

            command.SetHandler(async (string test1, string test2, string solution, string format, string output, 
                string depth, bool verbose, bool includePerformance) =>
            {
                var handler = host.Services.GetRequiredService<CompareTestsCommandHandler>();
                var context = new CommandContext(host.Services);
                context.SetParameter("test1", test1);
                context.SetParameter("test2", test2);
                context.SetParameter("solution", solution);
                context.SetParameter("format", format);
                context.SetParameter("output", output);
                context.SetParameter("depth", depth);
                context.SetParameter("verbose", verbose);
                context.SetParameter("include-performance", includePerformance);
                
                var exitCode = await handler.ExecuteAsync(context);
                Environment.ExitCode = exitCode;
            }, test1Option, test2Option, solutionOption, formatOption, outputOption, 
               depthOption, verboseOption, includePerformanceOption);

            return command;
        }
    }
}