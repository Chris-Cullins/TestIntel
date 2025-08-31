using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.ImpactAnalyzer.Analysis;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Analysis
{
    public class SymbolResolutionEngineTests : IDisposable
    {
        private readonly ILogger<SymbolResolutionEngine> _logger;
        private readonly CompilationManager _compilationManager;
        private readonly SymbolResolutionEngine _engine;
        private readonly string _tempDirectory;

        public SymbolResolutionEngineTests()
        {
            _logger = Substitute.For<ILogger<SymbolResolutionEngine>>();
            _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            
            var workspace = CreateMockWorkspace();
            _compilationManager = Substitute.For<CompilationManager>(Substitute.For<ILogger<CompilationManager>>(), workspace);
            _engine = new SymbolResolutionEngine(_compilationManager, _logger);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, true);
        }

        [Fact]
        public void Constructor_WithNullCompilationManager_ShouldThrowArgumentNullException()
        {
            var action = () => new SymbolResolutionEngine(null!, _logger);

            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            var action = () => new SymbolResolutionEngine(_compilationManager, null!);

            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void ResolveMethodSymbol_WithNoSemanticModel_ShouldReturnNull()
        {
            var sourceCode = @"
using System;
namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod()
        {
            Console.WriteLine(""Hello"");
        }
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = syntaxTree.GetRoot();
            var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().First();

            _compilationManager.GetSemanticModel(Arg.Any<string>()).Returns((SemanticModel?)null);

            var result = _engine.ResolveMethodSymbol(invocation, "test.cs");

            result.Should().BeNull();
        }


        [Fact]
        public void GetFullyQualifiedMethodName_WithNullMethodSymbol_ShouldThrowArgumentNullException()
        {
            var action = () => _engine.GetFullyQualifiedMethodName(null!);

            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void GetFullyQualifiedMethodName_WithStandaloneMethodSymbol_ShouldReturnFullName()
        {
            var sourceCode = @"
using System;
namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod(string parameter)
        {
        }
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree });
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            
            var root = syntaxTree.GetRoot();
            var methodDeclaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;

            var result = _engine.GetFullyQualifiedMethodName(methodSymbol!);

            result.Should().Be("TestNamespace.TestClass.TestMethod(string)");
        }

        [Fact]
        public void ResolveMemberAccess_WithNoSemanticModel_ShouldReturnNull()
        {
            var sourceCode = @"
using System;
namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod()
        {
            var text = ""hello"";
            text.ToString();
        }
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = syntaxTree.GetRoot();
            var memberAccess = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().First();

            _compilationManager.GetSemanticModel(Arg.Any<string>()).Returns((SemanticModel?)null);

            var result = _engine.ResolveMemberAccess(memberAccess, "test.cs");

            result.Should().BeNull();
        }

        [Fact]
        public void HandleGenericMethods_WithNonGenericMethod_ShouldReturnSameMethod()
        {
            var sourceCode = @"
using System;
namespace TestNamespace
{
    public class TestClass
    {
        public void NonGenericMethod() { }
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree });
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            
            var root = syntaxTree.GetRoot();
            var methodDeclaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;

            var invocation = SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("NonGenericMethod"));

            var result = _engine.HandleGenericMethods(methodSymbol!, invocation, "test.cs");

            result.Should().BeSameAs(methodSymbol);
        }

        [Fact]
        public void HandleGenericMethods_WithNullMethod_ShouldReturnNull()
        {
            var invocation = SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("Test"));

            var result = _engine.HandleGenericMethods(null!, invocation, "test.cs");

            result.Should().BeNull();
        }

        [Fact]
        public void ResolveInterfaceImplementations_WithMockedManager_ShouldReturnEmpty()
        {
            var sourceCode = @"
using System;
namespace TestNamespace
{
    public interface ITestInterface
    {
        void InterfaceMethod();
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree });
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            
            var root = syntaxTree.GetRoot();
            var interfaceDeclaration = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().First();
            var interfaceMethod = interfaceDeclaration.Members.OfType<MethodDeclarationSyntax>().First();
            var interfaceMethodSymbol = semanticModel.GetDeclaredSymbol(interfaceMethod) as IMethodSymbol;

            _compilationManager.GetAllProjects().Returns(new List<Project>());

            var result = _engine.ResolveInterfaceImplementations(interfaceMethodSymbol!);

            result.Should().BeEmpty();
        }

        [Fact]
        public void ResolveInterfaceImplementations_WithNonInterfaceMethod_ShouldReturnEmpty()
        {
            var sourceCode = @"
using System;
namespace TestNamespace
{
    public class TestClass
    {
        public void RegularMethod() { }
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree });
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            
            var root = syntaxTree.GetRoot();
            var methodDeclaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;

            var result = _engine.ResolveInterfaceImplementations(methodSymbol!);

            result.Should().BeEmpty();
        }

        [Fact]
        public void ResolveVirtualOverrides_WithMockedManager_ShouldReturnEmpty()
        {
            var sourceCode = @"
using System;
namespace TestNamespace
{
    public class BaseClass
    {
        public virtual void VirtualMethod() { }
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree });
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            
            var root = syntaxTree.GetRoot();
            var baseClass = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
            var virtualMethod = baseClass.Members.OfType<MethodDeclarationSyntax>().First();
            var virtualMethodSymbol = semanticModel.GetDeclaredSymbol(virtualMethod) as IMethodSymbol;

            _compilationManager.GetAllProjects().Returns(new List<Project>());

            var result = _engine.ResolveVirtualOverrides(virtualMethodSymbol!);

            result.Should().BeEmpty();
        }

        [Fact]
        public void ResolveVirtualOverrides_WithNonVirtualMethod_ShouldReturnEmpty()
        {
            var sourceCode = @"
using System;
namespace TestNamespace
{
    public class TestClass
    {
        public void RegularMethod() { }
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree });
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            
            var root = syntaxTree.GetRoot();
            var methodDeclaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;

            var result = _engine.ResolveVirtualOverrides(methodSymbol!);

            result.Should().BeEmpty();
        }

        private string CreateTempFile(string fileName, string content)
        {
            var filePath = Path.Combine(_tempDirectory, fileName);
            File.WriteAllText(filePath, content);
            return filePath;
        }

        private SolutionWorkspace CreateMockWorkspace()
        {
            var workspace = Substitute.For<MSBuildWorkspace>();
            var solution = Substitute.For<Solution>();
            var projects = new List<Project>();
            solution.Projects.Returns(projects);

            return new SolutionWorkspace(
                workspace,
                solution,
                new Dictionary<string, ProjectId>(),
                new Dictionary<ProjectId, Compilation>()
            );
        }

    }
}