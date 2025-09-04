using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.ImpactAnalyzer.Models;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public interface IGitDiffParser
    {
        Task<CodeChangeSet> ParseDiffAsync(string diffContent);
        Task<CodeChangeSet> ParseDiffFileAsync(string diffFilePath);
        Task<CodeChangeSet> ParseDiffFromCommandAsync(string gitCommand);
    }

    public class GitDiffParser : IGitDiffParser
    {
        private readonly ILogger<GitDiffParser> _logger;
        private readonly IRoslynAnalyzer _roslynAnalyzer;

        // Git diff regex patterns
        private static readonly Regex FileHeaderPattern = new Regex(@"^diff --git a/(.*) b/(.*)$", RegexOptions.Multiline);
        private static readonly Regex FileChangePattern = new Regex(@"^(\+\+\+|---) (.*)$", RegexOptions.Multiline);
        private static readonly Regex HunkHeaderPattern = new Regex(@"^@@ -(\d+),?(\d*) \+(\d+),?(\d*) @@", RegexOptions.Multiline);
        private static readonly Regex AddedLinePattern = new Regex(@"^\+(.*)$", RegexOptions.Multiline);
        private static readonly Regex RemovedLinePattern = new Regex(@"^-(.*)$", RegexOptions.Multiline);
        
        // C# method detection patterns - match method names in various contexts
        private static readonly Regex MethodSignaturePattern = new Regex(
            @"(?:(?:public|private|protected|internal)\s+)?(?:(?:static|virtual|override|async|abstract)\s+)*(?:Task<?[\w<>,\s]*>?|void|bool|int|string|[\w<>\[\],]+)\s+(\w+)(?:<[^>]*>)?\s*\([^)]*\)(?:\s+where\s+[^{]*)?\s*",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        
        // Additional patterns for method-related changes
        private static readonly Regex SimplerMethodPattern = new Regex(
            @"(\w+)\s*\([^)]*\)\s*(?:;|{|\s*$)",
            RegexOptions.Multiline);
            
        private static readonly Regex VariableAssignmentPattern = new Regex(
            @"var\s+(\w+)\s*=|(\w+)\s+(\w+)\s*=",
            RegexOptions.Multiline);

        public GitDiffParser(ILogger<GitDiffParser> logger, IRoslynAnalyzer roslynAnalyzer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _roslynAnalyzer = roslynAnalyzer ?? throw new ArgumentNullException(nameof(roslynAnalyzer));
        }

        public Task<CodeChangeSet> ParseDiffAsync(string diffContent)
        {
            if (string.IsNullOrWhiteSpace(diffContent))
            {
                _logger.LogWarning("Empty diff content provided");
                return Task.FromResult(new CodeChangeSet(new List<CodeChange>()));
            }

            _logger.LogInformation("Parsing git diff content ({Length} characters)", diffContent.Length);

            var changes = new List<CodeChange>();
            var lines = diffContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            var currentFile = string.Empty;
            var currentChangeType = CodeChangeType.Modified;
            var changedLines = new List<string>();
            
            foreach (var line in lines)
            {
                // Check for file header
                var fileMatch = FileHeaderPattern.Match(line);
                if (fileMatch.Success)
                {
                    // Process previous file if exists
                    if (!string.IsNullOrEmpty(currentFile) && changedLines.Any())
                    {
                        var change = CreateCodeChange(currentFile, currentChangeType, changedLines);
                        if (change != null)
                            changes.Add(change);
                    }

                    currentFile = fileMatch.Groups[2].Value;
                    currentChangeType = CodeChangeType.Modified; // Default to modified
                    changedLines.Clear();
                    continue;
                }

                // Check for special file mode changes
                if (line.StartsWith("deleted file mode"))
                {
                    currentChangeType = CodeChangeType.Deleted;
                    continue;
                }
                
                if (line.StartsWith("new file mode"))
                {
                    currentChangeType = CodeChangeType.Added;
                    continue;
                }

                // Check for file change markers
                var changeMatch = FileChangePattern.Match(line);
                if (changeMatch.Success)
                {
                    var filePath = changeMatch.Groups[2].Value;
                    if (filePath.StartsWith("b/"))
                        filePath = filePath.Substring(2);
                    if (filePath == "/dev/null")
                        currentChangeType = line.StartsWith("---") ? CodeChangeType.Added : CodeChangeType.Deleted;
                    continue;
                }

                // Skip hunk headers
                if (HunkHeaderPattern.IsMatch(line))
                    continue;

                // Collect changed lines (added or removed)
                if (line.StartsWith("+") && !line.StartsWith("+++"))
                {
                    changedLines.Add(line.Substring(1));
                }
                else if (line.StartsWith("-") && !line.StartsWith("---"))
                {
                    changedLines.Add(line.Substring(1));
                }
            }

            // Process the last file
            if (!string.IsNullOrEmpty(currentFile) && changedLines.Any())
            {
                var change = CreateCodeChange(currentFile, currentChangeType, changedLines);
                if (change != null)
                    changes.Add(change);
            }

            _logger.LogInformation("Parsed {ChangeCount} code changes from diff", changes.Count);
            foreach (var change in changes)
            {
                _logger.LogDebug("Code change: {FilePath} ({ChangeType}) - {MethodCount} methods: [{Methods}]", 
                    change.FilePath, change.ChangeType, change.ChangedMethods.Count, 
                    string.Join(", ", change.ChangedMethods));
            }
            
            return Task.FromResult(new CodeChangeSet(changes));
        }

        public async Task<CodeChangeSet> ParseDiffFileAsync(string diffFilePath)
        {
            if (!File.Exists(diffFilePath))
                throw new FileNotFoundException($"Diff file not found: {diffFilePath}");

            _logger.LogInformation("Reading diff from file: {FilePath}", diffFilePath);
            var diffContent = File.ReadAllText(diffFilePath);
            return await ParseDiffAsync(diffContent);
        }

        public async Task<CodeChangeSet> ParseDiffFromCommandAsync(string gitCommand)
        {
            _logger.LogInformation("Executing git command: {Command}", gitCommand);
            
            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = gitCommand.StartsWith("git ") ? gitCommand.Substring(4) : gitCommand,
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                if (process == null)
                    throw new InvalidOperationException("Failed to start git process");

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    _logger.LogError("Git command failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
                    throw new InvalidOperationException($"Git command failed: {error}");
                }

                return await ParseDiffAsync(output);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute git command: {Command}", gitCommand);
                throw;
            }
        }

        private CodeChange? CreateCodeChange(string filePath, CodeChangeType changeType, List<string> changedLines)
        {
            try
            {
                // Only analyze C# files
                if (!filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    return null;

                var changedMethods = ExtractMethodNames(changedLines);
                var changedTypes = ExtractTypeNames(changedLines);

                // If we have any changed lines in a C# file, create a change even if we can't detect specific methods/types
                // This ensures we don't lose changes due to parsing limitations
                if (!changedLines.Any())
                    return null;

                _logger.LogDebug("Creating code change for {FilePath}: {MethodCount} methods, {TypeCount} types, {LineCount} changed lines", 
                    filePath, changedMethods.Count, changedTypes.Count, changedLines.Count);

                return new CodeChange(filePath, changeType, changedMethods.ToList(), changedTypes.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create code change for file: {FilePath}", filePath);
                return null;
            }
        }

        private static CodeChangeType DetermineChangeType(string diffLine)
        {
            if (diffLine.Contains("/dev/null"))
                return diffLine.StartsWith("---") ? CodeChangeType.Added : CodeChangeType.Deleted;
            
            return CodeChangeType.Modified;
        }

        private HashSet<string> ExtractMethodNames(List<string> lines)
        {
            var methodNames = new HashSet<string>();

            foreach (var line in lines)
            {
                // Try primary method pattern
                var matches = MethodSignaturePattern.Matches(line);
                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        var methodName = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(methodName) && IsValidMethodName(methodName))
                        {
                            methodNames.Add(methodName);
                            _logger.LogDebug("Found method via primary pattern: {MethodName}", methodName);
                        }
                    }
                }

                // Try simpler method pattern for method calls and definitions
                var simpleMatches = SimplerMethodPattern.Matches(line);
                foreach (Match match in simpleMatches)
                {
                    if (match.Groups.Count > 1)
                    {
                        var methodName = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(methodName) && IsValidMethodName(methodName))
                        {
                            methodNames.Add(methodName);
                            _logger.LogDebug("Found method via simple pattern: {MethodName}", methodName);
                        }
                    }
                }

                // Skip variable extraction as it creates noise in method detection
                // Focus on actual method signatures and method calls only
            }

            return methodNames;
        }

        private HashSet<string> ExtractTypeNames(List<string> lines)
        {
            var typeNames = new HashSet<string>();
            var classPattern = new Regex(@"(public|private|protected|internal|\s)+(abstract\s+|sealed\s+)?(class|interface|struct|enum)\s+(\w+)", RegexOptions.IgnoreCase);

            foreach (var line in lines)
            {
                var matches = classPattern.Matches(line);
                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 4)
                    {
                        var typeName = match.Groups[4].Value.Trim();
                        if (!string.IsNullOrEmpty(typeName))
                        {
                            typeNames.Add(typeName);
                        }
                    }
                }
            }

            return typeNames;
        }

        private static bool IsValidMethodName(string methodName)
        {
            if (string.IsNullOrWhiteSpace(methodName))
                return false;
                
            // Filter out common non-method patterns
            if (methodName.StartsWith("Variable_", StringComparison.OrdinalIgnoreCase) ||
                methodName.Contains("Regex", StringComparison.OrdinalIgnoreCase) ||
                methodName.Length < 2 ||
                methodName.All(char.IsDigit))
                return false;
                
            // Basic validation to filter out keywords and invalid identifiers
            var csharpKeywords = new HashSet<string>
            {
                "if", "else", "while", "for", "foreach", "do", "switch", "case", "return", "break", "continue",
                "try", "catch", "finally", "throw", "using", "namespace", "class", "interface", "struct", "enum",
                "var", "new", "this", "base", "null", "true", "false", "static", "public", "private", "protected",
                "internal", "override", "virtual", "abstract", "sealed", "readonly", "const", "async", "await"
            };

            return !csharpKeywords.Contains(methodName.ToLower()) && 
                   char.IsLetter(methodName[0]) &&
                   methodName.All(c => char.IsLetterOrDigit(c) || c == '_');
        }
    }
}