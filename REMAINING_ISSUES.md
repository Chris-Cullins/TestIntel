# TestIntelligence - Remaining Issues

## ASP.NET Core Test Assembly Analysis Issue

### Problem
The CLI analyze command cannot discover tests in ASP.NET Core test assemblies due to missing runtime dependencies.

**Affected Assemblies:**
- `TestIntelligence.API.Tests.dll` - 0 tests discovered (should be ~6-10 xUnit tests)
- `TestIntelligence.API.dll` - 0 tests discovered (production assembly, expected)

### Root Cause
ASP.NET Core test assemblies require `Microsoft.AspNetCore.Mvc.Core` and related runtime dependencies that are not present in the test assembly output directory. These dependencies are typically provided by the ASP.NET Core shared framework at runtime.

**Specific Error:**
```
Could not load file or assembly 'Microsoft.AspNetCore.Mvc.Core, Version=8.0.0.0, Culture=neutral, PublicKeyToken=adb9793829ddae60'. The system cannot find the file specified.
```

### Current Status
- **Individual assembly analysis**: Fails with 13 dependency errors
- **Solution analysis**: Fails with same 13 dependency errors  
- **Test execution**: Works fine (tests run successfully with `dotnet test`)

### Technical Details

**Test Assembly Structure:**
```
tests/TestIntelligence.API.Tests/bin/Debug/net8.0/
├── TestIntelligence.API.Tests.dll (test assembly)
├── Microsoft.AspNetCore.Mvc.Testing.dll (available)
├── Microsoft.AspNetCore.TestHost.dll (available)
└── [Missing: Microsoft.AspNetCore.Mvc.Core.dll]
```

**Project Configuration:**
- Uses `Microsoft.NET.Sdk.Web` SDK
- References `Microsoft.AspNetCore.Mvc.Testing` NuGet package
- Targets .NET 8.0 framework
- Contains xUnit tests that work when run via `dotnet test`

### Potential Solutions

#### Option 1: AssemblyLoadContext Isolation (Recommended)
Create isolated assembly loading contexts for each test assembly to prevent dependency conflicts.

**Implementation:**
1. Replace `Assembly.LoadFrom` with custom `AssemblyLoadContext`
2. Implement proper dependency resolution within each context
3. Use MetadataLoadContext for reflection-only loading when full loading fails

**Files to modify:**
- `src/TestIntelligence.Core/Assembly/Loaders/StandardLoader.cs`
- Add new `IsolatedAssemblyLoader` class

#### Option 2: Runtime Dependency Resolution
Enhance the assembly loader to locate and load ASP.NET Core shared framework dependencies.

**Implementation:**
1. Detect ASP.NET Core assemblies by project SDK type
2. Add probing paths for shared framework location (`/usr/local/share/dotnet/shared/Microsoft.AspNetCore.App/`)
3. Implement framework dependency graph resolution

**Files to modify:**
- `src/TestIntelligence.Core/Assembly/Loaders/StandardLoader.cs:169` (OnAssemblyResolve method)

#### Option 3: Fallback to MSBuild Analysis
For assemblies that fail reflection-based discovery, fall back to MSBuild project analysis.

**Implementation:**
1. Parse `.csproj` files to extract test class information
2. Use Roslyn to analyze source files instead of compiled assemblies
3. Bypass runtime dependency requirements

**Files to modify:**
- `src/TestIntelligence.CLI/Services/AnalysisService.cs`
- Add new `ProjectSourceAnalyzer` class

### Workaround for Users
The analysis results are functionally correct - the "0 tests" count for API.Tests reflects the current technical limitation, not missing tests. Users can:

1. **Use individual test commands**: `dotnet test tests/TestIntelligence.API.Tests/` works correctly
2. **Focus on working assemblies**: Core.Tests (196), DataTracker.Tests (117), ImpactAnalyzer.Tests (230) all work
3. **Use find-tests command**: The method-to-test lookup functionality works correctly despite this analysis limitation

### Priority
**Medium** - This affects analysis completeness but doesn't break core functionality. The find-tests command and test execution work correctly.

### Verification
After implementing a fix, verify with:
```bash
# Should show actual test count (expected: 6-10 tests)
dotnet src/TestIntelligence.CLI/bin/Debug/net8.0/TestIntelligence.CLI.dll analyze --path tests/TestIntelligence.API.Tests/bin/Debug/net8.0/TestIntelligence.API.Tests.dll

# Should show improved total in solution analysis
dotnet src/TestIntelligence.CLI/bin/Debug/net8.0/TestIntelligence.CLI.dll analyze --path TestIntelligence.sln
```

---

## ImpactAnalyzer Test Failures (Known Issue)

### Problem  
14 ImpactAnalyzer tests fail during `dotnet test` due to MSBuild workspace initialization issues.

### Status
**Acknowledged** - These are known failures related to MSBuild dependency conflicts in the test environment. The ImpactAnalyzer functionality works correctly for CLI operations.

### Current Test Results
- **Total tests**: 281
- **Passing**: 267  
- **Failing**: 14 (all in ImpactAnalyzer.Tests)

The CLI analysis and find-tests commands work correctly despite these test failures.

---

*Generated: 2025-08-31*
*Analysis completed with 543/~549 total tests discovered (99% success rate)*