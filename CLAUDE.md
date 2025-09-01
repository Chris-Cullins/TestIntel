# TestIntelligence Library - Developer Guide

## Project Overview

TestIntelligence is a comprehensive test analysis and selection library for .NET projects supporting both .NET Framework 4.8 and .NET 8+. It provides intelligent test selection, impact analysis, call graph generation, and test categorization through advanced static code analysis.

## Architecture

The solution consists of several key components:
- **Core**: Cross-framework assembly loading and test discovery
- **Categorizer**: Test classification (Unit, Integration, Database, API, UI)
- **DataTracker**: Database dependency and parallel execution analysis
- **ImpactAnalyzer**: Roslyn-based code change impact analysis  
- **SelectionEngine**: Intelligent test selection based on confidence levels
- **CLI**: Command-line interface with comprehensive analysis commands
- **API**: RESTful service for programmatic access

## Build Commands

### Build Entire Solution
```bash
dotnet build
```

### Build Specific Projects
```bash
dotnet build src/TestIntelligence.Core/
dotnet build src/TestIntelligence.CLI/
dotnet build src/TestIntelligence.ImpactAnalyzer/
```

### Run Tests
```bash
# Run all tests
dotnet test

# Run specific test projects
dotnet test tests/TestIntelligence.Core.Tests/
dotnet test tests/TestIntelligence.DataTracker.Tests/
dotnet test tests/TestIntelligence.ImpactAnalyzer.Tests/
```

## CLI Usage

### Core Analysis Commands

#### Test Analysis
```bash
# Analyze test assemblies for categorization
dotnet run --project src/TestIntelligence.CLI analyze \
  --path MySolution.sln \
  --format json \
  --output analysis.json \
  --verbose

# Categorize tests by type
dotnet run --project src/TestIntelligence.CLI categorize \
  --path MySolution.sln \
  --output categories.json
```

#### Intelligent Test Selection
```bash
# Select optimal tests based on changes
dotnet run --project src/TestIntelligence.CLI select \
  --path MySolution.sln \
  --changes "src/MyProject/MyClass.cs" \
  --confidence Medium \
  --max-tests 50 \
  --output selection.json
```

#### Impact Analysis from Git Diff
```bash
# Analyze impact from git diff
dotnet run --project src/TestIntelligence.CLI diff \
  --solution MySolution.sln \
  --git-command "diff HEAD~1" \
  --format json \
  --verbose

# Analyze from diff file
dotnet run --project src/TestIntelligence.CLI diff \
  --solution MySolution.sln \
  --diff-file changes.patch \
  --output impact.json
```

### Advanced Analysis Commands

#### Call Graph Analysis
```bash
# Generate method call graph
dotnet run --project src/TestIntelligence.CLI callgraph \
  --path MySolution.sln \
  --format json \
  --output callgraph.json \
  --max-methods 100 \
  --verbose
```

#### Method-to-Test Reverse Lookup
```bash
# Find all tests that exercise a specific method
dotnet run --project src/TestIntelligence.CLI find-tests \
  --method "MyNamespace.MyClass.MyMethod" \
  --solution MySolution.sln \
  --format json \
  --output coverage.json \
  --verbose
```

#### Test Execution Tracing
```bash
# Trace all production code executed by a test
dotnet run --project src/TestIntelligence.CLI trace-execution \
  --test "MyTestNamespace.MyTestClass.MyTestMethod" \
  --solution MySolution.sln \
  --max-depth 20 \
  --verbose
```

#### Coverage Analysis
```bash
# Analyze how well tests cover code changes
dotnet run --project src/TestIntelligence.CLI analyze-coverage \
  --solution MySolution.sln \
  --tests "Test1" "Test2" "Test3" \
  --git-command "diff HEAD~1" \
  --verbose
```

### Configuration Management

#### Initialize Configuration
```bash
# Create default configuration file
dotnet run --project src/TestIntelligence.CLI config init \
  --path MySolution.sln

# Verify project filtering
dotnet run --project src/TestIntelligence.CLI config verify \
  --path MySolution.sln \
  --format text
```

### Cache Management
```bash
# Check cache status
dotnet run --project src/TestIntelligence.CLI cache \
  --solution MySolution.sln \
  --action status

# Clear cache
dotnet run --project src/TestIntelligence.CLI cache \
  --solution MySolution.sln \
  --action clear

# Initialize/warm up cache
dotnet run --project src/TestIntelligence.CLI cache \
  --solution MySolution.sln \
  --action warm-up \
  --verbose
```

## Development Workflow

### 1. Test Discovery Pattern
The library supports multiple test frameworks:
- **NUnit**: `[Test]`, `[TestCase]`, `[TestFixture]`
- **xUnit**: `[Fact]`, `[Theory]`, `[Collection]`
- **MSTest**: `[TestMethod]`, `[TestClass]`

### 2. Confidence Levels
- **Fast**: Unit tests directly affected (30 sec, 70% confidence)
- **Medium**: Unit + integration tests (5 min, 85% confidence)  
- **High**: All affected + dependent tests (15 min, 95% confidence)
- **Full**: Complete test suite (full time, 100% confidence)

### 3. Test Categories
- **Unit**: Pure logic tests, no external dependencies
- **Integration**: Tests with external systems (DB, HTTP, files)
- **Database**: Entity Framework, SQL operations
- **API**: HTTP client tests, web service calls
- **UI**: Selenium, browser automation

## Project Structure

```
TestIntelligence/
├── src/
│   ├── TestIntelligence.Core/              # Assembly loading, caching
│   ├── TestIntelligence.Categorizer/       # Test classification
│   ├── TestIntelligence.DataTracker/       # Database dependency analysis
│   ├── TestIntelligence.ImpactAnalyzer/    # Roslyn-based impact analysis
│   ├── TestIntelligence.SelectionEngine/   # Intelligent test selection
│   ├── TestIntelligence.API/               # RESTful web API
│   ├── TestIntelligence.CLI/               # Command-line interface
│   ├── TestIntelligence.Framework48Adapter/# .NET Framework 4.8 support
│   └── TestIntelligence.NetCoreAdapter/    # .NET Core/8+ support
├── tests/                                   # Comprehensive test suite
├── samples/                                 # Sample projects for testing
└── reports/                                # Generated analysis reports
```

## Key Features

### ✅ Completed Features
- Cross-framework assembly loading (.NET Framework 4.8 + .NET 8)
- Advanced Roslyn syntax analysis with semantic modeling
- Method call graph generation with transitive dependencies
- Test discovery and categorization across multiple frameworks
- Database pattern detection (EF6/EF Core)
- Git diff impact analysis
- Method-to-test reverse lookup with confidence scoring
- Test execution tracing and production code mapping
- Comprehensive CLI with 10+ analysis commands
- RESTful API for programmatic access
- Persistent caching for large solutions

### 🚀 Ready to Use
- All compilation errors have been resolved
- CLI functionality is fully operational

## Performance Characteristics

- **Large Solutions**: Handles 10,000+ tests with caching
- **Analysis Speed**: <30 seconds for most operations
- **Memory Usage**: Optimized for large codebases with compression
- **Parallel Processing**: Multi-threaded analysis where possible

## Integration Points

The library is designed for:
- **AI Agents**: RESTful API for automated code analysis
- **CI/CD Pipelines**: CLI integration for build automation
- **IDEs**: Extensible for VS/VS Code integration
- **Coverage Tools**: Compatible with OpenCover, Coverlet

## Test Statistics
- **Core Tests**: 62 passing
- **DataTracker Tests**: 119 passing  
- **ImpactAnalyzer Tests**: 34 passing
- **Total**: 215 tests passing ✅

## VSCode Integration
The test projects are properly configured with:
- xUnit test framework
- .NET 8.0 target framework
- All required dependencies

If VSCode isn't discovering tests:
1. Open Command Palette (Cmd+Shift+P)
2. Run "Test: Refresh Tests"
3. Or use "Test: Reset and Reload All Test Data"

## Validation & Quality Assurance

### Running the Test Suite
```bash
# Run all tests to verify core functionality
dotnet test

# Run tests with detailed output
dotnet test --verbosity normal

# Run specific test categories
dotnet test --filter Category=Unit
dotnet test --filter Category=Integration

# Generate test coverage report
dotnet test --collect:"XPlat Code Coverage"
```

### Validating Analysis Outputs

#### 1. Test Categorization Validation
```bash
# Generate test categorization report
dotnet run --project src/TestIntelligence.CLI categorize \
  --path TestIntelligence.sln \
  --output test-categories.json

# Manually verify categories match actual test patterns:
# - Unit tests should have no external dependencies
# - Integration tests should call databases, APIs, or file systems
# - Database tests should use Entity Framework or SQL connections
```

#### 2. Call Graph Accuracy Verification
```bash
# Generate call graph for a known method
dotnet run --project src/TestIntelligence.CLI callgraph \
  --path TestIntelligence.sln \
  --format json \
  --output callgraph.json \
  --verbose

# Validate call graph accuracy:
# 1. Pick a method you know well from the codebase
# 2. Compare the generated call graph to actual method calls in the source
# 3. Verify transitive dependencies are correctly identified
# 4. Check that framework calls vs application calls are properly categorized
```

#### 3. Method-to-Test Coverage Validation
```bash
# Test reverse lookup for a specific method
dotnet run --project src/TestIntelligence.CLI find-tests \
  --method "TestIntelligence.Core.Discovery.NUnitTestDiscovery.DiscoverTestsAsync" \
  --solution TestIntelligence.sln \
  --verbose

# Manual verification steps:
# 1. Search the test codebase for the target method name
# 2. Verify all found tests actually exercise the method (directly or indirectly)
# 3. Check confidence scores align with call depth and test types
# 4. Ensure no legitimate tests are missed
```

#### 4. Impact Analysis Verification
```bash
# Create a test diff and analyze impact
echo "Test change" >> src/TestIntelligence.Core/Discovery/NUnitTestDiscovery.cs
git add -A
git diff --cached > test-changes.patch
git reset HEAD

# Analyze the impact
dotnet run --project src/TestIntelligence.CLI diff \
  --solution TestIntelligence.sln \
  --diff-file test-changes.patch \
  --verbose

# Verification checklist:
# 1. All tests that directly use the changed method should be identified
# 2. Tests that indirectly depend on the method should be included
# 3. Unrelated tests should not appear in the results
# 4. Confidence scores should reflect the likelihood of impact
```

### Cache Validation

#### 1. Cache Storage Verification
```bash
# Initialize cache and check structure
dotnet run --project src/TestIntelligence.CLI cache \
  --solution TestIntelligence.sln \
  --action init \
  --verbose

# Verify cache directory structure
ls -la ~/.testintelligence/cache/
# Should contain:
# - solution-specific subdirectories
# - compressed .cache files
# - metadata.json files
# - lock files for concurrent access
```

#### 2. Cache Performance Testing
```bash
# Test cache warm-up
time dotnet run --project src/TestIntelligence.CLI cache \
  --solution TestIntelligence.sln \
  --action warm-up

# Run analysis with cold cache
dotnet run --project src/TestIntelligence.CLI cache \
  --solution TestIntelligence.sln \
  --action clear

time dotnet run --project src/TestIntelligence.CLI analyze \
  --path TestIntelligence.sln

# Run analysis with warm cache (should be significantly faster)
time dotnet run --project src/TestIntelligence.CLI analyze \
  --path TestIntelligence.sln
```

#### 3. Cache Consistency Validation
```bash
# Generate baseline analysis
dotnet run --project src/TestIntelligence.CLI analyze \
  --path TestIntelligence.sln \
  --output baseline-analysis.json

# Clear cache and regenerate
dotnet run --project src/TestIntelligence.CLI cache \
  --solution TestIntelligence.sln \
  --action clear

dotnet run --project src/TestIntelligence.CLI analyze \
  --path TestIntelligence.sln \
  --output cached-analysis.json

# Compare outputs (should be identical)
diff baseline-analysis.json cached-analysis.json
```

### Integration Testing

#### 1. Multi-Framework Validation
```bash
# Test with .NET Framework 4.8 sample
dotnet run --project src/TestIntelligence.CLI analyze \
  --path samples/SampleMonorepo.Framework48/ \
  --verbose

# Test with .NET 8 sample  
dotnet run --project src/TestIntelligence.CLI analyze \
  --path samples/SampleMonorepo.Net8/ \
  --verbose

# Verify both work without assembly loading conflicts
```

#### 2. Large Solution Stress Testing
```bash
# Test with large solution (if available)
time dotnet run --project src/TestIntelligence.CLI analyze \
  --path /path/to/large/solution.sln \
  --verbose

# Monitor memory usage during analysis
# Verify performance stays within acceptable bounds
```

### Manual Code Review Checklist

#### 1. Test Discovery Accuracy
- [ ] All test methods with `[Test]`, `[Fact]`, `[TestMethod]` are found
- [ ] Test fixtures and classes are properly identified
- [ ] Parameterized tests (`[TestCase]`, `[Theory]`) are handled correctly
- [ ] Setup/teardown methods are recognized

#### 2. Categorization Logic
- [ ] Database tests properly detect EF/SQL usage
- [ ] API tests identify HttpClient and web service calls
- [ ] Integration tests catch file I/O and external dependencies
- [ ] Unit tests have no external dependencies flagged

#### 3. Call Graph Construction
- [ ] Method calls within same assembly are tracked
- [ ] Cross-assembly calls are followed correctly
- [ ] Generic method calls are handled properly
- [ ] Interface implementations are resolved

#### 4. Performance Benchmarks
```bash
# Benchmark key operations
dotnet run --project src/TestIntelligence.CLI analyze \
  --path TestIntelligence.sln \
  --verbose | grep "Analysis completed in"

# Expected performance targets:
# - Small solution (<100 tests): <5 seconds
# - Medium solution (100-1000 tests): <15 seconds  
# - Large solution (1000+ tests): <30 seconds with caching
```

### Troubleshooting Common Issues

#### 1. Assembly Loading Problems
```bash
# Check MSBuild registration
dotnet run --project src/TestIntelligence.CLI analyze \
  --path TestIntelligence.sln \
  --verbose 2>&1 | grep -i msbuild
```

#### 2. Cache Corruption
```bash
# Clear and rebuild cache if issues occur
dotnet run --project src/TestIntelligence.CLI cache \
  --solution TestIntelligence.sln \
  --action clear

dotnet run --project src/TestIntelligence.CLI cache \
  --solution TestIntelligence.sln \
  --action init
```

#### 3. Memory Usage Issues
```bash
# Monitor memory during large analyses
/usr/bin/time -v dotnet run --project src/TestIntelligence.CLI analyze \
  --path large-solution.sln
```

## Next Steps

1. **Test CLI Commands**: Verify all commands work with your solutions
2. **Run Validation Suite**: Execute all validation steps above
3. **Performance Testing**: Validate with large real-world solutions
4. **Integration**: Connect to CI/CD pipelines and AI agents