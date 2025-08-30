using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Models;

namespace TestIntelligence.ImpactAnalyzer.Tests.Analysis
{
    public class GitDiffParserTests
    {
        private readonly ILogger<GitDiffParser> _logger;
        private readonly IRoslynAnalyzer _roslynAnalyzer;
        private readonly GitDiffParser _parser;

        public GitDiffParserTests()
        {
            _logger = Substitute.For<ILogger<GitDiffParser>>();
            _roslynAnalyzer = Substitute.For<IRoslynAnalyzer>();
            _parser = new GitDiffParser(_logger, _roslynAnalyzer);
        }

        [Fact]
        public async Task ParseDiffAsync_WithEmptyContent_ReturnsEmptyChangeSet()
        {
            // Act
            var result = await _parser.ParseDiffAsync("");

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.Changes);
        }

        [Fact]
        public async Task ParseDiffAsync_WithNullContent_ReturnsEmptyChangeSet()
        {
            // Act
            var result = await _parser.ParseDiffAsync(null!);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.Changes);
        }

        [Fact]
        public async Task ParseDiffAsync_WithSimpleMethodChange_ReturnsCorrectCodeChange()
        {
            // Arrange
            var diffContent = @"diff --git a/src/MyClass.cs b/src/MyClass.cs
index abc123..def456 100644
--- a/src/MyClass.cs
+++ b/src/MyClass.cs
@@ -10,7 +10,7 @@ public class MyClass
     {
-        return oldMethod();
+        return newMethod();
     }
 
+    public void AddedMethod()
+    {
+        Console.WriteLine(""New method"");
+    }";

            // Act
            var result = await _parser.ParseDiffAsync(diffContent);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Changes);
            
            var change = result.Changes.First();
            Assert.Equal("src/MyClass.cs", change.FilePath);
            Assert.Equal(CodeChangeType.Modified, change.ChangeType);
        }

        [Fact]
        public async Task ParseDiffAsync_WithMethodSignature_ExtractsMethodName()
        {
            // Arrange
            var diffContent = @"diff --git a/src/Calculator.cs b/src/Calculator.cs
index abc123..def456 100644
--- a/src/Calculator.cs
+++ b/src/Calculator.cs
@@ -5,0 +6,5 @@ public class Calculator
+    public int Add(int a, int b)
+    {
+        return a + b;
+    }
+";

            // Act
            var result = await _parser.ParseDiffAsync(diffContent);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Changes);
            
            var change = result.Changes.First();
            Assert.Contains("Add", change.ChangedMethods);
        }

        [Fact]
        public async Task ParseDiffAsync_WithClassDefinition_ExtractsTypeName()
        {
            // Arrange
            var diffContent = @"diff --git a/src/NewClass.cs b/src/NewClass.cs
new file mode 100644
index 0000000..abc123
--- /dev/null
+++ b/src/NewClass.cs
@@ -0,0 +1,10 @@
+using System;
+
+namespace MyNamespace
+{
+    public class NewCalculator
+    {
+        public int Multiply(int a, int b) => a * b;
+    }
+}";

            // Act
            var result = await _parser.ParseDiffAsync(diffContent);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Changes);
            
            var change = result.Changes.First();
            Assert.Equal(CodeChangeType.Added, change.ChangeType);
            Assert.Contains("NewCalculator", change.ChangedTypes);
            Assert.Contains("Multiply", change.ChangedMethods);
        }

        [Fact]
        public async Task ParseDiffAsync_WithDeletedFile_ReturnsDeletedChangeType()
        {
            // Arrange
            var diffContent = @"diff --git a/src/OldClass.cs b/src/OldClass.cs
deleted file mode 100644
index abc123..0000000
--- a/src/OldClass.cs
+++ /dev/null
@@ -1,10 +0,0 @@
-using System;
-
-namespace MyNamespace
-{
-    public class OldClass
-    {
-        public void OldMethod() { }
-    }
-}";

            // Act
            var result = await _parser.ParseDiffAsync(diffContent);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Changes);
            
            var change = result.Changes.First();
            Assert.Equal(CodeChangeType.Deleted, change.ChangeType);
            Assert.Contains("OldClass", change.ChangedTypes);
            Assert.Contains("OldMethod", change.ChangedMethods);
        }

        [Fact]
        public async Task ParseDiffAsync_WithMultipleFiles_ReturnsMultipleChanges()
        {
            // Arrange
            var diffContent = @"diff --git a/src/ClassA.cs b/src/ClassA.cs
index abc123..def456 100644
--- a/src/ClassA.cs
+++ b/src/ClassA.cs
@@ -1,3 +1,4 @@
+    public void NewMethodA() { }
 
diff --git a/src/ClassB.cs b/src/ClassB.cs
index xyz789..uvw012 100644
--- a/src/ClassB.cs
+++ b/src/ClassB.cs
@@ -1,3 +1,4 @@
+    public void NewMethodB() { }";

            // Act
            var result = await _parser.ParseDiffAsync(diffContent);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Changes.Count);
            
            var changeA = result.Changes.FirstOrDefault(c => c.FilePath.Contains("ClassA"));
            var changeB = result.Changes.FirstOrDefault(c => c.FilePath.Contains("ClassB"));
            
            Assert.NotNull(changeA);
            Assert.NotNull(changeB);
            Assert.Contains("NewMethodA", changeA.ChangedMethods);
            Assert.Contains("NewMethodB", changeB.ChangedMethods);
        }

        [Fact]
        public async Task ParseDiffAsync_WithNonCSharpFiles_IgnoresFiles()
        {
            // Arrange
            var diffContent = @"diff --git a/README.md b/README.md
index abc123..def456 100644
--- a/README.md
+++ b/README.md
@@ -1,3 +1,4 @@
 # My Project
+Added new documentation
 
diff --git a/src/MyClass.cs b/src/MyClass.cs
index xyz789..uvw012 100644
--- a/src/MyClass.cs
+++ b/src/MyClass.cs
@@ -1,3 +1,4 @@
+    public void NewMethod() { }";

            // Act
            var result = await _parser.ParseDiffAsync(diffContent);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Changes); // Only the .cs file should be included
            
            var change = result.Changes.First();
            Assert.Equal("src/MyClass.cs", change.FilePath);
            Assert.Contains("NewMethod", change.ChangedMethods);
        }

        [Fact]
        public async Task ParseDiffFileAsync_WithValidFile_ParsesContent()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            var diffContent = @"diff --git a/src/Test.cs b/src/Test.cs
index abc123..def456 100644
--- a/src/Test.cs
+++ b/src/Test.cs
@@ -1,3 +1,4 @@
+    public void TestMethod() { }";
            
            await File.WriteAllTextAsync(tempFile, diffContent);

            try
            {
                // Act
                var result = await _parser.ParseDiffFileAsync(tempFile);

                // Assert
                Assert.NotNull(result);
                Assert.Single(result.Changes);
                var change = result.Changes.First();
                Assert.Equal("src/Test.cs", change.FilePath);
                Assert.Contains("TestMethod", change.ChangedMethods);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ParseDiffFileAsync_WithNonExistentFile_ThrowsFileNotFoundException()
        {
            // Arrange
            var nonExistentFile = "non-existent-file.diff";

            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(() => 
                _parser.ParseDiffFileAsync(nonExistentFile));
        }

        [Fact]
        public async Task ParseDiffAsync_WithComplexMethodSignatures_ExtractsCorrectNames()
        {
            // Arrange
            var diffContent = @"diff --git a/src/ComplexClass.cs b/src/ComplexClass.cs
index abc123..def456 100644
--- a/src/ComplexClass.cs
+++ b/src/ComplexClass.cs
@@ -1,10 +1,15 @@
+    public async Task<List<string>> GetItemsAsync(int pageSize, CancellationToken token)
+    {
+        return await repository.GetItemsAsync(pageSize, token);
+    }
+
+    private static bool IsValid<T>(T item) where T : IValidatable
+    {
+        return item.IsValid;
+    }";

            // Act
            var result = await _parser.ParseDiffAsync(diffContent);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Changes);
            
            var change = result.Changes.First();
            Assert.Contains("GetItemsAsync", change.ChangedMethods);
            Assert.Contains("IsValid", change.ChangedMethods);
        }

        [Fact]
        public async Task ParseDiffAsync_WithInterfaceAndAbstractClass_ExtractsTypes()
        {
            // Arrange
            var diffContent = @"diff --git a/src/Types.cs b/src/Types.cs
index abc123..def456 100644
--- a/src/Types.cs
+++ b/src/Types.cs
@@ -1,10 +1,20 @@
+    public interface IRepository<T>
+    {
+        Task<T> GetByIdAsync(int id);
+    }
+
+    public abstract class BaseService
+    {
+        protected abstract void Initialize();
+    }
+
+    public struct Point
+    {
+        public int X { get; set; }
+        public int Y { get; set; }
+    }";

            // Act
            var result = await _parser.ParseDiffAsync(diffContent);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Changes);
            
            var change = result.Changes.First();
            Assert.Contains("IRepository", change.ChangedTypes);
            Assert.Contains("BaseService", change.ChangedTypes);
            Assert.Contains("Point", change.ChangedTypes);
            Assert.Contains("GetByIdAsync", change.ChangedMethods);
            Assert.Contains("Initialize", change.ChangedMethods);
        }
    }
}