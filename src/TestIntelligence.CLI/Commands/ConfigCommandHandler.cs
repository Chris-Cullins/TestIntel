using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.CLI.Services;
using TestIntelligence.CLI.Models;

namespace TestIntelligence.CLI.Commands;

/// <summary>
/// Command handler for the config command.
/// Manages TestIntelligence configuration.
/// </summary>
public class ConfigCommandHandler : BaseCommandHandler
{
    public ConfigCommandHandler(ILogger<ConfigCommandHandler> logger) : base(logger)
    {
    }

    protected override async Task<int> ExecuteInternalAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Extract subcommand from context
        var subcommand = context.GetParameter<string>("subcommand");
        
        if (string.IsNullOrEmpty(subcommand))
        {
            Logger.LogError("Config subcommand is required. Use 'init' or 'verify'.");
            Console.WriteLine("Error: Config subcommand is required. Use 'init' or 'verify'.");
            return 1;
        }
        
        switch (subcommand.ToLower())
        {
            case "init":
                return await HandleInitAsync(context, cancellationToken);
            case "verify":
                return await HandleVerifyAsync(context, cancellationToken);
            default:
                Logger.LogError("Unknown config subcommand: {Subcommand}", subcommand);
                Console.WriteLine($"Error: Unknown config subcommand: {subcommand}. Use 'init' or 'verify'.");
                return 1;
        }
    }

    private async Task<int> HandleInitAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var path = context.GetParameter<string>("path");
        
        Logger.LogInformation("Initializing configuration at path: {Path}", path ?? "current directory");
        
        var configurationService = context.GetService<IConfigurationService>();
        
        // Use current directory if no path specified
        var targetDirectory = string.IsNullOrEmpty(path) ? Directory.GetCurrentDirectory() : path;
        
        // If path is a .sln file, use its directory
        if (!string.IsNullOrEmpty(path) && File.Exists(path) && path.EndsWith(".sln"))
        {
            targetDirectory = Path.GetDirectoryName(path)!;
        }
        
        var configPath = Path.Combine(targetDirectory, "testintel.config");
        
        if (File.Exists(configPath))
        {
            Console.WriteLine($"Configuration file already exists: {configPath}");
            Console.Write("Overwrite? (y/N): ");
            var response = Console.ReadLine();
            if (!string.Equals(response, "y", StringComparison.OrdinalIgnoreCase) && 
                !string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Configuration creation cancelled.");
                return 0;
            }
        }
        
        var success = await configurationService.CreateDefaultConfigurationAsync(configPath);
        
        if (success)
        {
            Console.WriteLine($"✓ Created default configuration file: {configPath}");
            Console.WriteLine();
            Console.WriteLine("Edit this file to customize project filtering and analysis settings.");
            Console.WriteLine("Common configuration options:");
            Console.WriteLine("  - Exclude specific projects or patterns");
            Console.WriteLine("  - Filter by project types (ORM, database, etc.)");
            Console.WriteLine("  - Set default output formats and directories");
            return 0;
        }
        else
        {
            Console.WriteLine("✗ Failed to create configuration file.");
            return 1;
        }
    }

    private async Task<int> HandleVerifyAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Validate required parameters for verify subcommand
        ValidateRequiredParameters(context, "path");
        
        var path = context.GetParameter<string>("path");
        var format = context.GetParameter<string>("format") ?? "text";
        var output = context.GetParameter<string>("output");
        
        Logger.LogInformation("Verifying project filtering for path: {Path}", path);
        
        var configurationService = context.GetService<IConfigurationService>();
        var outputFormatter = context.GetService<IOutputFormatter>();
        
        Console.WriteLine($"Analyzing project filtering for: {path}");
        
        // Load configuration
        var configuration = await configurationService.LoadConfigurationAsync(path!);
        
        // Analyze project filtering
        var analysisResult = await configurationService.AnalyzeProjectFilteringAsync(path!, configuration);
        
        // Format and display results
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            await outputFormatter.WriteOutputAsync(analysisResult, "json", output);
        }
        else
        {
            await DisplayProjectAnalysisAsync(analysisResult, output);
        }
        
        return 0;
    }

    private static async Task DisplayProjectAnalysisAsync(ProjectFilterAnalysisResult result, string? outputPath)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        var output = new StringBuilder();
        
        // Header
        output.AppendLine("=== TestIntelligence Project Filtering Analysis ===");
        output.AppendLine($"Solution: {result.SolutionPath ?? "Unknown"}");
        output.AppendLine($"Analysis Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        output.AppendLine();
        
        // Summary - Fix null reference by initializing if needed
        output.AppendLine("=== SUMMARY ===");
        var summary = result.Summary ?? new ProjectFilterSummary();
        output.AppendLine($"Total Projects: {summary.TotalProjects}");
        output.AppendLine($"✅ Included: {summary.IncludedProjects}");
        output.AppendLine($"❌ Excluded: {summary.ExcludedProjects}");
        output.AppendLine($"🧪 Test Projects: {summary.TestProjects}");
        output.AppendLine($"🏭 Production Projects: {summary.ProductionProjects}");
        output.AppendLine();

        // Configuration Summary
        output.AppendLine("=== CONFIGURATION APPLIED ===");
        var config = result.Configuration ?? new TestIntelConfiguration();
        output.AppendLine($"Test Projects Only: {config.Projects.TestProjectsOnly}");
        
        if (config.Projects.Include?.Any() == true)
        {
            output.AppendLine($"Include Patterns: {string.Join(", ", config.Projects.Include)}");
        }
        
        if (config.Projects.Exclude?.Any() == true)
        {
            output.AppendLine($"Exclude Patterns: {string.Join(", ", config.Projects.Exclude)}");
        }
        
        if (config.Projects.ExcludeTypes?.Any() == true)
        {
            output.AppendLine($"Exclude Types: {string.Join(", ", config.Projects.ExcludeTypes)}");
        }
        output.AppendLine();

        // Project Details
        output.AppendLine("=== PROJECT DETAILS ===");
        
        // Group by included/excluded
        var includedProjects = result.Projects?.Where(p => p.IsIncluded).OrderBy(p => p.ProjectName).ToList() ?? new List<ProjectAnalysisDetail>();
        var excludedProjects = result.Projects?.Where(p => !p.IsIncluded).OrderBy(p => p.ProjectName).ToList() ?? new List<ProjectAnalysisDetail>();

        if (includedProjects.Any())
        {
            output.AppendLine($"✅ INCLUDED PROJECTS ({includedProjects.Count}):");
            foreach (var project in includedProjects)
            {
                var typeInfo = project.DetectedType != null ? $" [{project.DetectedType}]" : "";
                var testInfo = project.IsTestProject ? " (Test)" : " (Prod)";
                output.AppendLine($"  • {project.ProjectName}{typeInfo}{testInfo}");
                
                if (project.FilteringReasons != null)
                {
                    foreach (var reason in project.FilteringReasons)
                    {
                        output.AppendLine($"    └─ {reason}");
                    }
                }
                output.AppendLine();
            }
        }

        if (excludedProjects.Any())
        {
            output.AppendLine($"❌ EXCLUDED PROJECTS ({excludedProjects.Count}):");
            foreach (var project in excludedProjects)
            {
                var typeInfo = project.DetectedType != null ? $" [{project.DetectedType}]" : "";
                var testInfo = project.IsTestProject ? " (Test)" : " (Prod)";
                output.AppendLine($"  • {project.ProjectName}{typeInfo}{testInfo}");
                
                if (project.FilteringReasons != null)
                {
                    foreach (var reason in project.FilteringReasons)
                    {
                        output.AppendLine($"    └─ {reason}");
                    }
                }
                output.AppendLine();
            }
        }

        var outputText = output.ToString();
        
        if (!string.IsNullOrEmpty(outputPath))
        {
            await File.WriteAllTextAsync(outputPath, outputText);
            Console.WriteLine($"✓ Analysis saved to: {outputPath}");
        }
        else
        {
            Console.Write(outputText);
        }
    }
}