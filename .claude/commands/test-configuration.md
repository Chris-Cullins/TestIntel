# Test Configuration System

Instructions for Claude to test the configuration system's project filtering capabilities through manual verification of different scenarios.

## Testing Protocol

### Step 1: Environment Setup
1. Create a temporary directory for testing configurations
2. Backup any existing configuration if present
3. Initialize a clean configuration structure for testing

```bash
# Create test environment
mkdir -p /tmp/config-test/
cd /tmp/config-test/

# Initialize base configuration
dotnet run --project src/TestIntelligence.CLI config init --path ./
```

### Step 2: Test Scenarios

#### Scenario 1: Default Configuration
Test basic functionality with minimal configuration.

**Configuration**: `scenario-1-default.json`
```json
{
  "version": "1.0",
  "projects": {
    "include": ["*"],
    "exclude": []
  },
  "testSettings": {
    "timeout": 30000,
    "parallel": true
  }
}
```

**Verification Command**:
```bash
dotnet run --project src/TestIntelligence.CLI config verify \
  --config scenario-1-default.json \
  --solution TestIntelligence.sln \
  --format text --verbose
```

#### Scenario 2: Core Projects Only
Filter to include only core functionality projects.

**Configuration**: `scenario-2-core-only.json`
```json
{
  "version": "1.0",
  "projects": {
    "include": ["*Core*", "*TestIntelligence.CLI*"],
    "exclude": ["*Tests*", "*Test*"]
  },
  "testSettings": {
    "timeout": 30000,
    "parallel": true
  }
}
```

**Verification Command**:
```bash
dotnet run --project src/TestIntelligence.CLI config verify \
  --config scenario-2-core-only.json \
  --solution TestIntelligence.sln \
  --format json --output core-projects.json \
  --verbose
```

#### Scenario 3: Exclude Heavy Dependencies
Filter out projects with heavy external dependencies.

**Configuration**: `scenario-3-exclude-heavy.json`
```json
{
  "version": "1.0",
  "projects": {
    "include": ["*"],
    "exclude": ["*EntityFramework*", "*Database*", "*Reporting*", "*Tests*"]
  },
  "testSettings": {
    "timeout": 15000,
    "parallel": false
  }
}
```

**Verification Command**:
```bash
dotnet run --project src/TestIntelligence.CLI config verify \
  --config scenario-3-exclude-heavy.json \
  --solution TestIntelligence.sln \
  --format text --verbose
```

#### Scenario 4: Performance-Focused Filtering
Configuration optimized for performance testing scenarios.

**Configuration**: `scenario-4-performance.json`
```json
{
  "version": "1.0",
  "projects": {
    "include": ["*Core*", "*SelectionEngine*", "*ImpactAnalyzer*"],
    "exclude": ["*Tests*", "*Test*", "*Mock*", "*Stub*"]
  },
  "testSettings": {
    "timeout": 60000,
    "parallel": true,
    "maxDegreeOfParallelism": 4
  }
}
```

**Verification Command**:
```bash
dotnet run --project src/TestIntelligence.CLI config verify \
  --config scenario-4-performance.json \
  --solution TestIntelligence.sln \
  --format json --output performance-config.json \
  --verbose
```

#### Scenario 5: Custom Wildcard Patterns
Test complex wildcard pattern matching.

**Configuration**: `scenario-5-wildcards.json`
```json
{
  "version": "1.0",
  "projects": {
    "include": ["TestIntelligence.*.Tests", "*Categorizer*", "*DataTracker*"],
    "exclude": ["*Integration*", "*E2E*"]
  },
  "testSettings": {
    "timeout": 45000,
    "parallel": true
  }
}
```

**Verification Command**:
```bash
dotnet run --project src/TestIntelligence.CLI config verify \
  --config scenario-5-wildcards.json \
  --solution TestIntelligence.sln \
  --format text --verbose
```

### Step 3: Manual Verification Process

For each scenario:

1. **Run the CLI Command**: Execute the `config verify` command and capture output
2. **Project Discovery**: Use helper commands to discover actual projects in the solution
3. **Pattern Matching**: Verify that include/exclude patterns work as expected
4. **Cross-Reference**: Compare CLI output with actual project structure
5. **Edge Case Testing**: Test with empty patterns, conflicting rules, and invalid patterns

### Step 4: Helper Commands for Verification

```bash
# Discover all projects in the solution
find . -name "*.csproj" -not -path "./bin/*" -not -path "./obj/*"

# List projects by type/pattern
find . -name "*.csproj" | grep -i "core"
find . -name "*.csproj" | grep -i "test"
find . -name "*.csproj" | grep -i "service"

# Check specific pattern matches
find . -name "*.csproj" | grep -E "TestIntelligence\..*\.Tests"

# Count projects by category
echo "Total projects: $(find . -name "*.csproj" | wc -l)"
echo "Test projects: $(find . -name "*.csproj" | grep -i test | wc -l)"
echo "Core projects: $(find . -name "*.csproj" | grep -i core | wc -l)"

# Examine solution file directly
grep -E "\.csproj" TestIntelligence.sln

# Test configuration creation
dotnet run --project src/TestIntelligence.CLI config create \
  --template basic \
  --output test-config.json

# Validate configuration syntax
dotnet run --project src/TestIntelligence.CLI config validate \
  --config test-config.json
```

### Step 5: Edge Case Testing

Test the following edge cases:

1. **Empty Patterns**: Configuration with empty include/exclude arrays
2. **Conflicting Rules**: Include pattern that conflicts with exclude pattern
3. **Invalid Wildcards**: Malformed wildcard patterns
4. **Non-existent Projects**: Patterns that match no projects
5. **Case Sensitivity**: Test pattern matching with different cases
6. **Special Characters**: Projects with special characters in names

```bash
# Edge case configuration examples
echo '{
  "version": "1.0",
  "projects": {
    "include": [],
    "exclude": []
  }
}' > edge-case-empty.json

echo '{
  "version": "1.0",
  "projects": {
    "include": ["*Core*"],
    "exclude": ["*Core*"]
  }
}' > edge-case-conflict.json

# Test edge cases
dotnet run --project src/TestIntelligence.CLI config verify \
  --config edge-case-empty.json \
  --solution TestIntelligence.sln \
  --format text
```

### Example Verification Report Format

```
## Configuration Testing Report

**Scenario**: Core Projects Only (scenario-2-core-only.json)

**CLI Output Summary**:
- Included Projects: 3
- Excluded Projects: 8
- Total Projects Scanned: 11

**Manual Verification**:
✅ TestIntelligence.Core.csproj
   - Matches pattern: *Core*
   - Correctly included

✅ TestIntelligence.CLI.csproj  
   - Matches pattern: *TestIntelligence.CLI*
   - Correctly included

✅ TestIntelligence.Core.Tests.csproj
   - Matches exclude pattern: *Tests*
   - Correctly excluded (exclude overrides include)

❌ TestIntelligence.Categorizer.csproj
   - Should be excluded as it's not a core project
   - Issue: Pattern matching too broad or missing exclude rule

**Expected vs Actual**:
- Expected Included: TestIntelligence.Core, TestIntelligence.CLI
- Expected Excluded: All test projects, non-core projects
- Actual Included: TestIntelligence.Core, TestIntelligence.CLI, TestIntelligence.Categorizer
- Actual Excluded: All test projects

**Pattern Analysis**:
- Include patterns working: ✅ *Core*, ✅ *TestIntelligence.CLI*
- Exclude patterns working: ✅ *Tests*, ✅ *Test*
- Pattern precedence: ✅ Exclude correctly overrides include

**Performance Metrics**:
- Configuration load time: 45ms
- Project scanning time: 120ms
- Pattern matching time: 23ms

**Issues Found**:
1. Categorizer project incorrectly included - pattern too broad
2. Consider more specific include patterns

**Recommendations**:
- Refine include patterns to be more specific: ["TestIntelligence.Core", "TestIntelligence.CLI"]
- Add validation for pattern conflicts
- Consider case-insensitive pattern matching documentation

**Overall Accuracy**: 9/11 projects correctly classified (82%)
```

## Usage Instructions for Claude

When running this testing protocol:

1. **Be Systematic**: Execute all 5 scenarios and compare results
2. **Verify Manually**: Don't just trust CLI output - use helper commands to verify
3. **Test Edge Cases**: Pay special attention to conflicting patterns and empty configurations
4. **Document Issues**: Note any discrepancies between expected and actual behavior
5. **Performance Awareness**: Monitor configuration loading and pattern matching performance
6. **Pattern Logic**: Understand and verify that exclude patterns override include patterns
7. **Cross-Platform**: Consider path separator differences if testing on different platforms

This comprehensive testing ensures the configuration system correctly filters projects according to specified patterns and handles edge cases appropriately. The goal is to validate that users can reliably configure TestIntelligence to work with their specific project structures and requirements.