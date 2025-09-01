using FluentAssertions;
using TestIntelligence.E2E.Tests.Helpers;
using Xunit;

namespace TestIntelligence.E2E.Tests.Commands;

[Collection("E2E Tests")]
public class DiffCommandE2ETests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    [Fact]
    public async Task Diff_WithDiffContent_ReturnsImpactAnalysis()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();
        var diffContent = @"
diff --git a/src/TestClass.cs b/src/TestClass.cs
index abc123..def456 100644
--- a/src/TestClass.cs
+++ b/src/TestClass.cs
@@ -10,7 +10,7 @@ namespace TestNamespace
     {
         public void TestMethod()
         {
-            Console.WriteLine(""Old implementation"");
+            Console.WriteLine(""New implementation"");
         }
     }
";

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("diff", 
            $"--solution \"{solutionPath}\" --diff-content \"{diffContent}\"");

        // Assert
        result.Success.Should().BeTrue($"Command should succeed. Error: {result.StandardError}");
        result.StandardOutput.Should().Contain("Test Impact Analysis Results");
    }

    [Fact]
    public async Task Diff_WithDiffFile_ReadsFileAndAnalyzes()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();
        var diffFile = CreateTempFile(".patch");
        var diffContent = @"
diff --git a/src/TestClass.cs b/src/TestClass.cs
index abc123..def456 100644
--- a/src/TestClass.cs
+++ b/src/TestClass.cs
@@ -10,7 +10,7 @@ namespace TestNamespace
         public void TestMethod()
         {
-            Console.WriteLine(""Old implementation"");
+            Console.WriteLine(""New implementation"");
         }
";
        await File.WriteAllTextAsync(diffFile, diffContent);

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("diff", 
            $"--solution \"{solutionPath}\" --diff-file \"{diffFile}\"");

        // Assert
        result.Success.Should().BeTrue();
        result.StandardOutput.Should().Contain("Test Impact Analysis Results");
    }

    [Fact]
    public async Task Diff_WithGitCommand_ExecutesGitAndAnalyzes()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("diff", 
            $"--solution \"{solutionPath}\" --git-command \"diff HEAD~1\"", timeoutMs: 45000);

        // Assert - Git command might fail if no commits, but should handle gracefully
        if (result.Success)
        {
            result.StandardOutput.Should().Contain("Test Impact Analysis Results");
        }
        else
        {
            // Should handle git errors gracefully
            result.StandardError.Should().Contain("Error analyzing diff:");
        }
    }

    [Fact]
    public async Task Diff_WithJsonOutput_ReturnsValidJson()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();
        var diffContent = @"
diff --git a/src/TestClass.cs b/src/TestClass.cs
index abc123..def456 100644
--- a/src/TestClass.cs
+++ b/src/TestClass.cs
@@ -10,7 +10,7 @@ namespace TestNamespace
         public void TestMethod()
         {
-            Console.WriteLine(""Old implementation"");
+            Console.WriteLine(""New implementation"");
         }
";

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("diff", 
            $"--solution \"{solutionPath}\" --diff-content \"{diffContent}\" --format json");

        // Assert
        result.Success.Should().BeTrue();
        result.StandardOutput.Should().Contain("{");
        result.StandardOutput.Should().Contain("\"TotalChanges\"");
    }

    [Fact]
    public async Task Diff_WithOutputFile_WritesToFile()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();
        var outputFile = CreateTempFile(".txt");
        var diffContent = @"
diff --git a/src/TestClass.cs b/src/TestClass.cs
index abc123..def456 100644
--- a/src/TestClass.cs
+++ b/src/TestClass.cs
@@ -10,7 +10,7 @@ namespace TestNamespace
         public void TestMethod()
         {
-            Console.WriteLine(""Old implementation"");
+            Console.WriteLine(""New implementation"");
         }
";

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("diff", 
            $"--solution \"{solutionPath}\" --diff-content \"{diffContent}\" --output \"{outputFile}\"");

        // Assert
        result.Success.Should().BeTrue();
        result.StandardOutput.Should().Contain($"Diff analysis results written to: {outputFile}");
        File.Exists(outputFile).Should().BeTrue();
        
        var fileContent = await File.ReadAllTextAsync(outputFile);
        fileContent.Should().Contain("Test Impact Analysis Results");
    }

    [Fact]
    public async Task Diff_WithMultipleDiffSources_FailsValidation()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();
        var diffContent = "sample diff";
        var diffFile = CreateTempFile(".patch");
        await File.WriteAllTextAsync(diffFile, "sample diff");

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("diff", 
            $"--solution \"{solutionPath}\" --diff-content \"{diffContent}\" --diff-file \"{diffFile}\"");

        // Assert
        result.Success.Should().BeFalse();
        result.StandardError.Should().Contain("Cannot specify multiple diff sources");
    }

    [Fact]
    public async Task Diff_WithNoDiffSource_FailsValidation()
    {
        // Arrange
        var solutionPath = GetTestSolutionPath();

        // Act
        var result = await CliTestHelper.RunCliCommandAsync("diff", 
            $"--solution \"{solutionPath}\"");

        // Assert
        result.Success.Should().BeFalse();
        result.StandardError.Should().Contain("Must specify exactly one diff source");
    }

    [Fact]
    public async Task Diff_WithMissingRequiredArguments_ShowsHelp()
    {
        // Act
        var result = await CliTestHelper.RunCliCommandAsync("diff", "");

        // Assert
        result.Success.Should().BeFalse();
        result.StandardError.Should().Contain("Required option");
        result.StandardError.Should().Contain("--solution");
    }

    [Fact]
    public async Task Diff_CommandExists_InHelpOutput()
    {
        // Act
        var result = await CliTestHelper.RunCliCommandAsync("--help", "");

        // Assert
        result.Success.Should().BeTrue();
        result.StandardOutput.Should().Contain("diff");
        result.StandardOutput.Should().Contain("Analyze test impact from git diff");
    }

    private string GetTestSolutionPath()
    {
        var solutionPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, 
            "..", "..", "..", "..", "..", 
            "TestIntelligence.sln");
        
        return Path.GetFullPath(solutionPath);
    }

    private string CreateTempFile(string extension)
    {
        var tempFile = Path.GetTempFileName();
        var newFile = Path.ChangeExtension(tempFile, extension);
        File.Delete(tempFile);
        _tempFiles.Add(newFile);
        return newFile;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }
}