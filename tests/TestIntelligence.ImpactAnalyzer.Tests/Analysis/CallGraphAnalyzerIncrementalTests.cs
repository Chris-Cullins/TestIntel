using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.ImpactAnalyzer.Analysis.CallGraph;
using TestIntelligence.ImpactAnalyzer.Analysis.Workspace;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Analysis
{
    public class CallGraphAnalyzerIncrementalTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _solutionPath;
        private readonly string _projectPath;
        private readonly string _codeFilePath;

        public CallGraphAnalyzerIncrementalTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TestIntelligence", nameof(CallGraphAnalyzerIncrementalTests), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);

            var projectDir = Path.Combine(_tempDir, "TestProject");
            Directory.CreateDirectory(projectDir);

            _solutionPath = Path.Combine(_tempDir, "TestSolution.sln");
            _projectPath = Path.Combine(projectDir, "TestProject.csproj");
            _codeFilePath = Path.Combine(projectDir, "Foo.cs");

            // Minimal solution file referencing our project
            File.WriteAllText(_solutionPath, $@"
Microsoft Visual Studio Solution File, Format Version 12.00
Project(""{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}"") = ""TestProject"", ""TestProject\\TestProject.csproj"", ""{{12345678-1234-1234-1234-123456789012}}""
EndProject");

            // Minimal SDK-style project
            File.WriteAllText(_projectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>preview</LangVersion>
  </PropertyGroup>
</Project>");

            // Source code with constructor, overload and generic method calling another method
            File.WriteAllText(_codeFilePath, @"
using System;
namespace TestProject
{
    public class Foo
    {
        public Foo() {}
        public int Bar(int x) => x + 1;
        public T Baz<T>(T item) { var _ = Bar(1); return item; }
    }
}
");
        }

        // Keep tests sandbox-friendly: avoid solution/workspace initialization paths
        // Validate the fast scanner via reflection on a single .cs file

        [Fact]
        public void GetMethodIdsFromFile_FastScanner_ReturnsNonEmpty()
        {
            var loggerFactory = Substitute.For<ILoggerFactory>();
            loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

            var logger = Substitute.For<ILogger<CallGraphAnalyzer>>();
            var workspaceLogger = Substitute.For<ILogger<WorkspaceManager>>();

            using var workspace = new WorkspaceManager(workspaceLogger, loggerFactory);
            var analyzer = new CallGraphAnalyzer(logger, loggerFactory, workspace);

            var method = typeof(CallGraphAnalyzer)
                .GetMethod("GetMethodIdsFromFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.NotNull(method);
            var result = method!.Invoke(analyzer, new object[] { _codeFilePath }) as System.Collections.Generic.IEnumerable<string>;
            Assert.NotNull(result);

            var list = result!.ToList();
            Assert.NotEmpty(list);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch
            {
                // ignore cleanup errors
            }
        }
    }
}
