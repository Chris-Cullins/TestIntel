# TestIntelligence

**Smart test selection for .NET - Run only the tests that matter** 🎯

TestIntelligence analyzes your codebase to intelligently select which tests to run based on code changes, dramatically reducing CI/CD execution times while maintaining confidence in test coverage.

## 🚀 Quick Start

### Installation
```bash
dotnet pack src/TestIntelligence.CLI/ -o nupkg
dotnet tool install -g --add-source ./nupkg TestIntelligence.CLI
```

### Essential Commands
```bash
# 🏗️ Setup caching for 90% performance boost (run once)
test-intel cache --solution MySolution.sln --action init
test-intel cache --solution MySolution.sln --action warm-up

# 🔍 Find tests for a method
test-intel find-tests --method "UserService.CreateUser" --solution MySolution.sln

# 📊 Analyze git changes
test-intel diff --solution MySolution.sln --git-command "diff HEAD~1"

# ⚡ Smart test selection
test-intel select --path MyProject.Tests.dll --changes "src/UserService.cs" --confidence Medium

# 📈 Coverage analysis
test-intel analyze-coverage --solution MySolution.sln --tests "*.Tests.*" --git-command "diff HEAD~1"
```

## ✨ Key Features

- **🎯 Smart Selection** - Only run tests affected by your changes
- **📊 Impact Analysis** - Understand which tests are affected by code changes  
- **🔍 Method Coverage** - Find all tests that exercise specific methods
- **📈 Coverage Analysis** - Analyze how well tests cover your changes
- **🏷️ Auto-Categorization** - Classify tests as Unit, Integration, Database, API
- **⚡ Performance Boost** - Up to 90% faster with intelligent caching
- **🔧 Multi-Framework** - NUnit, xUnit, MSTest across .NET Framework 4.8 to .NET 8+

## Configuration

TestIntelligence supports project-specific configuration files to customize analysis behavior. Place a `testintel.config` file in the same directory as your solution file for automatic pickup.

### Creating Configuration Files

Generate a default configuration file:

```bash
# Create config in current directory
test-intel config init

# Create config in specific directory/solution
test-intel config init --path /path/to/MySolution.sln
```

### Configuration File Format

The `testintel.config` file uses JSON format with comments:

```json
{
  // TestIntelligence Configuration
  // This file controls which projects are analyzed and how analysis is performed
  
  "projects": {
    // Include specific project patterns (empty = include all)
    "include": [],
    
    // Exclude specific project patterns (wildcards supported)
    "exclude": [
      "**/obj/**",
      "**/bin/**", 
      "*.Integration.Tests*",
      "*ORM*",
      "*Database*"
    ],
    
    // Exclude projects by type/purpose
    "excludeTypes": [
      "orm",
      "database", 
      "migration"
    ],
    
    // Only analyze test projects (recommended)
    "testProjectsOnly": true
  },
  
  "analysis": {
    // Enable verbose logging by default
    "verbose": false,
    
    // Maximum parallel analysis operations
    "maxParallelism": 8,
    
    // Timeout for individual project analysis (seconds)
    "timeoutSeconds": 300
  },
  
  "output": {
    // Default output format (text or json)
    "format": "text",
    
    // Default output directory (null = current directory)
    "outputDirectory": null
  }
}
```

### Project Filtering

Filter which projects are included in analysis:

**Include Patterns**: Only analyze projects matching these patterns
```json
"include": ["*Core*", "*Services*"]
```

**Exclude Patterns**: Skip projects matching these patterns (takes precedence over include)
```json
"exclude": ["*Integration*", "*ORM*", "*Migration*", "**/bin/**"]
```

**Exclude by Type**: Skip projects based on their purpose/content
```json
"excludeTypes": ["orm", "database", "migration", "integration"]
```

Supported project types:
- `orm`: Entity Framework, Dapper ORM projects
- `database`: SQL, database-related projects  
- `migration`: Database migration projects
- `integration`: Integration test projects
- `api`: Web API, REST API projects
- `ui`: User interface, web client projects

### Wildcard Patterns

TestIntelligence supports standard wildcard patterns:
- `*`: Matches any number of characters
- `?`: Matches a single character  
- `**/`: Matches any number of directories
- `**/*.Tests*`: Matches any test project in any subdirectory

### Configuration Priority

Configuration values are applied in order of precedence:
1. **Command-line arguments** (highest priority)
2. **Configuration file settings**
3. **Default values** (lowest priority)

### Examples

**Focus on Core Projects Only**:
```json
{
  "projects": {
    "include": ["*Core*", "*Services*", "*Domain*"],
    "testProjectsOnly": false
  }
}
```

**Exclude Heavy Dependencies**:
```json
{
  "projects": {
    "exclude": ["*EntityFramework*", "*ORM*", "*Migration*", "*Integration*"],
    "excludeTypes": ["orm", "database", "integration"]
  }
}
```

**Performance-Focused Analysis**:
```json
{
  "analysis": {
    "maxParallelism": 4,
    "timeoutSeconds": 180
  },
  "projects": {
    "exclude": ["**/node_modules/**", "**/wwwroot/**"]
  }
}
```

All CLI commands automatically detect and use configuration files in the solution directory. This makes it easy to customize TestIntelligence behavior per project without affecting global settings.

## 📖 Core Commands

> All commands support `--verbose` for detailed output and `--format json` for machine-readable output.

| Command | Purpose | Example |
|---------|---------|---------|
| **`find-tests`** | Find tests that exercise a method | `test-intel find-tests --method "UserService.CreateUser" --solution MySolution.sln` |
| **`diff`** | Analyze git changes for test impact | `test-intel diff --solution MySolution.sln --git-command "diff HEAD~1"` |
| **`select`** | Smart test selection with confidence levels | `test-intel select --path MyProject.Tests.dll --changes "src/UserService.cs" --confidence Medium` |
| **`analyze-coverage`** | Coverage analysis for code changes | `test-intel analyze-coverage --solution MySolution.sln --tests "*.Tests.*" --git-command "diff HEAD~1"` |

### Confidence Levels
- **`Fast`** - ~70% confidence, ≤30s execution
- **`Medium`** - ~85% confidence, ≤5min execution  
- **`High`** - ~95% confidence, ≤15min execution
- **`Full`** - ~99% confidence, ≤1hr execution

## 🛠️ Additional Commands

<details>
<summary><strong>categorize</strong> - Auto-classify tests (Unit, Integration, Database, API, etc.)</summary>

```bash
test-intel categorize --path MyProject.Tests.dll --output categories.txt
```
</details>

<details>
<summary><strong>callgraph</strong> - Build method dependency graphs</summary>

```bash
test-intel callgraph --path MySolution.sln --format json --output callgraph.json
```
</details>

<details>
<summary><strong>trace-execution</strong> - Trace production code executed by tests</summary>

```bash
test-intel trace-execution --test "MyProject.Tests.UserServiceTests.CreateUser" --solution MySolution.sln
```
</details>

<details>
<summary><strong>cache</strong> - Manage persistent caching (90% performance boost)</summary>

```bash
# 1. Initialize cache directories and structure (quick)
test-intel cache --solution MySolution.sln --action init

# 2. Populate caches with actual data (slower, but only needed once)
test-intel cache --solution MySolution.sln --action warm-up

# Check cache status and statistics
test-intel cache --solution MySolution.sln --action status

# Clear all cached data
test-intel cache --solution MySolution.sln --action clear
```

**Cache Workflow**:
- **`init`** - Creates cache directory structure (empty caches, ~1 second)
- **`warm-up`** - Analyzes solution and populates caches with data (~30 seconds - 5 minutes)
- **`status`** - Shows cache statistics and entry counts
- **Subsequent runs** - Use populated caches for 90% performance boost

⚠️ **Important**: Run `warm-up` after `init` to populate caches with actual data.
</details>

<details>
<summary><strong>config</strong> - Manage project configuration</summary>

```bash
test-intel config init --path MySolution.sln
```
</details>

## 🌐 API & Integration

### RESTful API
```bash
# Start API server
dotnet run --project src/TestIntelligence.API/

# Available at: https://localhost:5001/swagger
```

### CI/CD Integration

<details>
<summary><strong>GitHub Actions Example</strong></summary>

```yaml
- name: Smart Test Selection
  run: |
    test-intel diff --solution "MySolution.sln" --git-command "diff ${{ github.event.pull_request.base.sha }}" --format json --output impact.json
    HIGH_CONFIDENCE_TESTS=$(jq -r '.impactedTests[] | select(.confidence >= 0.7) | .methodName' impact.json | tr '\n' '|')
    dotnet test --filter "FullyQualifiedName~$HIGH_CONFIDENCE_TESTS"
```
</details>

<details>
<summary><strong>Programmatic Usage</strong></summary>

```csharp
using TestIntelligence.SelectionEngine.Engine;

var engine = new TestSelectionEngine();
var changes = new List<CodeChange> { /* your changes */ };
var changeSet = new CodeChangeSet(changes);
var testPlan = await engine.GetOptimalTestPlanAsync(changeSet, ConfidenceLevel.High);
```
</details>

## 📊 Performance

| Confidence Level | Time Reduction | Coverage Confidence | Use Case |
|------------------|----------------|-------------------|-----------|
| **Fast** | ~70% | ~70% | Quick feedback loops |
| **Medium** | ~50% | ~85% | Regular development |
| **High** | ~30% | ~95% | Pre-merge validation |
| **Full** | 0% | ~99% | Critical releases |

**Caching Benefits**: Up to 90% faster on subsequent runs for large solutions (40+ projects)

## 🏗️ Architecture

| Component | Purpose |
|-----------|---------|
| **Core** | Assembly loading, test discovery |
| **ImpactAnalyzer** | Roslyn-based code analysis |
| **SelectionEngine** | AI-driven test selection |
| **CLI** | Command-line interface |
| **API** | RESTful endpoints |

## 🚀 Development

```bash
# Build and test
dotnet build
dotnet test

# Run specific test suites  
dotnet test tests/TestIntelligence.Core.Tests/
```

---

## 📚 Detailed Documentation

<details>
<summary><strong>Advanced Configuration Examples</strong></summary>

### Full Configuration File (`testintel.config`)
```json
{
  "projects": {
    "include": ["*Core*", "*Services*"],
    "exclude": ["*Integration*", "*ORM*", "**/bin/**"],
    "excludeTypes": ["orm", "database", "migration"],
    "testProjectsOnly": true
  },
  "analysis": {
    "verbose": false,
    "maxParallelism": 8,
    "timeoutSeconds": 300
  },
  "output": {
    "format": "text",
    "outputDirectory": null
  }
}
```

### CI/CD Integration Examples
```yaml
# Complete GitHub Actions workflow
- name: Smart Test Selection with Coverage Validation
  run: |
    test-intel analyze-coverage \
      --solution "MySolution.sln" \
      --tests "MyProject.Tests.*" \
      --git-command "diff ${{ github.event.pull_request.base.sha }}" \
      --format json --output pr-coverage.json
    
    COVERAGE=$(jq '.summary.coveragePercentage' pr-coverage.json)
    if (( $(echo "$COVERAGE < 80" | bc -l) )); then
      echo "❌ Coverage ${COVERAGE}% below 80% threshold"
      exit 1
    fi
```
</details>

<details>
<summary><strong>Sample Outputs</strong></summary>

### Method Coverage Analysis
```
Found 8 tests for UserService.CreateUser:
  [95%] UserServiceTests.CreateUser_WithValidData (Unit, Direct call)
  [87%] UserServiceTests.UpdateUser_CallsCreate (Unit, Indirect call)
  [65%] UserControllerTests.CreateUser_Returns201 (Integration, API layer)
```

### Test Impact Analysis  
```
Code Changes: 3 files, 5 methods modified
High Confidence Tests (≥70%): 4 tests
Medium Confidence Tests (40-69%): 8 tests
Estimated execution time: 2m 30s (vs 12m full suite)
```
</details>

<details>
<summary><strong>Performance & Caching</strong></summary>

### Cache Architecture & Performance
TestIntelligence uses a multi-tier caching system for maximum performance:

#### Cache Types Built During Warm-Up
- **Call Graph Cache** - Method dependency mappings, transitive relationships
- **Project Cache** - Assembly metadata, test discovery results
- **Roslyn Cache** - Parsed syntax trees, semantic analysis
- **Assembly Cache** - Loaded assemblies, reflection metadata

#### Performance Characteristics
- **Cache initialization (`init`)**: ~1 second (directory structure only)
- **Cache warm-up (`warm-up`)**: 30 seconds - 5 minutes (builds all data)  
- **Subsequent runs**: 5-15 seconds (90% faster with populated caches)
- **Storage**: 200-500MB per solution
- **Cleanup**: Automatic based on solution size and age

#### Cache Workflow Examples
```bash
# Initial setup (run once per solution)
test-intel cache --solution MySolution.sln --action init
test-intel cache --solution MySolution.sln --action warm-up --verbose

# Check what was built
test-intel cache --solution MySolution.sln --action status --format json

# Regular usage (fast with populated caches)
test-intel find-tests --method "UserService.CreateUser" --solution MySolution.sln

# Maintenance
test-intel cache --solution MySolution.sln --action clear  # if needed
```

🚀 **Pro Tip**: Run warm-up during CI setup or overnight for best performance during development.
</details>

## 📄 License

MIT License - see [LICENSE](LICENSE) file for details.

---

*TestIntelligence: Run smarter tests, not harder tests.* ⚡