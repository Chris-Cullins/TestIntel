using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public class MethodCallInfo
    {
        public MethodCallInfo(string calledMethodId, string callExpression, int lineNumber, MethodCallType callType)
        {
            CalledMethodId = calledMethodId ?? throw new ArgumentNullException(nameof(calledMethodId));
            CallExpression = callExpression ?? throw new ArgumentNullException(nameof(callExpression));
            LineNumber = lineNumber;
            CallType = callType;
        }

        public string CalledMethodId { get; }
        public string CallExpression { get; }
        public int LineNumber { get; }
        public MethodCallType CallType { get; }

        public override string ToString()
        {
            return $"[{CallType}] {CalledMethodId} at line {LineNumber}";
        }
    }

    public enum MethodCallType
    {
        DirectCall,
        PropertyGetter,
        PropertySetter,
        Constructor,
        ImplicitConstructor,
        ExtensionMethod,
        InterfaceCall,
        VirtualCall,
        StaticCall,
        DelegateInvoke,
        OperatorCall
    }

    public class EnhancedMethodCallVisitor : CSharpSyntaxWalker
    {
        private readonly SemanticModel _semanticModel;
        private readonly ICompilationManager _compilationManager;
        private readonly ISymbolResolutionEngine _symbolResolver;
        private readonly ILogger<EnhancedMethodCallVisitor> _logger;
        private readonly List<MethodCallInfo> _methodCalls;
        private readonly string _filePath;

        public EnhancedMethodCallVisitor(SemanticModel semanticModel, ICompilationManager compilationManager, 
            ISymbolResolutionEngine symbolResolver, ILogger<EnhancedMethodCallVisitor> logger, string filePath)
        {
            _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
            _compilationManager = compilationManager ?? throw new ArgumentNullException(nameof(compilationManager));
            _symbolResolver = symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _methodCalls = new List<MethodCallInfo>();
        }

        public IReadOnlyList<MethodCallInfo> MethodCalls => _methodCalls;

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            try
            {
                var methodSymbol = _symbolResolver.ResolveMethodSymbol(node, _filePath);
                if (methodSymbol != null)
                {
                    var methodId = _symbolResolver.GetFullyQualifiedMethodName(methodSymbol);
                    var callType = DetermineCallType(node, methodSymbol);
                    var lineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var callExpression = node.ToString();

                    var callInfo = new MethodCallInfo(methodId, callExpression, lineNumber, callType);
                    _methodCalls.Add(callInfo);

                    _logger.LogTrace("Found method call: {MethodId} at line {LineNumber}", methodId, lineNumber);
                }
                else
                {
                    // Enhanced fallback: try direct semantic model resolution
                    var fallbackSymbol = TryFallbackSymbolResolution(node);
                    if (fallbackSymbol != null)
                    {
                        var methodId = _symbolResolver.GetFullyQualifiedMethodName(fallbackSymbol);
                        var callType = DetermineCallType(node, fallbackSymbol);
                        var lineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        var callExpression = node.ToString();

                        var callInfo = new MethodCallInfo(methodId, callExpression, lineNumber, callType);
                        _methodCalls.Add(callInfo);

                        _logger.LogTrace("Found method call via fallback: {MethodId} at line {LineNumber}", methodId, lineNumber);
                    }
                    else
                    {
                        var lineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        _logger.LogDebug("Could not resolve method call '{CallExpression}' at line {LineNumber} in {FilePath}", 
                            node.ToString(), lineNumber, _filePath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to resolve invocation expression at line {LineNumber}", 
                    node.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
            }

            base.VisitInvocationExpression(node);
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            try
            {
                var symbolInfo = _semanticModel.GetSymbolInfo(node);
                var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

                if (symbol is IPropertySymbol propertySymbol)
                {
                    HandlePropertyAccess(node, propertySymbol);
                }
                else if (symbol is IMethodSymbol methodSymbol && !IsPartOfInvocation(node))
                {
                    // Method group reference (e.g., for delegates)
                    var methodId = _symbolResolver.GetFullyQualifiedMethodName(methodSymbol);
                    var lineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var callExpression = node.ToString();

                    var callInfo = new MethodCallInfo(methodId, callExpression, lineNumber, MethodCallType.DelegateInvoke);
                    _methodCalls.Add(callInfo);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to resolve member access expression at line {LineNumber}", 
                    node.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
            }

            base.VisitMemberAccessExpression(node);
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            try
            {
                var symbolInfo = _semanticModel.GetSymbolInfo(node);
                var constructorSymbol = symbolInfo.Symbol as IMethodSymbol;

                if (constructorSymbol != null)
                {
                    var methodId = _symbolResolver.GetFullyQualifiedMethodName(constructorSymbol);
                    var lineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var callExpression = node.ToString();

                    var callInfo = new MethodCallInfo(methodId, callExpression, lineNumber, MethodCallType.Constructor);
                    _methodCalls.Add(callInfo);

                    _logger.LogTrace("Found constructor call: {MethodId} at line {LineNumber}", methodId, lineNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to resolve object creation expression at line {LineNumber}", 
                    node.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
            }

            base.VisitObjectCreationExpression(node);
        }

        public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
        {
            try
            {
                var symbolInfo = _semanticModel.GetSymbolInfo(node);
                var constructorSymbol = symbolInfo.Symbol as IMethodSymbol;

                if (constructorSymbol != null)
                {
                    var methodId = _symbolResolver.GetFullyQualifiedMethodName(constructorSymbol);
                    var lineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var callExpression = node.ToString();

                    var callInfo = new MethodCallInfo(methodId, callExpression, lineNumber, MethodCallType.ImplicitConstructor);
                    _methodCalls.Add(callInfo);

                    _logger.LogTrace("Found implicit constructor call: {MethodId} at line {LineNumber}", methodId, lineNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to resolve implicit object creation expression at line {LineNumber}", 
                    node.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
            }

            base.VisitImplicitObjectCreationExpression(node);
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            // Handle operator overloads
            try
            {
                var symbolInfo = _semanticModel.GetSymbolInfo(node);
                var operatorSymbol = symbolInfo.Symbol as IMethodSymbol;

                if (operatorSymbol != null && operatorSymbol.MethodKind == MethodKind.UserDefinedOperator)
                {
                    var methodId = _symbolResolver.GetFullyQualifiedMethodName(operatorSymbol);
                    var lineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var callExpression = node.ToString();

                    var callInfo = new MethodCallInfo(methodId, callExpression, lineNumber, MethodCallType.OperatorCall);
                    _methodCalls.Add(callInfo);

                    _logger.LogTrace("Found operator call: {MethodId} at line {LineNumber}", methodId, lineNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to resolve binary expression at line {LineNumber}", 
                    node.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
            }

            base.VisitBinaryExpression(node);
        }

        public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        {
            HandleUnaryOperator(node, node.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
            base.VisitPrefixUnaryExpression(node);
        }

        public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        {
            HandleUnaryOperator(node, node.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
            base.VisitPostfixUnaryExpression(node);
        }

        private void HandlePropertyAccess(MemberAccessExpressionSyntax node, IPropertySymbol propertySymbol)
        {
            var lineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var callExpression = node.ToString();

            // Determine if this is a read or write access
            if (IsWriteAccess(node))
            {
                // Property setter
                if (propertySymbol.SetMethod != null)
                {
                    var setterId = _symbolResolver.GetFullyQualifiedMethodName(propertySymbol.SetMethod);
                    var callInfo = new MethodCallInfo(setterId, callExpression, lineNumber, MethodCallType.PropertySetter);
                    _methodCalls.Add(callInfo);

                    _logger.LogTrace("Found property setter call: {MethodId} at line {LineNumber}", setterId, lineNumber);
                }
            }
            else
            {
                // Property getter
                if (propertySymbol.GetMethod != null)
                {
                    var getterId = _symbolResolver.GetFullyQualifiedMethodName(propertySymbol.GetMethod);
                    var callInfo = new MethodCallInfo(getterId, callExpression, lineNumber, MethodCallType.PropertyGetter);
                    _methodCalls.Add(callInfo);

                    _logger.LogTrace("Found property getter call: {MethodId} at line {LineNumber}", getterId, lineNumber);
                }
            }
        }

        private void HandleUnaryOperator(ExpressionSyntax node, int lineNumber)
        {
            try
            {
                var symbolInfo = _semanticModel.GetSymbolInfo(node);
                var operatorSymbol = symbolInfo.Symbol as IMethodSymbol;

                if (operatorSymbol != null && operatorSymbol.MethodKind == MethodKind.UserDefinedOperator)
                {
                    var methodId = _symbolResolver.GetFullyQualifiedMethodName(operatorSymbol);
                    var callExpression = node.ToString();

                    var callInfo = new MethodCallInfo(methodId, callExpression, lineNumber, MethodCallType.OperatorCall);
                    _methodCalls.Add(callInfo);

                    _logger.LogTrace("Found unary operator call: {MethodId} at line {LineNumber}", methodId, lineNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to resolve unary operator at line {LineNumber}", lineNumber);
            }
        }

        private MethodCallType DetermineCallType(InvocationExpressionSyntax invocation, IMethodSymbol methodSymbol)
        {
            if (methodSymbol.IsExtensionMethod)
                return MethodCallType.ExtensionMethod;

            if (methodSymbol.IsStatic)
                return MethodCallType.StaticCall;

            if (methodSymbol.IsVirtual || methodSymbol.IsOverride)
                return MethodCallType.VirtualCall;

            if (methodSymbol.ContainingType.TypeKind == TypeKind.Interface)
                return MethodCallType.InterfaceCall;

            return MethodCallType.DirectCall;
        }

        private static bool IsWriteAccess(MemberAccessExpressionSyntax memberAccess)
        {
            var parent = memberAccess.Parent;

            // Check if it's the left side of an assignment
            if (parent is AssignmentExpressionSyntax assignment && assignment.Left == memberAccess)
                return true;

            // Check if it's part of a compound assignment (+=, -=, etc.)
            if (parent is AssignmentExpressionSyntax compoundAssignment && compoundAssignment.Left == memberAccess)
                return true;

            // Check if it's used with ++ or -- operators
            if (parent is PrefixUnaryExpressionSyntax prefixUnary && 
                (prefixUnary.IsKind(SyntaxKind.PreIncrementExpression) || 
                 prefixUnary.IsKind(SyntaxKind.PreDecrementExpression)))
                return true;

            if (parent is PostfixUnaryExpressionSyntax postfixUnary && 
                (postfixUnary.IsKind(SyntaxKind.PostIncrementExpression) || 
                 postfixUnary.IsKind(SyntaxKind.PostDecrementExpression)))
                return true;

            return false;
        }

        private static bool IsPartOfInvocation(MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Parent is InvocationExpressionSyntax invocation && 
                   invocation.Expression == memberAccess;
        }

        private IMethodSymbol? TryFallbackSymbolResolution(InvocationExpressionSyntax invocation)
        {
            try
            {
                // Direct semantic model approach as fallback
                var symbolInfo = _semanticModel.GetSymbolInfo(invocation);
                
                // Try direct symbol first
                if (symbolInfo.Symbol is IMethodSymbol directMethod)
                {
                    return directMethod;
                }
                
                // Try all candidate symbols
                foreach (var candidate in symbolInfo.CandidateSymbols.OfType<IMethodSymbol>())
                {
                    return candidate; // Take the first viable candidate
                }
                
                // Try resolving through type information
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    var typeInfo = _semanticModel.GetTypeInfo(memberAccess.Expression);
                    if (typeInfo.Type != null)
                    {
                        var methodName = memberAccess.Name.Identifier.ValueText;
                        var methods = typeInfo.Type.GetMembers(methodName).OfType<IMethodSymbol>();
                        
                        // Try to find a matching method by parameter count
                        var argCount = invocation.ArgumentList?.Arguments.Count ?? 0;
                        foreach (var method in methods)
                        {
                            if (method.Parameters.Length == argCount || method.Parameters.Any(p => p.IsOptional))
                            {
                                return method;
                            }
                        }
                        
                        // If no exact match, return the first method with the same name
                        return methods.FirstOrDefault();
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Fallback symbol resolution failed for invocation at line {LineNumber}",
                    invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
                return null;
            }
        }
    }
}