# RoslynAnalyzer Rewrite Implementation Plan

## Problem Summary

The current `RoslynAnalyzer` creates individual file compilations without proper project dependencies, resulting in:
- Only ~2% test coverage detection (15/715 methods)
- Failed cross-project symbol resolution
- Missing method calls between projects
- Inability to analyze multi-project solutions effectively

## Root Cause

The `GetOrCreateCompilationAsync` method creates isolated compilations per file with only basic .NET references, preventing proper semantic analysis across the solution.

## Implementation Plan

### Phase 1: Solution Structure Analysis

**Goal**: Build complete understanding of solution structure and dependencies

#### 1.1 Solution Parser
- **File**: `Analysis/SolutionParser.cs`
- **Purpose**: Parse `.sln` files to discover projects and dependencies
- **Key Methods**:
  - `ParseSolutionAsync(string solutionPath)`
  - `GetProjectPaths()`
  - `GetProjectDependencies()`

#### 1.2 Project Parser
- **File**: `Analysis/ProjectParser.cs`
- **Purpose**: Parse `.csproj` files for references and source files
- **Key Methods**:
  - `ParseProjectAsync(string projectPath)`
  - `GetSourceFiles()`
  - `GetProjectReferences()`
  - `GetPackageReferences()`
  - `GetAssemblyReferences()`

#### 1.3 Dependency Graph Builder
- **File**: `Analysis/DependencyGraphBuilder.cs`
- **Purpose**: Build topological ordering of projects for compilation
- **Key Methods**:
  - `BuildDependencyGraph(IEnumerable<ProjectInfo>)`
  - `GetCompilationOrder()`
  - `DetectCircularDependencies()`

### Phase 2: Workspace Construction

**Goal**: Create proper Roslyn workspace with full solution context

#### 2.1 Solution Workspace Builder
- **File**: `Analysis/SolutionWorkspaceBuilder.cs`
- **Purpose**: Build complete MSBuild workspace
- **Key Methods**:
  - `CreateWorkspaceAsync(string solutionPath)`
  - `LoadProjectsAsync()`
  - `ResolveMetadataReferences()`
  - `ApplyCompilationOptions()`

#### 2.2 Compilation Manager
- **File**: `Analysis/CompilationManager.cs`
- **Purpose**: Manage project compilations with dependencies
- **Key Methods**:
  - `BuildSolutionCompilationsAsync()`
  - `GetCompilationForProject(string projectPath)`
  - `GetSemanticModel(string filePath)`
  - `ResolveSymbolInfo(SyntaxNode node)`

### Phase 3: Enhanced Call Graph Analysis

**Goal**: Rebuild call graph with proper cross-project symbol resolution

#### 3.1 Enhanced Method Call Visitor
- **File**: `Analysis/EnhancedMethodCallVisitor.cs`
- **Purpose**: Analyze method calls with full semantic context
- **Key Features**:
  - Cross-project method call detection
  - Generic method handling
  - Extension method resolution
  - Interface/abstract method mapping
  - Implicit method calls (operators, properties)

#### 3.2 Symbol Resolution Engine
- **File**: `Analysis/SymbolResolutionEngine.cs`
- **Purpose**: Resolve method symbols across projects
- **Key Methods**:
  - `ResolveMethodSymbol(InvocationExpressionSyntax invocation)`
  - `GetFullyQualifiedMethodName(IMethodSymbol method)`
  - `ResolveMemberAccess(MemberAccessExpressionSyntax access)`
  - `HandleGenericMethods(IMethodSymbol method)`

### Phase 4: Improved RoslynAnalyzer

**Goal**: Replace existing analyzer with solution-aware implementation

#### 4.1 New RoslynAnalyzer Core
- **File**: `Analysis/RoslynAnalyzer.cs` (replace existing)
- **Key Changes**:
  - Remove per-file compilation approach
  - Use solution workspace for all analysis
  - Implement proper caching strategies
  - Add progress reporting for large solutions

#### 4.2 Call Graph Builder v2
- **File**: `Analysis/CallGraphBuilderV2.cs`
- **Purpose**: Build comprehensive call graphs
- **Key Features**:
  - Multi-project call tracking
  - Transitive dependency analysis
  - Performance optimizations
  - Memory-efficient storage

### Phase 5: Integration and Testing

**Goal**: Ensure new implementation works with existing TestIntelligence components

#### 5.1 Interface Updates
- Update `IRoslynAnalyzer` interface if needed
- Ensure backward compatibility with existing services
- Add new configuration options

#### 5.2 Performance Optimizations
- **Caching Strategy**: Cache compilations and semantic models
- **Parallel Processing**: Analyze projects in parallel where possible
- **Memory Management**: Dispose of large objects properly
- **Incremental Analysis**: Only reanalyze changed files

#### 5.3 Comprehensive Testing
- Unit tests for each new component
- Integration tests with real solutions
- Performance benchmarks
- Regression tests for existing functionality

## Implementation Details

### MSBuild Integration

```csharp
// Use MSBuildWorkspace for proper solution loading
using Microsoft.CodeAnalysis.MSBuild;

var workspace = MSBuildWorkspace.Create();
var solution = await workspace.OpenSolutionAsync(solutionPath);
```

### Semantic Model Usage

```csharp
// Proper cross-project symbol resolution
var compilation = await project.GetCompilationAsync();
var semanticModel = compilation.GetSemanticModel(syntaxTree);
var symbolInfo = semanticModel.GetSymbolInfo(invocationExpression);
```

### Method ID Generation

```csharp
// Consistent method identification across projects
private string GetMethodId(IMethodSymbol methodSymbol)
{
    var containingType = methodSymbol.ContainingType.ToDisplayString();
    var methodName = methodSymbol.Name;
    var parameters = string.Join(",", methodSymbol.Parameters.Select(p => p.Type.ToDisplayString()));
    return $"{containingType}.{methodName}({parameters})";
}
```

## Expected Outcomes

After implementation:
- **Coverage Detection**: Should detect 80%+ of actual test coverage
- **Cross-Project Calls**: All method calls between projects captured
- **Performance**: Initial analysis may be slower but with better caching
- **Accuracy**: Precise method identification and call path analysis
- **Scalability**: Support for large multi-project solutions

## Risk Mitigation

1. **Backward Compatibility**: Keep existing interface during transition
2. **Performance Monitoring**: Add metrics to track analysis performance
3. **Incremental Rollout**: Test with smaller solutions first
4. **Fallback Strategy**: Maintain ability to use old analyzer if needed
5. **Memory Management**: Monitor memory usage during large solution analysis

## Dependencies

- **Microsoft.CodeAnalysis.Workspaces.MSBuild**: For solution loading
- **Microsoft.Build**: For project file parsing
- **System.Collections.Immutable**: For efficient data structures
- **Microsoft.Extensions.Logging**: For detailed progress logging

## Estimated Timeline

- **Phase 1-2**: 2-3 weeks (Solution parsing and workspace construction)
- **Phase 3**: 2 weeks (Enhanced call graph analysis)
- **Phase 4**: 1 week (Integration with existing RoslynAnalyzer)
- **Phase 5**: 1-2 weeks (Testing and optimization)

**Total**: 6-8 weeks for complete implementation and testing

## Success Metrics

- Test coverage detection increases from ~2% to 80%+
- `TestMethod.ToString()` correctly identified with test coverage
- All cross-project method calls captured in call graph
- Performance acceptable for large solutions (< 30 seconds for TestIntelligence solution)
- No regression in existing functionality