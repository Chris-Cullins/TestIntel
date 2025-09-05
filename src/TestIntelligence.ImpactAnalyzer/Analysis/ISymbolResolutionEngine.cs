using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public interface ISymbolResolutionEngine
    {
        IMethodSymbol? ResolveMethodSymbol(InvocationExpressionSyntax invocation, string filePath);
        string GetFullyQualifiedMethodName(IMethodSymbol methodSymbol);
        IMethodSymbol? ResolveMemberAccess(MemberAccessExpressionSyntax memberAccess, string filePath);
        IMethodSymbol? HandleGenericMethods(IMethodSymbol method, InvocationExpressionSyntax invocation, string filePath);
    }
}