# TestIntelligence

**Intelligent test analysis and selection for .NET applications**

TestIntelligence helps you run the right tests at the right time. It analyzes your codebase to intelligently select which tests to run based on your code changes, dramatically reducing CI/CD execution times while maintaining high confidence in test coverage.

## What TestIntelligence Does

- **Smart Test Selection**: Only run tests affected by your code changes
- **Method Coverage Analysis**: Find all tests that exercise a specific method  
- **Execution Path Tracing**: Trace all production code executed by test methods for deep analysis
- **Impact Analysis**: Understand which tests are affected by git diffs or code changes
- **Test Categorization**: Automatically classify tests as Unit, Integration, Database, API, etc.
- **Parallel Execution Safety**: Detect data conflicts to prevent test failures
- **Multi-Framework Support**: Works with NUnit, xUnit, MSTest across .NET Framework 4.8, .NET Core, and .NET 5+

## Quick Start

### Installation

```bash
# Build and install the CLI tool
dotnet pack src/TestIntelligence.CLI/ -o nupkg
dotnet tool install -g --add-source ./nupkg TestIntelligence.CLI
```

### Basic Usage

```bash
# Find which tests exercise a specific method
test-intel find-tests --method "UserService.CreateUser" --solution MySolution.sln

# Trace production code execution paths for a test
test-intel trace-execution --test "MyProject.Tests.UserServiceTests.CreateUser_WithValidData" --solution MySolution.sln

# Analyze what tests are affected by your git changes  
test-intel diff --solution MySolution.sln --git-command "diff HEAD~1"

# Select optimal tests based on file changes
test-intel select --path MyProject.Tests.dll --changes "src/UserService.cs" --confidence Medium
```

## CLI Commands

All commands support `--verbose` for detailed output and `--format json` for machine-readable output.

### 🎯 find-tests - Find Tests for a Method

Find all tests that exercise a specific production method.

```bash
# Basic usage
test-intel find-tests --method "UserService.CreateUser" --solution MySolution.sln

# With confidence filtering and JSON output
test-intel find-tests \
  --method "PaymentService.ProcessPayment" \
  --solution MySolution.sln \
  --format json \
  --output coverage.json
```

**Use cases**: Refactoring safety, test gap analysis, code review verification

### 📊 diff - Analyze Git Changes

Analyze git diffs to determine which tests are impacted by code changes.

```bash
# Analyze changes since last commit
test-intel diff --solution MySolution.sln --git-command "diff HEAD~1"

# Analyze staged changes
test-intel diff --solution MySolution.sln --git-command "diff --cached"

# Analyze from patch file
test-intel diff --solution MySolution.sln --diff-file changes.patch

# Analyze diff content directly
test-intel diff --solution MySolution.sln --diff-content "$(git diff HEAD~1)"
```

**Use cases**: PR analysis, pre-commit hooks, CI/CD optimization

### ⚡ select - Smart Test Selection

Select optimal tests based on code changes with AI-driven algorithms.

```bash
# Select tests for changed files
test-intel select \
  --path MyProject.Tests.dll \
  --changes "src/UserService.cs,src/PaymentController.cs" \
  --confidence Medium \
  --max-tests 50

# With time constraints
test-intel select \
  --path MyProject.Tests.dll \
  --changes "src/UserService.cs" \
  --confidence High \
  --max-time "10m"
```

**Confidence Levels**:
- `Fast`: ~70% confidence, ≤30s execution
- `Medium`: ~85% confidence, ≤5min execution  
- `High`: ~95% confidence, ≤15min execution
- `Full`: ~99% confidence, ≤1hr execution

### 🏷️ categorize - Test Classification

Automatically categorize tests by type and characteristics.

```bash
test-intel categorize --path MyProject.Tests.dll --output categories.txt
```

**Categories**: Unit, Integration, Database, API, UI, EndToEnd, Performance, Security

### 🔍 analyze - Assembly Analysis

Analyze test assemblies for detailed metadata and statistics.

```bash
test-intel analyze --path MyProject.Tests.dll --format json --output analysis.json
```

### 🕸️ callgraph - Build Call Graph

Build a comprehensive call graph showing method dependencies across your codebase.

```bash
# Build call graph for entire solution
test-intel callgraph --path MySolution.sln --verbose

# Build call graph with JSON output
test-intel callgraph \
  --path MySolution.sln \
  --format json \
  --output callgraph.json
```

**Use cases**: Dependency analysis, refactoring impact assessment, code architecture visualization

### 🔍 trace-execution - Trace Production Code Execution

Trace all production code executed by a specific test method to understand execution paths and dependencies.

```bash
# Trace execution for a specific test method
test-intel trace-execution \
  --test "MyProject.Tests.UserServiceTests.CreateUser_WithValidData_ShouldCreateUser" \
  --solution MySolution.sln \
  --verbose

# Generate JSON output with limited depth
test-intel trace-execution \
  --test "MyProject.Tests.UserServiceTests.CreateUser_WithValidData_ShouldCreateUser" \
  --solution MySolution.sln \
  --format json \
  --output execution-trace.json \
  --max-depth 10
```

**Use cases**: Understanding test coverage depth, debugging test failures, code path analysis, refactoring impact assessment

## RESTful API

Start the API server for programmatic access and AI agent integration:

```bash
# Start the API server
dotnet run --project src/TestIntelligence.API/

# Available at:
# - HTTP: http://localhost:5000
# - HTTPS: https://localhost:5001  
# - Swagger UI: https://localhost:5001/swagger
```

### Key API Endpoints

```bash
# Find tests that exercise a specific method
POST /api/testselection/find-tests
{
  "methodId": "UserService.CreateUser",
  "solutionPath": "/path/to/solution.sln",
  "minimumConfidence": 0.7
}

# Analyze git diff for test impact
POST /api/testselection/analyze-diff
{
  "solutionPath": "/path/to/solution.sln",
  "diffContent": "git diff content...",
  "confidenceLevel": "Medium"
}

# Get optimal test plan
POST /api/testselection/plan
{
  "codeChanges": { /* change set */ },
  "confidenceLevel": "High",
  "maxTests": 50
}

# Trace execution paths for a test method
POST /api/execution/trace
{
  "testMethod": "MyProject.Tests.UserServiceTests.CreateUser_WithValidData_ShouldCreateUser",
  "solutionPath": "/path/to/solution.sln",
  "maxDepth": 10,
  "includeSystemCalls": false
}
```

## Programmatic Usage

```csharp
using TestIntelligence.SelectionEngine.Engine;
using TestIntelligence.SelectionEngine.Models;

var engine = new TestSelectionEngine();
var changes = new List<CodeChange>
{
    new CodeChange("src/UserService.cs", CodeChangeType.Modified, 
                   new[] { "CreateUser" }, new[] { "UserService" })
};
var changeSet = new CodeChangeSet(changes);
var testPlan = await engine.GetOptimalTestPlanAsync(changeSet, ConfidenceLevel.High);
```

## CI/CD Integration

### GitHub Actions

TestIntelligence integrates seamlessly with your CI/CD pipeline to run only the tests that matter:

```yaml
- name: Smart Test Selection
  run: |
    # Analyze git diff for impacted tests
    test-intel diff \
      --solution "MySolution.sln" \
      --git-command "diff ${{ github.event.pull_request.base.sha }}" \
      --format json \
      --output impact.json
    
    # Run high-confidence tests
    HIGH_CONFIDENCE_TESTS=$(jq -r '.impactedTests[] | select(.confidence >= 0.7) | .methodName' impact.json | tr '\n' '|')
    dotnet test --filter "FullyQualifiedName~$HIGH_CONFIDENCE_TESTS"

- name: Method Coverage Check
  run: |
    # Check coverage for critical methods
    test-intel find-tests \
      --method "PaymentService.ProcessPayment" \
      --solution "MySolution.sln" \
      --format json \
      --output coverage.json
    
    # Fail if insufficient coverage
    TEST_COUNT=$(jq '.testsFound' coverage.json)
    if [ "$TEST_COUNT" -lt 3 ]; then
      echo "❌ Insufficient test coverage for PaymentService.ProcessPayment"
      exit 1
    fi
```

### Azure DevOps

```yaml
- task: DotNetCoreCLI@2
  displayName: 'Intelligent Test Selection'
  inputs:
    command: 'custom'
    custom: 'tool'
    arguments: 'run test-intel diff --solution $(Build.SourcesDirectory)/MySolution.sln --git-command "diff HEAD~1" --format json --output test-impact.json'
```

## Performance Benefits

- **Fast**: ~70% reduction in execution time
- **Medium**: ~50% reduction with 85% coverage confidence  
- **High**: ~30% reduction with 95% coverage confidence
- **Smart Parallelization**: Prevents data conflicts and race conditions

## Architecture

TestIntelligence is built with modular components:

- **Core**: Assembly loading and test discovery across .NET versions
- **ImpactAnalyzer**: Roslyn-based code analysis and call graph building  
- **DataTracker**: Entity Framework pattern detection for safe parallel execution
- **SelectionEngine**: AI algorithms for intelligent test selection
- **CLI**: Command-line interface and global tool
- **API**: RESTful endpoints for programmatic access

## Development & Testing

```bash
# Build entire solution
dotnet build

# Run all tests
dotnet test

# Run specific test suites
dotnet test tests/TestIntelligence.Core.Tests/         
dotnet test tests/TestIntelligence.ImpactAnalyzer.Tests/ 
dotnet test tests/TestIntelligence.DataTracker.Tests/   
```

## Contributing

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/amazing-feature`  
3. Commit your changes: `git commit -m 'Add amazing feature'`
4. Push to the branch: `git push origin feature/amazing-feature`
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## Appendix: Detailed Output Examples

<details>
<summary>Method Coverage Analysis (JSON)</summary>

```json
{
  "method": "MyApp.Services.UserService.CreateUser",
  "solutionPath": "MyApp.sln", 
  "testsFound": 8,
  "coverageTests": [
    {
      "testName": "CreateUser_WithValidInput_ShouldCreateUser",
      "testClass": "UserServiceTests",
      "category": "Unit",
      "framework": "NUnit", 
      "confidence": 0.95,
      "callDepth": 1,
      "callPath": ["UserServiceTests.CreateUser_WithValidInput_ShouldCreateUser", "UserService.CreateUser"],
      "reasonsForInclusion": ["Direct method call", "Method name similarity"]
    }
  ],
  "analysisStatistics": {
    "totalProjectsAnalyzed": 5,
    "totalTestMethodsScanned": 247,
    "totalAnalysisTimeMs": 15750
  }
}
```
</details>

<details>
<summary>Test Impact Analysis (Text)</summary>

```
=== Test Impact Analysis Results ===
Code Changes: 3 changes across 2 files
Changed Methods: 5
Potentially Impacted Tests: 12

High Confidence (≥70%):
  [95%] UserServiceTests.CreateUser_WithValidData_ShouldCreateUser
  [87%] UserServiceTests.UpdateUser_WithValidData_ShouldUpdate

Medium Confidence (40-69%):
  [65%] UserControllerTests.CreateUser_ValidRequest_Returns201
  [58%] UserIntegrationTests.UserWorkflow_EndToEnd
```
</details>

<details>
<summary>Test Categorization Report</summary>

```
Assembly: MyProject.Tests.dll
Total Tests: 156

┌─────────────┬───────┬─────────────────────────────────────┐
│ Category    │ Count │ Examples                            │
├─────────────┼───────┼─────────────────────────────────────┤
│ Unit        │    89 │ UserService_CreateUser_Success      │
│ Integration │    45 │ UserWorkflow_EndToEnd               │
│ Database    │    15 │ UserRepository_DatabaseOperations   │
│ API         │     7 │ UserController_HttpEndpoints        │
└─────────────┴───────┴─────────────────────────────────────┘
```
</details>

<details>
<summary>Execution Trace Analysis (JSON)</summary>

```json
{
  "testMethod": "MyProject.Tests.UserServiceTests.CreateUser_WithValidData_ShouldCreateUser",
  "solutionPath": "MyProject.sln",
  "maxDepth": 10,
  "executionTrace": {
    "totalMethods": 45,
    "maxCallDepth": 8,
    "executionPaths": [
      {
        "depth": 1,
        "methodName": "MyProject.Services.UserService.CreateUser",
        "fileName": "UserService.cs", 
        "lineNumber": 42,
        "callType": "DirectCall",
        "children": [
          {
            "depth": 2,
            "methodName": "MyProject.Services.ValidationService.ValidateUser",
            "fileName": "ValidationService.cs",
            "lineNumber": 15,
            "callType": "DirectCall"
          },
          {
            "depth": 2,
            "methodName": "MyProject.Repositories.UserRepository.SaveUser",
            "fileName": "UserRepository.cs", 
            "lineNumber": 28,
            "callType": "DirectCall"
          }
        ]
      }
    ]
  },
  "analysisStatistics": {
    "totalProjectsAnalyzed": 3,
    "totalSourceFilesAnalyzed": 189,
    "totalAnalysisTimeMs": 8450
  }
}
```
</details>

**TestIntelligence helps you run smarter tests, not harder tests.**