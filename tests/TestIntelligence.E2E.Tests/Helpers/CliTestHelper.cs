using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace TestIntelligence.E2E.Tests.Helpers;

public static class CliTestHelper
{
    private static readonly string CliExecutablePath = GetCliExecutablePath();
    
    public static Task<CliResult> RunCliCommandAsync(string command, string arguments = "", int timeoutMs = 180000)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"{CliExecutablePath} {command} {arguments}".Trim(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var output = new StringBuilder();
        var error = new StringBuilder();
        
        using var process = new Process { StartInfo = startInfo };
        
        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                output.AppendLine(e.Data);
        };
        
        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                error.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completed = process.WaitForExit(timeoutMs);
        
        if (!completed)
        {
            process.Kill();
            throw new TimeoutException($"CLI command timed out after {timeoutMs}ms");
        }

        return Task.FromResult(new CliResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = output.ToString(),
            StandardError = error.ToString(),
            Success = process.ExitCode == 0
        });
    }

    public static async Task<T> RunCliCommandWithJsonOutputAsync<T>(string command, string arguments = "", int timeoutMs = 180000)
    {
        var result = await RunCliCommandAsync(command, $"{arguments} --format json", timeoutMs);
        
        result.Success.Should().BeTrue($"CLI command failed with exit code {result.ExitCode}. Error: {result.StandardError}");
        result.StandardOutput.Should().NotBeNullOrWhiteSpace("CLI should produce JSON output");

        try
        {
            return JsonSerializer.Deserialize<T>(result.StandardOutput, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })!;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse CLI JSON output: {ex.Message}. Output was: {result.StandardOutput}");
        }
    }

    public static string CreateTempSolutionFile()
    {
        var tempPath = Path.GetTempFileName();
        File.WriteAllText(tempPath, @"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
EndGlobal");
        return tempPath;
    }

    public static string CreateTempCsFile(string className, string methodName, string? content = null)
    {
        var tempPath = Path.GetTempFileName();
        var newPath = Path.ChangeExtension(tempPath, ".cs");
        File.Delete(tempPath);

        var fileContent = content ?? $@"
using System;

namespace TestNamespace
{{
    public class {className}
    {{
        public void {methodName}()
        {{
            Console.WriteLine(""Hello from {methodName}"");
        }}
    }}
}}";

        File.WriteAllText(newPath, fileContent);
        return newPath;
    }

    private static string GetCliExecutablePath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var solutionRoot = FindSolutionRoot(baseDir) ?? baseDir;
        
        var cliPath = Path.Combine(solutionRoot, "src", "TestIntelligence.CLI", "bin", "Debug", "net8.0", "TestIntelligence.CLI.dll");
        
        if (!File.Exists(cliPath))
        {
            throw new FileNotFoundException($"CLI executable not found at {cliPath}. Please build the solution first.");
        }
        
        return cliPath;
    }
    
    private static string? FindSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        
        while (current != null)
        {
            if (current.GetFiles("*.sln").Any())
                return current.FullName;
            current = current.Parent;
        }
        
        return null;
    }
}

public class CliResult
{
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
    public bool Success { get; set; }
}