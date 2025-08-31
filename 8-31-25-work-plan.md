# Implementation Plan: Method-to-Test Reverse Lookup Feature

## Overview
Add functionality to TestIntelligence library that allows users to specify a method and discover all tests that exercise (directly or indirectly call) that method.

## Current State Analysis
The library currently has:
- ✅ **RoslynAnalyzer**: Builds call graphs and analyzes method dependencies
- ✅ **MethodCallGraph**: Tracks method calls and can find dependents via `GetMethodDependents()` and `GetTransitiveDependents()`
- ✅ **Test Discovery**: NUnitTestDiscovery for finding test methods
- ✅ **Impact Analysis**: Forward analysis from changes to potentially impacted tests

**Missing**: Reverse lookup from production method to tests that exercise it.

## Implementation Plan

### Phase 1: Core Test-to-Method Mapping Service
**Files to Create:**
- `src/TestIntelligence.Core/Services/TestCoverageAnalyzer.cs`
- `src/TestIntelligence.Core/Models/TestCoverageInfo.cs`
- `src/TestIntelligence.Core/Interfaces/ITestCoverageAnalyzer.cs`

**Functionality:**
```csharp
public interface ITestCoverageAnalyzer
{
    Task<IReadOnlyList<TestCoverageInfo>> FindTestsExercisingMethodAsync(
        string methodId, 
        string solutionPath, 
        CancellationToken cancellationToken = default);
        
    Task<TestCoverageMap> BuildTestCoverageMapAsync(
        string solutionPath, 
        CancellationToken cancellationToken = default);
}
```

### Phase 2: Enhanced Call Graph Integration
**Files to Modify:**
- `src/TestIntelligence.ImpactAnalyzer/Analysis/RoslynAnalyzer.cs`

**Enhancements:**
1. Extend `MethodCallGraph` to distinguish between test methods and production methods
2. Add method to trace from production method through call graph to find all test methods that can reach it
3. Support both direct calls and transitive dependencies

### Phase 3: Test Method Classification
**Files to Create:**
- `src/TestIntelligence.Core/Classification/TestMethodClassifier.cs`

**Functionality:**
- Identify test methods using common patterns:
  - `[Test]`, `[TestMethod]`, `[Fact]`, `[Theory]` attributes
  - Method names ending with "Test" or "Tests"
  - Classes in test assemblies/projects
- Distinguish between unit tests, integration tests, and other test types

### Phase 4: CLI Integration
**Files to Modify:**
- `src/TestIntelligence.CLI/Program.cs`
- `src/TestIntelligence.CLI/Services/AnalysisService.cs`

**New Command:**
```bash
testintel find-tests --method "MyNamespace.MyClass.MyMethod" --solution "MySolution.sln"
```

### Phase 5: API Integration
**Files to Create:**
- `src/TestIntelligence.API/Controllers/TestCoverageController.cs`

**New Endpoints:**
- `GET /api/test-coverage/method/{methodId}`
- `POST /api/test-coverage/bulk` (for multiple methods)

### Phase 6: Performance Optimizations
**Features:**
1. **Caching**: Cache test-to-method mappings to avoid recomputing
2. **Incremental Updates**: Update mappings when files change instead of full rebuild
3. **Parallel Processing**: Analyze multiple test assemblies concurrently

## Technical Implementation Details

### Core Algorithm
1. **Build Complete Call Graph**: Use existing `RoslynAnalyzer.BuildCallGraphAsync()` for entire solution
2. **Identify Test Methods**: Scan all test assemblies to find test methods using attributes/naming patterns
3. **Reverse Traversal**: For target method, traverse call graph backward to find all paths that lead from test methods
4. **Confidence Scoring**: Score results based on:
   - Direct vs indirect calls
   - Number of call chain hops
   - Test type (unit vs integration)

### Data Structures
```csharp
public class TestCoverageInfo
{
    public string TestMethodId { get; set; }
    public string TestMethodName { get; set; }
    public string TestClassName { get; set; }
    public string TestAssembly { get; set; }
    public string[] CallPath { get; set; } // Chain from test to target method
    public double Confidence { get; set; } // 0.0 - 1.0
    public TestType TestType { get; set; } // Unit, Integration, End2End
}

public class TestCoverageMap
{
    public Dictionary<string, List<TestCoverageInfo>> MethodToTests { get; set; }
    public DateTime BuildTimestamp { get; set; }
    public string SolutionPath { get; set; }
}
```

## Validation & Testing

### Test Cases to Create
1. **Direct Test Coverage**: Method directly called by test
2. **Indirect Test Coverage**: Method called through multiple layers
3. **Multiple Test Coverage**: Method called by multiple different tests
4. **No Coverage**: Method not called by any tests
5. **Complex Inheritance**: Method overridden in derived classes
6. **Generic Methods**: Generic method instantiations

### Integration Points
- Verify compatibility with existing NUnit test discovery
- Ensure call graph analysis works with existing impact analysis
- Test performance with large codebases (1000+ test methods)

## Success Criteria
1. ✅ Given a method identifier, return all tests that exercise it
2. ✅ Support both direct and transitive (indirect) test coverage
3. ✅ Provide confidence scores for coverage relationships
4. ✅ Handle complex scenarios (inheritance, generics, async methods)
5. ✅ Performance: Process 1000+ methods in <30 seconds
6. ✅ Integration: Works with existing CLI and API interfaces

## Future Enhancements (Out of Scope)
- Visual call graph representation
- Test gap analysis (methods with no test coverage)
- Test redundancy detection (multiple tests exercising same path)
- IDE plugin integration
- Real-time coverage updates during development

## Estimated Timeline
- **Phase 1-3**: 2-3 days (Core implementation)
- **Phase 4-5**: 1-2 days (CLI/API integration)  
- **Phase 6**: 1-2 days (Performance optimization)
- **Testing**: 1-2 days (Comprehensive test coverage)

**Total**: 5-9 days for complete implementation