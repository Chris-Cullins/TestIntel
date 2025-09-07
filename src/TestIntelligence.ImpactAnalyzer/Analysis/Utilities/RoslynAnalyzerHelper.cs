using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TestIntelligence.Core.Models;

namespace TestIntelligence.ImpactAnalyzer.Analysis.Utilities
{
    public static class RoslynAnalyzerHelper
    {
        public static ImmutableArray<MetadataReference> GetBasicReferences()
        {
            var references = new List<MetadataReference>();
            
            var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (runtimePath != null)
            {
                references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Runtime.dll")));
                references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Collections.dll")));
                references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Linq.dll")));
                references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Threading.Tasks.dll")));
                references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            }

            return ImmutableArray.CreateRange(references);
        }

        public static string GetMethodIdentifier(IMethodSymbol methodSymbol)
        {
            return $"{methodSymbol.ContainingType.ToDisplayString()}.{methodSymbol.Name}({string.Join(",", methodSymbol.Parameters.Select(p => p.Type.ToDisplayString()))})";
        }

        public static bool IsTestMethod(IMethodSymbol methodSymbol, MethodDeclarationSyntax methodSyntax)
        {
            // Primary check: Look for test framework attributes (most accurate)
            var testAttributes = new[] { "Test", "TestMethod", "Fact", "Theory", "TestCase", "DataTestMethod" };
            
            foreach (var attributeList in methodSyntax.AttributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    var attributeName = attribute.Name.ToString();
                    if (testAttributes.Any(ta => attributeName.EndsWith(ta, StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
            }

            // Secondary check: Only for methods in explicitly recognized test projects
            // This prevents false positives in production code
            var filePath = methodSyntax.SyntaxTree.FilePath ?? string.Empty;
            if (IsInTestProject(filePath))
            {
                var methodName = methodSymbol.Name;
                
                // More restrictive name patterns for fallback cases
                // Only match methods that are clearly test methods by naming convention
                if (methodName.StartsWith("Test", StringComparison.OrdinalIgnoreCase) ||
                    methodName.StartsWith("Should", StringComparison.OrdinalIgnoreCase) ||
                    methodName.StartsWith("When", StringComparison.OrdinalIgnoreCase) ||
                    methodName.StartsWith("Given", StringComparison.OrdinalIgnoreCase))
                {
                    // Additional safety: method should be public to be a test
                    return methodSymbol.DeclaredAccessibility == Accessibility.Public;
                }
            }

            return false;
        }
        
        public static bool IsInTestProject(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;
                
            // Check if the file path contains test indicators
            var pathLower = filePath.ToLowerInvariant();
            return pathLower.Contains("/test") || pathLower.Contains("\\test") ||
                   pathLower.Contains("/tests") || pathLower.Contains("\\tests") ||
                   pathLower.Contains(".test.") || pathLower.Contains(".tests.");
        }

        public static TypeUsageContext GetTypeUsageContext(BaseTypeDeclarationSyntax typeDecl)
        {
            switch (typeDecl)
            {
                case ClassDeclarationSyntax _:
                case InterfaceDeclarationSyntax _:
                case StructDeclarationSyntax _:
                case EnumDeclarationSyntax _:
                    return TypeUsageContext.Declaration;
                default:
                    return TypeUsageContext.Reference;
            }
        }

        public static bool IsWriteAccess(MemberAccessExpressionSyntax memberAccess)
        {
            var parent = memberAccess.Parent;

            // Check if it's the left side of an assignment
            if (parent is AssignmentExpressionSyntax assignment && assignment.Left == memberAccess)
                return true;

            // Check if it's used with ++ or -- operators
            if (parent is PrefixUnaryExpressionSyntax prefixUnary && 
                (prefixUnary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PreIncrementExpression) || 
                 prefixUnary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PreDecrementExpression)))
                return true;

            if (parent is PostfixUnaryExpressionSyntax postfixUnary && 
                (postfixUnary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PostIncrementExpression) || 
                 postfixUnary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PostDecrementExpression)))
                return true;

            return false;
        }

        public static string[] GetSourceFilesFromSolution(string solutionPath)
        {
            try
            {
                var solutionDir = Path.GetDirectoryName(solutionPath) ?? Path.GetDirectoryName(Path.GetFullPath(solutionPath));
                if (string.IsNullOrEmpty(solutionDir))
                {
                    return Array.Empty<string>();
                }

                var sourceFiles = Directory.GetFiles(solutionDir, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("/bin/") && !f.Contains("\\bin\\") && 
                                !f.Contains("/obj/") && !f.Contains("\\obj\\")) // Skip build artifacts
                    .ToArray();

                return sourceFiles;
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        public static string? FindSolutionFile(string[] files)
        {
            foreach (var file in files)
            {
                if (file.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }

            // Look for solution files in the parent directories of the provided files
            foreach (var file in files)
            {
                var directory = Path.GetDirectoryName(file);
                while (!string.IsNullOrEmpty(directory))
                {
                    var solutionFiles = Directory.GetFiles(directory, "*.sln");
                    if (solutionFiles.Length > 0)
                    {
                        return solutionFiles[0];
                    }
                    directory = Path.GetDirectoryName(directory);
                }
            }

            return null;
        }
    }
}