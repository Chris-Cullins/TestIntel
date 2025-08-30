# TestIntelligence

**Intelligent test analysis and selection library for .NET applications**

TestIntelligence is a comprehensive solution that analyzes your test suites to provide intelligent test selection, categorization, and impact analysis. Reduce CI/CD execution times while maintaining high confidence in test coverage through AI-driven test selection algorithms.

## 🚀 Features

- **Cross-Framework Support**: Works with .NET Framework 4.8, .NET Core, and .NET 5+
- **Intelligent Test Selection**: AI-driven algorithms select optimal tests based on code changes
- **Test Categorization**: Automatically categorizes tests (Unit, Integration, Database, API, UI, etc.)
- **Impact Analysis**: Advanced Roslyn-based analysis of code dependencies and test impact
- **Data Conflict Detection**: Identifies tests that cannot run in parallel due to shared data
- **Multiple Test Frameworks**: Supports NUnit, MSTest, and xUnit
- **CLI Tool**: Command-line interface for CI/CD integration
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

## 🎯 Git Diff Analysis (NEW!)

TestIntelligence can now analyze git diffs, patch files, or SVN patches to determine which tests are likely impacted by your changes. This is perfect for:

- **Pull Request Analysis**: Automatically determine which tests to run based on PR changes
- **Pre-commit Hooks**: Run only tests affected by your local changes  
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