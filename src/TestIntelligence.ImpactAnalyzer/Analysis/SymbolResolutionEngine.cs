using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public class SymbolResolutionEngine : ISymbolResolutionEngine
    {
        private readonly ICompilationManager _compilationManager;
        private readonly ILogger<SymbolResolutionEngine> _logger;

        public SymbolResolutionEngine(ICompilationManager compilationManager, ILogger<SymbolResolutionEngine> logger)
        {
            _compilationManager = compilationManager ?? throw new ArgumentNullException(nameof(compilationManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IMethodSymbol? ResolveMethodSymbol(InvocationExpressionSyntax invocation, string filePath)
        {
            try
            {
                var semanticModel = _compilationManager.GetSemanticModel(filePath);
                if (semanticModel == null)
                {
                    _logger.LogDebug("No semantic model found for file: {FilePath}", filePath);
                    return null;
                }

                var symbolInfo = semanticModel.GetSymbolInfo(invocation);
                var methodSymbol = symbolInfo.Symbol as IMethodSymbol;

                if (methodSymbol != null)
                {
                    return methodSymbol;
                }

                // Try candidate symbols if direct resolution failed
                var candidateSymbol = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
                if (candidateSymbol != null)
                {
                    _logger.LogTrace("Using candidate symbol for method resolution in {FilePath}", filePath);
                    return candidateSymbol;
                }

                // Handle extension methods specifically
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    var extensionMethod = ResolveExtensionMethod(memberAccess, semanticModel);
                    if (extensionMethod != null)
                        return extensionMethod;
                }

                _logger.LogTrace("Could not resolve method symbol for invocation in {FilePath} at line {Line}", 
                    filePath, invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Exception while resolving method symbol in {FilePath}", filePath);
                return null;
            }
        }

        public string GetFullyQualifiedMethodName(IMethodSymbol methodSymbol)
        {
            if (methodSymbol == null)
                throw new ArgumentNullException(nameof(methodSymbol));

            var sb = new StringBuilder();

            // Build the containing type name
            var containingType = GetFullyQualifiedTypeName(methodSymbol.ContainingType);
            sb.Append(containingType);
            sb.Append('.');

            // Add method name
            sb.Append(methodSymbol.Name);

            // Handle generic methods
            if (methodSymbol.IsGenericMethod)
            {
                sb.Append('<');
                var typeParameters = methodSymbol.TypeArguments.Select(t => GetFullyQualifiedTypeName(t));
                sb.Append(string.Join(",", typeParameters));
                sb.Append('>');
            }

            // Add parameters
            sb.Append('(');
            var parameters = methodSymbol.Parameters.Select(p => GetFullyQualifiedTypeName(p.Type));
            sb.Append(string.Join(",", parameters));
            sb.Append(')');

            return sb.ToString();
        }

        public IMethodSymbol? ResolveMemberAccess(MemberAccessExpressionSyntax memberAccess, string filePath)
        {
            try
            {
                var semanticModel = _compilationManager.GetSemanticModel(filePath);
                if (semanticModel == null)
                    return null;

                var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
                return symbolInfo.Symbol as IMethodSymbol ?? 
                       symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to resolve member access in {FilePath}", filePath);
                return null;
            }
        }

        public IMethodSymbol? HandleGenericMethods(IMethodSymbol method, InvocationExpressionSyntax invocation, string filePath)
        {
            if (method == null || !method.IsGenericMethod)
                return method;

            try
            {
                var semanticModel = _compilationManager.GetSemanticModel(filePath);
                if (semanticModel == null)
                    return method;

                // Try to get the constructed generic method
                var symbolInfo = semanticModel.GetSymbolInfo(invocation);
                var constructedMethod = symbolInfo.Symbol as IMethodSymbol;

                if (constructedMethod != null && constructedMethod.IsGenericMethod)
                {
                    return constructedMethod;
                }

                return method;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to handle generic method in {FilePath}", filePath);
                return method;
            }
        }

        public IReadOnlyList<IMethodSymbol> ResolveInterfaceImplementations(IMethodSymbol interfaceMethod)
        {
            var implementations = new List<IMethodSymbol>();

            if (interfaceMethod.ContainingType.TypeKind != TypeKind.Interface)
                return implementations;

            try
            {
                // Find all types that implement this interface
                var allProjects = _compilationManager.GetAllProjects();
                
                foreach (var project in allProjects)
                {
                    var compilation = _compilationManager.GetCompilationForProject(project.FilePath ?? "");
                    if (compilation == null)
                        continue;

                    // Look for implementations in this compilation
                    var interfaceType = interfaceMethod.ContainingType;
                    
                    foreach (var type in GetAllTypes(compilation))
                    {
                        if (ImplementsInterface(type, interfaceType))
                        {
                            var implementation = FindInterfaceImplementation(type, interfaceMethod);
                            if (implementation != null)
                            {
                                implementations.Add(implementation);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to resolve interface implementations for {MethodName}", interfaceMethod.Name);
            }

            return implementations;
        }

        public IReadOnlyList<IMethodSymbol> ResolveVirtualOverrides(IMethodSymbol virtualMethod)
        {
            var overrides = new List<IMethodSymbol>();

            if (!virtualMethod.IsVirtual && !virtualMethod.IsOverride && !virtualMethod.IsAbstract)
                return overrides;

            try
            {
                var allProjects = _compilationManager.GetAllProjects();
                
                foreach (var project in allProjects)
                {
                    var compilation = _compilationManager.GetCompilationForProject(project.FilePath ?? "");
                    if (compilation == null)
                        continue;

                    foreach (var type in GetAllTypes(compilation))
                    {
                        if (InheritsFrom(type, virtualMethod.ContainingType))
                        {
                            var override_ = FindVirtualOverride(type, virtualMethod);
                            if (override_ != null)
                            {
                                overrides.Add(override_);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to resolve virtual overrides for {MethodName}", virtualMethod.Name);
            }

            return overrides;
        }

        private IMethodSymbol? ResolveExtensionMethod(MemberAccessExpressionSyntax memberAccess, SemanticModel semanticModel)
        {
            try
            {
                // Get the type of the expression that the extension method is called on
                var expressionType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
                if (expressionType == null)
                    return null;

                var methodName = memberAccess.Name.Identifier.ValueText;

                // Look for extension methods in the current compilation
                var compilation = semanticModel.Compilation;
                foreach (var type in GetAllTypes(compilation))
                {
                    if (type.IsStatic)
                    {
                        foreach (var method in type.GetMembers(methodName).OfType<IMethodSymbol>())
                        {
                            if (method.IsExtensionMethod && method.Parameters.Length > 0)
                            {
                                var firstParamType = method.Parameters[0].Type;
                                if (CanConvert(expressionType, firstParamType, semanticModel.Compilation))
                                {
                                    return method;
                                }
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to resolve extension method");
                return null;
            }
        }

        private string GetFullyQualifiedTypeName(ITypeSymbol typeSymbol)
        {
            if (typeSymbol == null)
                return "object";

            // Handle special cases
            switch (typeSymbol.SpecialType)
            {
                case SpecialType.System_Void:
                    return "void";
                case SpecialType.System_Object:
                    return "object";
                case SpecialType.System_Boolean:
                    return "bool";
                case SpecialType.System_Char:
                    return "char";
                case SpecialType.System_SByte:
                    return "sbyte";
                case SpecialType.System_Byte:
                    return "byte";
                case SpecialType.System_Int16:
                    return "short";
                case SpecialType.System_UInt16:
                    return "ushort";
                case SpecialType.System_Int32:
                    return "int";
                case SpecialType.System_UInt32:
                    return "uint";
                case SpecialType.System_Int64:
                    return "long";
                case SpecialType.System_UInt64:
                    return "ulong";
                case SpecialType.System_Decimal:
                    return "decimal";
                case SpecialType.System_Single:
                    return "float";
                case SpecialType.System_Double:
                    return "double";
                case SpecialType.System_String:
                    return "string";
            }

            return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private IEnumerable<INamedTypeSymbol> GetAllTypes(Compilation compilation)
        {
            return GetAllTypesFromNamespace(compilation.GlobalNamespace);
        }

        private IEnumerable<INamedTypeSymbol> GetAllTypesFromNamespace(INamespaceSymbol @namespace)
        {
            foreach (var type in @namespace.GetTypeMembers())
            {
                yield return type;
                
                foreach (var nestedType in GetNestedTypes(type))
                {
                    yield return nestedType;
                }
            }

            foreach (var nestedNamespace in @namespace.GetNamespaceMembers())
            {
                foreach (var type in GetAllTypesFromNamespace(nestedNamespace))
                {
                    yield return type;
                }
            }
        }

        private IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type)
        {
            foreach (var nestedType in type.GetTypeMembers())
            {
                yield return nestedType;
                
                foreach (var doublyNested in GetNestedTypes(nestedType))
                {
                    yield return doublyNested;
                }
            }
        }

        private static bool ImplementsInterface(ITypeSymbol type, ITypeSymbol interfaceType)
        {
            return type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, interfaceType));
        }

        private static bool InheritsFrom(ITypeSymbol type, ITypeSymbol baseType)
        {
            var current = type.BaseType;
            while (current != null)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                    return true;
                current = current.BaseType;
            }
            return false;
        }

        private static IMethodSymbol? FindInterfaceImplementation(ITypeSymbol type, IMethodSymbol interfaceMethod)
        {
            return type.GetMembers(interfaceMethod.Name)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => SymbolEqualityComparer.Default.Equals(m, type.FindImplementationForInterfaceMember(interfaceMethod)));
        }

        private static IMethodSymbol? FindVirtualOverride(ITypeSymbol type, IMethodSymbol virtualMethod)
        {
            return type.GetMembers(virtualMethod.Name)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.IsOverride && SymbolEqualityComparer.Default.Equals(m.OverriddenMethod, virtualMethod));
        }

        private static bool CanConvert(ITypeSymbol from, ITypeSymbol to, Compilation compilation)
        {
            // Simplified conversion check - in reality, this would need to handle all C# conversion rules
            return SymbolEqualityComparer.Default.Equals(from, to) ||
                   compilation.HasImplicitConversion(from, to);
        }
    }
}