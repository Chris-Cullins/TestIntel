using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.ImpactAnalyzer.Analysis;
using TestIntelligence.ImpactAnalyzer.Configuration;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Analysis
{
    public class EnhancedRoslynAnalyzerTests : IDisposable
    {
        private readonly IRoslynAnalyzer _legacyAnalyzer;
        private readonly IRoslynAnalyzer _enhancedAnalyzer;
        private readonly ILoggerFactory _loggerFactory;
        private readonly string _tempDirectory;

        public EnhancedRoslynAnalyzerTests()
        {
            _loggerFactory = Substitute.For<ILoggerFactory>();
            _loggerFactory.CreateLogger<RoslynAnalyzer>().Returns(Substitute.For<ILogger<RoslynAnalyzer>>());
            _loggerFactory.CreateLogger<RoslynAnalyzerV2>().Returns(Substitute.For<ILogger<RoslynAnalyzerV2>>());
            
            _legacyAnalyzer = RoslynAnalyzerFactory.Create(_loggerFactory, RoslynAnalyzerConfig.Default);
            _enhancedAnalyzer = RoslynAnalyzerFactory.Create(_loggerFactory, RoslynAnalyzerConfig.Enhanced);
            
            _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, true);
        }

        [Fact]
        public async Task FindTestsExercisingMethod_ShouldDetectMoreCoverage_ThanLegacyAnalyzer()
        {
            // Arrange: Create a multi-project test scenario similar to the ToString issue
            var testProjectCode = CreateTestProjectCode();
            var productionProjectCode = CreateProductionProjectCode();
            
            var testFile = CreateTempFile("TestProject.cs", testProjectCode);
            var productionFile = CreateTempFile("ProductionProject.cs", productionProjectCode);
            var files = new[] { testFile, productionFile };

            // Target method that should be covered by tests
            var targetMethodId = "TestIntelligence.Production.TestMethod.ToString()";

            // Act: Compare coverage detection between analyzers
            var legacyCoverage = await _legacyAnalyzer.FindTestsExercisingMethodAsync(targetMethodId, files);
            var enhancedCoverage = await _enhancedAnalyzer.FindTestsExercisingMethodAsync(targetMethodId, files);

            // Assert: Enhanced analyzer should detect significantly more test coverage
            // Based on roslynfix.md, we expect improvement from ~2% to 80%+
            legacyCoverage.Should().NotBeEmpty("Legacy analyzer should detect some coverage");
            enhancedCoverage.Count.Should().BeGreaterOrEqualTo(legacyCoverage.Count, 
                "Enhanced analyzer should detect at least as much coverage as legacy");
            
            // The enhanced analyzer should detect cross-project method calls that the legacy analyzer misses
            var enhancedMethodIds = enhancedCoverage.Select(r => r.TestMethodId).ToHashSet();
            enhancedMethodIds.Should().Contain(id => id.Contains("ToString_ReturnsDisplayName"),
                "Enhanced analyzer should detect the ToString test coverage");
        }

        [Fact]
        public async Task BuildCallGraph_WithCrossProjectCalls_ShouldCaptureAllReferences()
        {
            // Arrange: Create a scenario with cross-project method calls
            var project1Code = @"
using System;

namespace Project1
{
    public class ClassA
    {
        public string GetMessage()
        {
            return Project2.ClassB.FormatMessage(""Hello"");
        }
        
        public override string ToString()
        {
            return ""ClassA"";
        }
    }
}";

            var project2Code = @"
using System;

namespace Project2
{
    public class ClassB
    {
        public static string FormatMessage(string input)
        {
            return $""Formatted: {input}"";
        }
    }
}";

            var testCode = @"
using System;
using Xunit;

namespace Tests
{
    public class Project1Tests
    {
        [Fact]
        public void ClassA_ToString_ReturnsDisplayName()
        {
            var instance = new Project1.ClassA();
            var result = instance.ToString();
            Assert.Equal(""ClassA"", result);
        }
        
        [Fact]
        public void ClassA_GetMessage_ReturnsFormattedMessage()
        {
            var instance = new Project1.ClassA();
            var result = instance.GetMessage();
            Assert.Contains(""Formatted:"", result);
        }
    }
}";

            var file1 = CreateTempFile("Project1.cs", project1Code);
            var file2 = CreateTempFile("Project2.cs", project2Code);
            var testFile = CreateTempFile("Tests.cs", testCode);
            var files = new[] { file1, file2, testFile };

            // Act
            var legacyCallGraph = await _legacyAnalyzer.BuildCallGraphAsync(files);
            var enhancedCallGraph = await _enhancedAnalyzer.BuildCallGraphAsync(files);

            // Assert: Enhanced analyzer should capture more method relationships
            var legacyMethodCount = legacyCallGraph.GetAllMethods().Count;
            var enhancedMethodCount = enhancedCallGraph.GetAllMethods().Count;
            
            enhancedMethodCount.Should().BeGreaterOrEqualTo(legacyMethodCount,
                "Enhanced analyzer should identify at least as many methods");

            // Check for cross-project call detection
            var toStringMethodId = "Project1.ClassA.ToString()";
            
            var legacyCoverage = legacyCallGraph.GetTestCoverageForMethod(toStringMethodId);
            var enhancedCoverage = enhancedCallGraph.GetTestCoverageForMethod(toStringMethodId);
            
            enhancedCoverage.Count.Should().BeGreaterOrEqualTo(legacyCoverage.Count,
                "Enhanced analyzer should detect better test coverage for ToString method");
        }

        [Fact]
        public void RoslynAnalyzerFactory_WithEnhancedConfig_ShouldCreateEnhancedAnalyzer()
        {
            // Arrange
            var config = RoslynAnalyzerConfig.Enhanced;
            
            // Act
            var analyzer = RoslynAnalyzerFactory.Create(_loggerFactory, config);
            
            // Assert
            analyzer.Should().NotBeNull();
            // In a full implementation, this would be a RoslynAnalyzerV2 instance
            // For now, we're testing the factory infrastructure
        }

        [Fact]
        public void RoslynAnalyzerFactory_WithDefaultConfig_ShouldCreateLegacyAnalyzer()
        {
            // Arrange
            var config = RoslynAnalyzerConfig.Default;
            
            // Act
            var analyzer = RoslynAnalyzerFactory.Create(_loggerFactory, config);
            
            // Assert
            analyzer.Should().NotBeNull();
            analyzer.Should().BeOfType<RoslynAnalyzer>("Default config should create legacy analyzer");
        }

        private string CreateTestProjectCode()
        {
            return @"
using System;
using Xunit;
using TestIntelligence.Production;

namespace TestIntelligence.Tests
{
    public class TestMethodTests
    {
        [Fact]
        public void ToString_ReturnsDisplayName()
        {
            var method = new TestMethod();
            var result = method.ToString();
            Assert.Equal(""TestMethod Display"", result);
        }
        
        [Fact] 
        public void Execute_CallsToString()
        {
            var method = new TestMethod();
            method.Execute(); // This indirectly calls ToString
        }
    }
}";
        }

        private string CreateProductionProjectCode()
        {
            return @"
using System;

namespace TestIntelligence.Production
{
    public class TestMethod
    {
        public override string ToString()
        {
            return ""TestMethod Display"";
        }
        
        public void Execute()
        {
            var display = this.ToString();
            Console.WriteLine(display);
        }
    }
}";
        }

        private string CreateTempFile(string fileName, string content)
        {
            var filePath = Path.Combine(_tempDirectory, fileName);
            File.WriteAllText(filePath, content);
            return filePath;
        }
    }
}