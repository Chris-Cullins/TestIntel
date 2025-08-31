# TestIntelligence

**Intelligent test analysis and selection library for .NET applications**

TestIntelligence is a comprehensive solution that analyzes your test suites to provide intelligent test selection, categorization, and impact analysis. Reduce CI/CD execution times while maintaining high confidence in test coverage through AI-driven test selection algorithms.

## 🚀 Features

- **Cross-Framework Support**: Works with .NET Framework 4.8, .NET Core, and .NET 5+
- **Intelligent Test Selection**: AI-driven algorithms select optimal tests based on code changes
- **Test Categorization**: Automatically categorizes tests (Unit, Integration, Database, API, UI, etc.)
- **Impact Analysis**: Advanced Roslyn-based analysis of code dependencies and test impact
- **Method-to-Test Reverse Lookup**: Find all tests that exercise specific production methods
- **Data Conflict Detection**: Identifies tests that cannot run in parallel due to shared data
- **Multiple Test Frameworks**: Supports NUnit, MSTest, and xUnit
- **CLI Tool**: Command-line interface for CI/CD integration
- **RESTful API**: Production-ready API for AI agent integration
- **JSON Output**: Machine-readable output for automation workflows

## 📦 Installation

### As a .NET Global Tool

```bash
# Build and install the CLI tool
dotnet pack src/TestIntelligence.CLI/ -o nupkg
dotnet tool install -g --add-source ./nupkg TestIntelligence.CLI
```

### As NuGet Packages

```xml
<!-- Core functionality -->
<PackageReference Include="TestIntelligence.Core" Version="1.0.0" />

<!-- Advanced impact analysis -->
<PackageReference Include="TestIntelligence.ImpactAnalyzer" Version="1.0.0" />

<!-- Test selection engine -->
<PackageReference Include="TestIntelligence.SelectionEngine" Version="1.0.0" />

<!-- Data dependency tracking -->
<PackageReference Include="TestIntelligence.DataTracker" Version="1.0.0" />
```

## 🎯 Quick Start

### CLI Usage

```bash
# Analyze test assemblies
test-intel analyze --path MyProject.Tests.dll --format json --output analysis.json

# Categorize tests by type
test-intel categorize --path MyProject.Tests.dll --output categories.txt

# Select optimal tests based on code changes
test-intel select \
  --path MyProject.Tests.dll \
  --changes "src/UserService.cs,src/PaymentController.cs" \
  --confidence High \
  --max-tests 50 \
  --max-time "10m"

# NEW: Analyze test impact from git diff or patch files
test-intel diff \
  --solution MySolution.sln \
  --git-command "diff HEAD~1" \
  --format json \
  --output diff-impact.json

# Analyze from diff file
test-intel diff \
  --solution MySolution.sln \
  --diff-file changes.patch \
  --verbose

# Analyze from diff content directly
test-intel diff \
  --solution MySolution.sln \
  --diff-content "$(git diff HEAD~1)" \
  --format text

# NEW: Find tests that exercise specific methods (Method-to-Test Reverse Lookup)
test-intel find-tests \
  --method "MyNamespace.MyClass.MyMethod" \
  --solution MySolution.sln \
  --verbose

# Find tests with JSON output
test-intel find-tests \
  --method "UserService.CreateUser" \
  --solution MySolution.sln \
  --format json \
  --output method-coverage.json
```

### Programmatic Usage

```csharp
using TestIntelligence.SelectionEngine.Engine;
using TestIntelligence.SelectionEngine.Models;
using TestIntelligence.ImpactAnalyzer.Models;

// Initialize the test selection engine
var engine = new TestSelectionEngine();

// Create code change set
var changes = new List<CodeChange>
{
    new CodeChange("src/UserService.cs", CodeChangeType.Modified, 
                   new[] { "GetUserById", "UpdateUser" }, 
                   new[] { "UserService" })
};
var changeSet = new CodeChangeSet(changes);

// Get optimal test plan
var testPlan = await engine.GetOptimalTestPlanAsync(changeSet, ConfidenceLevel.High);

Console.WriteLine($"Selected {testPlan.Tests.Count} tests");
Console.WriteLine($"Estimated duration: {testPlan.EstimatedDuration}");
Console.WriteLine($"Parallel batches: {testPlan.Batches.Count}");
```

## 📊 Output Artifacts

### Analysis Report (JSON)

```json
{
  "analyzedPath": "MyProject.Tests.dll",
  "timestamp": "2025-01-15T10:30:00Z",
  "assemblies": [
    {
      "assemblyName": "MyProject.Tests",
      "framework": ".NET,Version=v8.0",
      "totalTests": 156,
      "testMethods": [
        {
          "methodName": "Should_Create_User_Successfully",
          "className": "UserServiceTests",
          "category": "Unit",
          "tags": ["fast", "isolated"],
          "dependencies": ["UserService", "IUserRepository"],
          "averageExecutionTime": "00:00:00.125"
        }
      ]
    }
  ],
  "summary": {
    "totalTests": 156,
    "categoryBreakdown": {
      "Unit": 89,
      "Integration": 45,
      "Database": 15,
      "API": 7
    }
  }
}
```

### Method-to-Test Coverage Report (JSON)

```json
{
  "method": "MyApp.Services.UserService.CreateUser",
  "solutionPath": "MyApp.sln",
  "analysisTimestamp": "2025-01-15T14:30:00Z",
  "testsFound": 8,
  "coverageTests": [
    {
      "testName": "CreateUser_WithValidInput_ShouldCreateUser",
      "testClass": "UserServiceTests", 
      "testNamespace": "MyApp.Tests.Services",
      "assemblyPath": "MyApp.Tests.dll",
      "testMethod": "MyApp.Tests.Services.UserServiceTests.CreateUser_WithValidInput_ShouldCreateUser",
      "category": "Unit",
      "framework": "NUnit",
      "confidence": 0.95,
      "callDepth": 1,
      "callPath": [
        "UserServiceTests.CreateUser_WithValidInput_ShouldCreateUser",
        "UserService.CreateUser"
      ],
      "reasonsForInclusion": [
        "Direct method call",
        "Method name similarity",
        "Type name similarity"
      ]
    },
    {
      "testName": "EndToEnd_UserRegistration_ShouldPersistUser", 
      "testClass": "UserIntegrationTests",
      "testNamespace": "MyApp.Tests.Integration",
      "assemblyPath": "MyApp.Integration.Tests.dll",
      "testMethod": "MyApp.Tests.Integration.UserIntegrationTests.EndToEnd_UserRegistration_ShouldPersistUser",
      "category": "Integration",
      "framework": "NUnit", 
      "confidence": 0.72,
      "callDepth": 3,
      "callPath": [
        "UserIntegrationTests.EndToEnd_UserRegistration_ShouldPersistUser",
        "UserController.RegisterUser", 
        "UserRegistrationService.RegisterNewUser",
        "UserService.CreateUser"
      ],
      "reasonsForInclusion": [
        "Transitive method call",
        "Method name similarity"
      ]
    },
    {
      "testName": "CreateUser_InvalidInput_ShouldThrow",
      "testClass": "UserServiceTests",
      "testNamespace": "MyApp.Tests.Services", 
      "assemblyPath": "MyApp.Tests.dll",
      "testMethod": "MyApp.Tests.Services.UserServiceTests.CreateUser_InvalidInput_ShouldThrow",
      "category": "Unit",
      "framework": "NUnit",
      "confidence": 0.95,
      "callDepth": 1,
      "callPath": [
        "UserServiceTests.CreateUser_InvalidInput_ShouldThrow",
        "UserService.CreateUser"
      ],
      "reasonsForInclusion": [
        "Direct method call",
        "Method name similarity",
        "Type name similarity"
      ]
    }
  ],
  "analysisStatistics": {
    "totalProjectsAnalyzed": 5,
    "totalTestMethodsScanned": 247,
    "totalProductionMethodsInCallGraph": 1342,
    "callGraphBuildTimeMs": 8450,
    "testDiscoveryTimeMs": 2100,
    "coverageMappingTimeMs": 3200,
    "totalAnalysisTimeMs": 15750
  },
  "confidenceDistribution": {
    "high": 6,
    "medium": 2, 
    "low": 0
  },
  "testCategoryBreakdown": {
    "Unit": 5,
    "Integration": 2,
    "EndToEnd": 1
  }
}
```

### Method Coverage Analysis (Text)

```
=== Method-to-Test Coverage Analysis ===
Method: MyApp.Services.UserService.CreateUser
Analysis completed: 2025-01-15 14:30:00 UTC

=== Coverage Summary ===
Tests Found: 8
High Confidence (≥80%): 6 tests
Medium Confidence (50-79%): 2 tests  
Low Confidence (<50%): 0 tests

=== Test Coverage Details ===

High Confidence Tests:
  [95%] UserServiceTests.CreateUser_WithValidInput_ShouldCreateUser
    Category: Unit | Framework: NUnit | Call Depth: 1
    Path: UserServiceTests.CreateUser_WithValidInput_ShouldCreateUser → UserService.CreateUser
    Reasons: Direct method call, Method name similarity, Type name similarity

  [95%] UserServiceTests.CreateUser_InvalidInput_ShouldThrow  
    Category: Unit | Framework: NUnit | Call Depth: 1
    Path: UserServiceTests.CreateUser_InvalidInput_ShouldThrow → UserService.CreateUser
    Reasons: Direct method call, Method name similarity, Type name similarity

  [89%] UserServiceTests.CreateUser_DuplicateEmail_ShouldThrow
    Category: Unit | Framework: NUnit | Call Depth: 1
    Path: UserServiceTests.CreateUser_DuplicateEmail_ShouldThrow → UserService.CreateUser
    Reasons: Direct method call, Method name similarity, Type name similarity

Medium Confidence Tests:
  [72%] UserIntegrationTests.EndToEnd_UserRegistration_ShouldPersistUser
    Category: Integration | Framework: NUnit | Call Depth: 3
    Path: UserIntegrationTests.EndToEnd_UserRegistration_ShouldPersistUser → 
          UserController.RegisterUser → UserRegistrationService.RegisterNewUser → 
          UserService.CreateUser
    Reasons: Transitive method call, Method name similarity

  [58%] UserWorkflowTests.CompleteUserJourney_ShouldWork
    Category: EndToEnd | Framework: NUnit | Call Depth: 4
    Path: UserWorkflowTests.CompleteUserJourney_ShouldWork → ... → UserService.CreateUser
    Reasons: Deep transitive call, Weak method correlation

=== Performance Statistics ===
Solution Projects: 5
Test Methods Scanned: 247
Call Graph Methods: 1,342
Analysis Time: 15.8 seconds
  - Call Graph Building: 8.5s (54%)
  - Test Discovery: 2.1s (13%) 
  - Coverage Mapping: 3.2s (20%)
  - Other Processing: 2.0s (13%)

=== Confidence Scoring Factors ===
• Direct method calls: +40 points
• Method name similarity: +25 points  
• Type name similarity: +20 points
• Namespace correlation: +10 points
• Call depth penalty: -5 points per hop
• Test framework bonus: +5 points (NUnit/xUnit/MSTest)
```

### Categorization Report (Text)

```
Test Categorization Report
Generated: 2025-01-15 10:30:00

Assembly: MyProject.Tests.dll
Framework: .NET 8.0
Total Tests: 156

┌─────────────┬───────┬─────────────────────────────────────┐
│ Category    │ Count │ Examples                            │
├─────────────┼───────┼─────────────────────────────────────┤
│ Unit        │    89 │ UserService_CreateUser_Success      │
│             │       │ ProductValidator_ValidatePrice      │
│             │       │ OrderCalculator_ComputeTotal        │
├─────────────┼───────┼─────────────────────────────────────┤
│ Integration │    45 │ UserWorkflow_EndToEnd               │
│             │       │ PaymentProcess_Integration          │
├─────────────┼───────┼─────────────────────────────────────┤
│ Database    │    15 │ UserRepository_DatabaseOperations   │
│             │       │ OrderData_PersistenceTests          │
├─────────────┼───────┼─────────────────────────────────────┤
│ API         │     7 │ UserController_HttpEndpoints        │
│             │       │ PaymentAPI_RestfulOperations        │
└─────────────┴───────┴─────────────────────────────────────┘
```

### Diff Impact Analysis Report (JSON)

```json
{
  "summary": {
    "totalChanges": 3,
    "totalFiles": 2,
    "totalMethods": 5,
    "totalImpactedTests": 12,
    "analyzedAt": "2025-01-15T14:30:00Z"
  },
  "codeChanges": [
    {
      "filePath": "src/UserService.cs",
      "changeType": "Modified",
      "changedMethods": ["CreateUser", "UpdateUser", "ValidateUser"],
      "changedTypes": ["UserService"],
      "detectedAt": "2025-01-15T14:29:45Z"
    },
    {
      "filePath": "src/Models/User.cs", 
      "changeType": "Added",
      "changedMethods": ["GetFullName"],
      "changedTypes": ["User"],
      "detectedAt": "2025-01-15T14:29:45Z"
    }
  ],
  "affectedMethods": [
    "UserService.CreateUser",
    "UserService.UpdateUser", 
    "UserService.ValidateUser",
    "User.GetFullName",
    "UserController.CreateUserEndpoint"
  ],
  "impactedTests": [
    {
      "id": "UserServiceTests.CreateUser_WithValidData_ShouldCreateUser",
      "methodName": "CreateUser_WithValidData_ShouldCreateUser",
      "typeName": "UserServiceTests",
      "namespace": "MyApp.Tests.Services",
      "assemblyPath": "MyApp.Tests.dll",
      "category": "Unit",
      "confidence": 0.95,
      "impactReasons": "Method name similarity; Type name similarity"
    },
    {
      "id": "UserIntegrationTests.CreateUser_EndToEnd_ShouldPersist",
      "methodName": "CreateUser_EndToEnd_ShouldPersist", 
      "typeName": "UserIntegrationTests",
      "namespace": "MyApp.Tests.Integration",
      "assemblyPath": "MyApp.Integration.Tests.dll",
      "category": "Integration",
      "confidence": 0.72,
      "impactReasons": "Method name similarity; Namespace similarity"
    }
  ]
}
```

### Test Selection Report (JSON)

```json
{
  "analyzedPath": "MyProject.Tests.dll",
  "timestamp": "2025-01-15T10:30:00Z",
  "confidenceLevel": "High",
  "changedFiles": ["src/UserService.cs", "src/PaymentController.cs"],
  "selectedTests": [
    {
      "testName": "UserService_CreateUser_Success",
      "category": "Unit",
      "selectionScore": 0.95,
      "estimatedDuration": "00:00:00.125",
      "assembly": "MyProject.Tests.dll",
      "tags": ["fast", "isolated"]
    },
    {
      "testName": "PaymentController_ProcessPayment_ValidCard",
      "category": "Integration", 
      "selectionScore": 0.87,
      "estimatedDuration": "00:00:02.350",
      "assembly": "MyProject.Tests.dll",
      "tags": ["payment", "integration"]
    }
  ],
  "summary": {
    "totalSelectedTests": 23,
    "estimatedTotalDuration": "00:02:15.750",
    "averageSelectionScore": 0.82,
    "categoryBreakdown": {
      "Unit": 15,
      "Integration": 6,
      "Database": 2
    },
    "optimalParallelism": 4
  }
}
```

## 🎛️ Configuration Options

### Confidence Levels

- **Fast** (~70% confidence, ≤30s execution time)
- **Medium** (~85% confidence, ≤5min execution time)  
- **High** (~95% confidence, ≤15min execution time)
- **Full** (~99% confidence, ≤1hr execution time)

### Test Categories

- **Unit**: Isolated tests with no external dependencies
- **Integration**: Tests involving multiple components
- **Database**: Tests requiring database operations
- **API**: Tests for HTTP endpoints and web services
- **UI**: User interface and browser-based tests
- **EndToEnd**: Complete workflow tests
- **Performance**: Load and performance tests
- **Security**: Security and vulnerability tests

## 🔍 Method-to-Test Reverse Lookup (NEW!)

The `find-tests` command performs reverse lookup analysis to find all tests that exercise a specific production method. This powerful feature uses advanced call graph analysis and semantic modeling to trace execution paths from tests to your production code.

### Key Features

- **Deep Call Graph Analysis**: Traces method calls across multiple layers of abstraction
- **Semantic Symbol Resolution**: Uses Roslyn analyzers for accurate method identification
- **Confidence Scoring**: Multi-factor scoring based on call depth, method naming, and test patterns
- **Test Classification**: Advanced heuristics to identify and categorize different test types
- **Cross-Assembly Analysis**: Finds tests even when they're in different assemblies than the production code
- **Performance Optimized**: Efficient BFS traversal with caching for large codebases

### Use Cases

- **Refactoring Safety**: Find all tests that might break when changing a method
- **Test Gap Analysis**: Identify production methods with insufficient test coverage
- **Impact Analysis**: Understand the testing blast radius of code changes
- **Code Review**: Verify that new methods have appropriate test coverage
- **Legacy Code**: Discover existing tests for undocumented legacy methods

### Command Options

```bash
# Basic method lookup
test-intel find-tests --method "MethodName" --solution "Solution.sln"

# Full namespace.class.method specification
test-intel find-tests --method "MyApp.Services.UserService.CreateUser" --solution "MyApp.sln"

# With verbose logging to see analysis process
test-intel find-tests --method "PaymentService.ProcessPayment" --solution "MyApp.sln" --verbose

# JSON output for automation
test-intel find-tests \
  --method "OrderService.CalculateTotal" \
  --solution "MyApp.sln" \
  --format json \
  --output coverage-analysis.json
```

### Analysis Process

The find-tests command performs the following analysis:

1. **Solution Parsing**: Parses .sln file and loads all C# projects
2. **Dependency Graph Construction**: Builds project compilation order and dependencies
3. **MSBuild Workspace Creation**: Creates Roslyn workspace for semantic analysis
4. **Symbol Resolution**: Resolves the target method using semantic models
5. **Call Graph Building**: Constructs comprehensive method call graph using syntax tree analysis
6. **Test Discovery**: Identifies test methods using naming patterns and framework attributes
7. **Coverage Mapping**: Maps test methods to production methods via call graph traversal
8. **Confidence Scoring**: Calculates confidence scores based on multiple factors
9. **Results Ranking**: Sorts results by confidence and provides detailed reasoning

## 🎯 Git Diff Analysis (NEW!)

TestIntelligence can now analyze git diffs, patch files, or SVN patches to determine which tests are likely impacted by your changes. This is perfect for:

- **Pull Request Analysis**: Automatically determine which tests to run based on PR changes
- **Pre-commit Hooks**: Run only tests affected by your local changes

## 🌐 RESTful API for AI Agent Integration (NEW!)

TestIntelligence now includes a production-ready RESTful API designed for AI agent integration, providing programmatic access to all intelligent test selection capabilities.

### API Features

- **Multi-factor scoring algorithms** with configurable weights
- **Confidence-based test selection** (Fast/Medium/High/Full strategies)
- **Real-time test impact analysis** from git diffs
- **Machine learning from execution history** to improve recommendations
- **Batch optimization** for parallel test execution
- **OpenAPI/Swagger documentation** with interactive testing
- **CORS enabled** for web-based AI agents

### Starting the API Server

```bash
# Start the API server (default ports 5000/5001)
dotnet run --project src/TestIntelligence.API/

# API will be available at:
# - HTTP: http://localhost:5000
# - HTTPS: https://localhost:5001  
# - Swagger UI: https://localhost:5001/swagger
```

### API Endpoints

#### Test Selection API

```bash
# Get optimal test plan based on code changes
POST /api/testselection/plan
Content-Type: application/json
{
  "codeChanges": {
    "changes": [
      {
        "filePath": "src/UserService.cs",
        "changeType": "Modified", 
        "changedMethods": ["CreateUser", "UpdateUser"],
        "changedTypes": ["UserService"]
      }
    ]
  },
  "confidenceLevel": "Medium",
  "maxTests": 100,
  "maxExecutionTime": "00:05:00"
}

# Analyze git diff and get test recommendations  
POST /api/testselection/analyze-diff
Content-Type: application/json
{
  "solutionPath": "/path/to/solution.sln",
  "diffContent": "diff --git a/src/UserService.cs b/src/UserService.cs\n+added line",
  "confidenceLevel": "High"
}

# Update test execution history for ML learning
POST /api/testselection/execution-results
Content-Type: application/json
[
  {
    "testName": "UserTests.CreateUser_ShouldSucceed",
    "duration": "00:00:01.500", 
    "passed": true,
    "executedAt": "2024-01-15T10:30:00Z",
    "errorMessage": null
  }
]

# Get test execution history and statistics
GET /api/testselection/history?filter=UserTests

# NEW: Find tests that exercise specific methods
POST /api/testselection/find-tests
Content-Type: application/json
{
  "methodId": "MyApp.Services.UserService.CreateUser",
  "solutionPath": "/path/to/solution.sln",
  "includeTransitiveCalls": true,
  "minimumConfidence": 0.5
}
```

#### Test Discovery API

```bash
# Discover tests in assemblies or solutions
POST /api/testdiscovery/discover
Content-Type: application/json
{
  "path": "/path/to/tests.dll",
  "includeDetailedAnalysis": true,
  "categoryFilter": ["Unit", "Integration"]
}

# Get available test categories and descriptions  
GET /api/testdiscovery/categories

# API health check and feature list
GET /api/testdiscovery/health
```

### AI Agent Integration Example

```bash
# Example: Get test recommendations for a PR
curl -X POST https://localhost:5001/api/testselection/analyze-diff \
  -H "Content-Type: application/json" \
  -d '{
    "solutionPath": "/workspace/MyApp.sln",
    "diffContent": "'"$(git diff origin/main)"'",
    "confidenceLevel": "Medium"
  }' | jq '.recommendedTests.tests[].testName'
```

### Response Examples

**Test Selection Response:**
```json
{
  "tests": [
    {
      "testName": "UserServiceTests.CreateUser_ValidInput_ShouldSucceed",
      "category": "Unit",
      "selectionScore": 0.95,
      "averageExecutionTime": "00:00:00.125",
      "assemblyName": "MyApp.Tests"
    }
  ],
  "confidenceLevel": "Medium", 
  "estimatedDuration": "00:02:15.500",
  "description": "Selected 23 tests based on Medium confidence level"
}
```

**Diff Analysis Response:**
```json
{
  "changeSet": {
    "changes": [
      {
        "filePath": "src/UserService.cs",
        "changeType": "Modified",
        "changedMethods": ["CreateUser"],
        "changedTypes": ["UserService"]
      }
    ]
  },
  "recommendedTests": { /* TestExecutionPlan */ },
  "totalChanges": 1,
  "impactScore": 0.72,
  "analysisTimestamp": "2024-01-15T14:30:00Z"
}
```  
- **CI/CD Optimization**: Intelligent test selection based on git history
- **Code Review**: Understand test impact before merging changes

### Diff Analysis Options

#### 1. Git Command Integration
```bash
# Analyze changes between commits
test-intel diff --solution MySolution.sln --git-command "diff HEAD~1"

# Analyze staged changes
test-intel diff --solution MySolution.sln --git-command "diff --cached"

# Analyze changes in a specific branch
test-intel diff --solution MySolution.sln --git-command "diff main..feature-branch"

# Compare with main branch
test-intel diff --solution MySolution.sln --git-command "diff origin/main"
```

#### 2. Diff File Analysis
```bash
# From a saved patch file
git diff HEAD~3 > changes.patch
test-intel diff --solution MySolution.sln --diff-file changes.patch --verbose

# From SVN patch
svn diff > svn-changes.patch  
test-intel diff --solution MySolution.sln --diff-file svn-changes.patch
```

#### 3. Direct Diff Content
```bash
# Pass diff content directly (useful in scripts)
DIFF_CONTENT=$(git diff HEAD~1)
test-intel diff --solution MySolution.sln --diff-content "$DIFF_CONTENT" --format json
```

### Analysis Output

The diff analysis provides different confidence levels for impacted tests:

- **High Confidence (≥70%)**: Direct method/class name matches
- **Medium Confidence (40-69%)**: Related types or file patterns  
- **Low Confidence (<40%)**: Weak namespace or assembly correlations

### Text Output Example
```
=== Test Impact Analysis Results ===
Analyzed at: 2025-01-15 14:30:00 UTC

=== Summary ===
Code Changes: 3 changes across 2 files
Changed Methods: 5
Potentially Impacted Tests: 12

High Confidence (≥70%):
  [95%] UserServiceTests.CreateUser_WithValidData_ShouldCreateUser
    Reasons: Method name similarity; Type name similarity
  [87%] UserServiceTests.UpdateUser_WithValidData_ShouldUpdate
    Reasons: Method name similarity; Type name similarity
  [78%] UserValidationTests.ValidateUser_WithRules_ShouldSucceed
    Reasons: Method name similarity

Medium Confidence (40-69%):
  [65%] UserControllerTests.CreateUser_ValidRequest_Returns201
    Reasons: Type name similarity; Related file changes
  [58%] UserIntegrationTests.UserWorkflow_EndToEnd
    Reasons: Namespace similarity; Related file changes

=== Code Changes ===
[Modified] src/UserService.cs
  Methods: CreateUser, UpdateUser, ValidateUser
  Types: UserService
[Added] src/Models/User.cs  
  Methods: GetFullName
  Types: User
```

## 🔄 CI/CD Integration

### GitHub Actions

```yaml
- name: Intelligent Test Selection (Traditional)
  run: |
    # Get changed files from PR
    CHANGED_FILES=$(git diff --name-only HEAD~1 HEAD | grep '\.cs$' | tr '\n' ',' | sed 's/,$//')
    
    # Select optimal tests
    test-intel select \
      --path "tests/MyProject.Tests/bin/Release/MyProject.Tests.dll" \
      --changes "$CHANGED_FILES" \
      --confidence Medium \
      --max-time "5m" \
      --output test-selection.json
    
    # Run selected tests
    dotnet test --filter "$(jq -r '.selectedTests[].testName' test-selection.json | tr '\n' '|' | sed 's/|$//')"

- name: Diff-Based Test Analysis (NEW!)
  run: |
    # Analyze git diff for test impact
    test-intel diff \
      --solution "MySolution.sln" \
      --git-command "diff ${{ github.event.pull_request.base.sha }}" \
      --format json \
      --output diff-impact.json
    
    # Extract high-confidence tests  
    HIGH_CONFIDENCE_TESTS=$(jq -r '.impactedTests[] | select(.confidence >= 0.7) | .methodName' diff-impact.json | tr '\n' '|' | sed 's/|$//')
    
    # Run high-confidence tests first for fast feedback
    if [ ! -z "$HIGH_CONFIDENCE_TESTS" ]; then
      echo "Running high-confidence impacted tests..."
      dotnet test --filter "FullyQualifiedName~$HIGH_CONFIDENCE_TESTS" --logger trx
    fi
    
    # Upload analysis results as artifact
    - name: Upload Test Impact Analysis
      uses: actions/upload-artifact@v3
      with:
        name: test-impact-analysis
        path: diff-impact.json

- name: Method Coverage Analysis (NEW!)
  run: |
    # Analyze test coverage for critical methods
    CRITICAL_METHODS=(
      "PaymentService.ProcessPayment"
      "UserService.CreateUser" 
      "OrderService.CalculateTotal"
    )
    
    for method in "${CRITICAL_METHODS[@]}"; do
      echo "Analyzing test coverage for: $method"
      test-intel find-tests \
        --method "$method" \
        --solution "MySolution.sln" \
        --format json \
        --output "coverage-${method//\./-}.json"
      
      # Check if method has sufficient test coverage
      TEST_COUNT=$(jq '.testsFound' "coverage-${method//\./-}.json")
      if [ "$TEST_COUNT" -lt 3 ]; then
        echo "⚠️  WARNING: Method $method has only $TEST_COUNT tests (minimum: 3)"
        echo "method-coverage-warning=true" >> $GITHUB_ENV
      fi
    done
    
    # Combine all coverage reports
    jq -s '.' coverage-*.json > combined-coverage-analysis.json
```

### Azure DevOps

```yaml
- task: DotNetCoreCLI@2
  displayName: 'Select Tests Based on Changes'
  inputs:
    command: 'custom'
    custom: 'tool'
    arguments: 'run test-intel select --path $(testAssemblyPath) --changes $(Build.SourcesDirectory)/changed-files.txt --confidence High --output selected-tests.json'
```

## 🏗️ Architecture

TestIntelligence consists of several interconnected components:

```
┌─────────────────────────────────────────────────────────────────┐
│                    TestIntelligence.CLI                         │
│                   (Global Tool Interface)                       │
└─────────────────────┬───────────────────────────────────────────┘
                      │
┌─────────────────────▼───────────────────────────────────────────┐
│                TestIntelligence.SelectionEngine                 │
│            (AI-Driven Test Selection Logic)                     │
└─────┬─────────────────────────┬─────────────────────────────────┘
      │                         │
┌─────▼──────────────────┐ ┌────▼────────────────────────────────┐
│ TestIntelligence.      │ │    TestIntelligence.DataTracker     │
│    ImpactAnalyzer      │ │   (EF6/EF Core Pattern Detection)  │
│ (Roslyn Analysis)      │ │                                     │
└─────┬──────────────────┘ └─────────────┬───────────────────────┘
      │                                  │
┌─────▼──────────────────────────────────▼───────────────────────┐
│                  TestIntelligence.Core                         │
│        (Cross-Framework Assembly Loading & Discovery)          │
└─────────────────────────────────────────────────────────────────┘
```

### Core Components

1. **TestIntelligence.Core**: Cross-framework assembly loading and test discovery
2. **TestIntelligence.ImpactAnalyzer**: Roslyn-based code analysis and dependency tracking
3. **TestIntelligence.DataTracker**: EF6/EF Core pattern detection for data conflicts
4. **TestIntelligence.SelectionEngine**: AI algorithms for intelligent test selection
5. **TestIntelligence.CLI**: Command-line interface for automation

### Framework Adapters

- **TestIntelligence.Framework48Adapter**: Optimized for .NET Framework 4.8
- **TestIntelligence.NetCoreAdapter**: Optimized for .NET Core/.NET 5+

## 🧪 Testing

TestIntelligence has comprehensive test coverage with **271 passing tests**:

```bash
# Run all tests
dotnet test

# Run specific test suites  
dotnet test tests/TestIntelligence.Core.Tests/          # 62 tests
dotnet test tests/TestIntelligence.DataTracker.Tests/   # 119 tests
dotnet test tests/TestIntelligence.ImpactAnalyzer.Tests/ # 34 tests
dotnet test tests/TestIntelligence.SelectionEngine.Tests/ # 56 tests
```

## 📈 Performance Benefits

TestIntelligence can significantly reduce CI/CD execution times:

- **Fast Confidence**: ~70% reduction in test execution time
- **Medium Confidence**: ~50% reduction while maintaining 85% coverage
- **High Confidence**: ~30% reduction with 95% confidence
- **Smart Parallelization**: Automatic detection of data conflicts prevents race conditions

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/amazing-feature`
3. Commit your changes: `git commit -m 'Add amazing feature'`
4. Push to the branch: `git push origin feature/amazing-feature`
5. Open a Pull Request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙋 Support

- **Documentation**: [Wiki](https://github.com/TestIntelligence/TestIntelligence/wiki)
- **Issues**: [GitHub Issues](https://github.com/TestIntelligence/TestIntelligence/issues)
- **Discussions**: [GitHub Discussions](https://github.com/TestIntelligence/TestIntelligence/discussions)

---

**Built with ❤️ for the .NET community**

*TestIntelligence helps you run smarter tests, not harder tests.*