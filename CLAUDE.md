# TestIntelligence Library - Development Notes

## Test Commands

### Run All Tests
```bash
dotnet test
```

### Run Specific Test Project
```bash
# Core tests
dotnet test tests/TestIntelligence.Core.Tests/

# DataTracker tests  
dotnet test tests/TestIntelligence.DataTracker.Tests/

# ImpactAnalyzer tests (Phase 3)
dotnet test tests/TestIntelligence.ImpactAnalyzer.Tests/
```

### VSCode Test Discovery
The test projects are properly configured with:
- xUnit test framework
- .NET 8.0 target framework
- All required dependencies

If VSCode isn't discovering tests:
1. Open Command Palette (Cmd+Shift+P)
2. Run "Test: Refresh Tests"
3. Or use "Test: Reset and Reload All Test Data"

## Build Commands

### Build Entire Solution
```bash
dotnet build
```

### Build Specific Project
```bash
dotnet build src/TestIntelligence.ImpactAnalyzer/
```

## CLI Commands

### Find Tests Exercising a Method (Phase 4)
```bash
# Find all tests that exercise a specific method
dotnet src/TestIntelligence.CLI/bin/Debug/net8.0/TestIntelligence.CLI.dll find-tests \
  --method "MyNamespace.MyClass.MyMethod" \
  --solution "MySolution.sln" \
  --verbose

# JSON output format
dotnet src/TestIntelligence.CLI/bin/Debug/net8.0/TestIntelligence.CLI.dll find-tests \
  --method "MyNamespace.MyClass.MyMethod" \
  --solution "MySolution.sln" \
  --format json \
  --output results.json
```

### Build Call Graph
```bash
# Build call graph for entire solution
dotnet src/TestIntelligence.CLI/bin/Debug/net8.0/TestIntelligence.CLI.dll callgraph \
  --path MySolution.sln \
  --verbose

# Build call graph with output to file
dotnet src/TestIntelligence.CLI/bin/Debug/net8.0/TestIntelligence.CLI.dll callgraph \
  --path MySolution.sln \
  --output callgraph.json \
  --format json
```

### Other Available Commands
```bash
# Show all available commands
dotnet src/TestIntelligence.CLI/bin/Debug/net8.0/TestIntelligence.CLI.dll --help

# Analyze test assemblies
dotnet src/TestIntelligence.CLI/bin/Debug/net8.0/TestIntelligence.CLI.dll analyze --path MySolution.sln
```

## Project Status

### ✅ Phase 1: Core Analysis Engine (COMPLETED)
- Cross-framework assembly loading
- NUnit test discovery
- Assembly metadata caching

### ✅ Phase 2: Data Dependency Tracking (COMPLETED) 
- EF6/EF Core pattern detection
- Data conflict analysis
- Parallel execution compatibility

### ✅ Phase 3: Advanced Impact Analysis (COMPLETED)
- Comprehensive Roslyn syntax tree analysis
- Method call graph building with transitive dependencies
- Type usage analysis and semantic modeling
- **34 tests passing** ✅

### ✅ Phase 4: Method-to-Test Reverse Lookup (COMPLETED)
- **TestCoverageAnalyzer**: Core service for finding tests that exercise specific methods
- **CLI Integration**: `find-tests` command with method lookup functionality
- **RESTful API**: Comprehensive endpoints for test coverage analysis
- **Method-to-Test Mapping**: Reverse lookup from production methods to covering tests
- **Call Path Analysis**: BFS traversal to find test coverage relationships
- **Confidence Scoring**: Multi-factor scoring based on call depth and test types
- **Test Classification**: Advanced heuristics for identifying and categorizing test methods

## Test Statistics
- **Core Tests**: 62 passing
- **DataTracker Tests**: 119 passing  
- **ImpactAnalyzer Tests**: 34 passing
- **Total**: 215 tests passing ✅