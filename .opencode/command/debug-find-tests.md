# Debug Find-Tests Command

Debug version of find-tests testing with enhanced tracing to troubleshoot issues we've been experiencing with the find-tests command accuracy.

## Known Issues to Investigate
- Potential false positives in test detection
- Missing tests that should be found
- Incorrect confidence scoring
- Call graph traversal problems
- Assembly loading or reflection issues

## Debug Testing Protocol

### Step 1: Select Target Method with Debug Context
1. **Choose a well-known method** with clear test relationships:
   ```bash
   # Example: Pick a method we know is tested
   echo "🔍 Selecting target method for debug analysis..."
   grep -r "DiscoverTestsAsync\|AnalyzeAssembly\|ProcessDependencies" src/ --include="*.cs" -n | head -5
   ```

2. **Document expected behavior**:
   ```bash
   echo "📝 Documenting what we expect to find..."
   # Manually identify tests that should be found
   grep -r "TargetMethodName" tests/ --include="*.cs" -l
   ```

### Step 2: Add Debug Tracing to Source Code

Before running the command, temporarily add debug statements to trace execution:

```bash
# Backup original files
cp src/TestIntelligence.CLI/Commands/FindTestsCommand.cs src/TestIntelligence.CLI/Commands/FindTestsCommand.cs.debug-backup
cp src/TestIntelligence.Core/Services/TestMethodMapper.cs src/TestIntelligence.Core/Services/TestMethodMapper.cs.debug-backup
```

#### Add debug statements to key files:

**1. FindTestsCommand.cs - Entry point tracing**:
```csharp
// Add after method signature
Console.WriteLine($"🔍 DEBUG: Starting find-tests for method: {methodName}");
Console.WriteLine($"🔍 DEBUG: Solution path: {solutionPath}");
Console.WriteLine($"🔍 DEBUG: Output format: {format}");
```

**2. TestMethodMapper.cs - Core logic tracing**:
```csharp
// Add in FindTestsForMethod
Console.WriteLine($"🔍 DEBUG: Loading solution: {solutionPath}");
Console.WriteLine($"🔍 DEBUG: Target method: {targetMethod}");
Console.WriteLine($"🔍 DEBUG: Found {projects.Count} projects to analyze");

// Add in call graph traversal
Console.WriteLine($"🔍 DEBUG: Building call graph for method: {method.Name}");
Console.WriteLine($"🔍 DEBUG: Found {callers.Count} direct callers");

// Add in test discovery
Console.WriteLine($"🔍 DEBUG: Discovered {testMethods.Count} total test methods");
Console.WriteLine($"🔍 DEBUG: Filtering tests that call target method...");
```

**3. Assembly loading tracing**:
```csharp
// Add assembly loading debug info
Console.WriteLine($"🔍 DEBUG: Loading assembly: {assemblyPath}");
Console.WriteLine($"🔍 DEBUG: Assembly loaded successfully: {assembly.FullName}");
Console.WriteLine($"🔍 DEBUG: Found {types.Length} types in assembly");
```

### Step 3: Run Debug-Enhanced Find-Tests Command

```bash
# Clear cache to ensure fresh analysis
dotnet run --project src/TestIntelligence.CLI cache \
  --solution TestIntelligence.sln \
  --action clear

echo "🚀 Running debug find-tests command..."
dotnet run --project src/TestIntelligence.CLI find-tests \
  --method "TestIntelligence.Core.Discovery.NUnitTestDiscovery.DiscoverTestsAsync" \
  --solution TestIntelligence.sln \
  --format json \
  --output debug-find-tests-result.json \
  --verbose 2>&1 | tee debug-find-tests-trace.log
```

### Step 4: Enhanced Debug Analysis

#### 4.1 Trace Analysis
```bash
echo "📊 Analyzing debug trace..."

# Check if solution loaded correctly
grep "Loading solution" debug-find-tests-trace.log
grep "Found.*projects" debug-find-tests-trace.log

# Check assembly loading
grep "Loading assembly" debug-find-tests-trace.log
grep "Assembly loaded successfully" debug-find-tests-trace.log

# Check call graph construction
grep "Building call graph" debug-find-tests-trace.log
grep "Found.*callers" debug-find-tests-trace.log

# Check test discovery
grep "Discovered.*test methods" debug-find-tests-trace.log
grep "Filtering tests" debug-find-tests-trace.log
```

#### 4.2 Validate Each Step
```bash
echo "🔍 Step-by-step validation..."

# 1. Verify target method exists
echo "1. Checking if target method exists in codebase:"
grep -r "DiscoverTestsAsync" src/ --include="*.cs" -A 2 -B 2

# 2. Verify test methods that should be found
echo "2. Manual search for tests that should be found:"
grep -r "DiscoverTestsAsync\|NUnitTestDiscovery" tests/ --include="*.cs" -n

# 3. Check for false positives in results
echo "3. Examining found tests for false positives:"
jq '.foundTests[].testName' debug-find-tests-result.json

# 4. Cross-reference with actual test code
echo "4. Cross-referencing with actual test implementations:"
for test in $(jq -r '.foundTests[].testName' debug-find-tests-result.json); do
  echo "Examining test: $test"
  # Find the test file and examine it
  grep -r "$test" tests/ --include="*.cs" -A 10 -B 2
done
```

### Step 5: Deep Dive Debugging

#### 5.1 Call Graph Debugging
```bash
echo "🕸️ Deep dive into call graph construction..."

# Generate call graph for target method
dotnet run --project src/TestIntelligence.CLI callgraph \
  --path TestIntelligence.sln \
  --format json \
  --output debug-callgraph.json \
  --verbose

# Compare call graph with find-tests results
echo "Comparing call graph with find-tests results..."
jq '.methods[] | select(.name | contains("DiscoverTestsAsync"))' debug-callgraph.json
```

#### 5.2 Assembly Reflection Debugging
Add deeper assembly inspection:
```csharp
// Add to assembly loading section
Console.WriteLine($"🔍 DEBUG: Assembly types found:");
foreach (var type in assembly.GetTypes())
{
    Console.WriteLine($"  - {type.FullName}");
    if (type.Name.Contains("Test"))
    {
        var methods = type.GetMethods().Where(m => m.GetCustomAttributes().Any());
        Console.WriteLine($"    Test methods: {methods.Count()}");
        foreach (var method in methods)
        {
            Console.WriteLine($"      - {method.Name}");
        }
    }
}
```

### Step 6: Issue Classification and Reporting

#### 6.1 Categorize Issues Found
```bash
echo "📋 Categorizing issues found during debug session..."

# False Positives Analysis
echo "❌ FALSE POSITIVES:"
echo "Tests that were found but don't actually call the target method:"
# Manual analysis based on code examination

# False Negatives Analysis  
echo "❌ FALSE NEGATIVES:"
echo "Tests that should have been found but weren't:"
# Compare manual grep results with CLI output

# Confidence Score Issues
echo "⚠️ CONFIDENCE SCORE ISSUES:"
echo "Tests with inappropriate confidence scores:"
# Analyze if scores match call depth/complexity
```

#### 6.2 Root Cause Analysis
Based on debug output, identify likely causes:

**Common Issues to Look For**:
- **Assembly Loading**: Are all test assemblies being loaded?
- **Reflection Issues**: Are test attributes being detected correctly?
- **Call Graph**: Is method call traversal working correctly?
- **Name Matching**: Are there namespace or overload resolution issues?
- **Caching**: Are cached results stale or incorrect?

### Step 7: Debug Report Format

```
## Debug Find-Tests Analysis Report

**Target Method**: TestIntelligence.Core.Discovery.NUnitTestDiscovery.DiscoverTestsAsync
**Debug Session Date**: [Current Date]
**Known Issues Being Investigated**: False positives, missing integration tests

### Debug Trace Summary
- ✅ Solution loaded: 15 projects found
- ✅ Target method located in: TestIntelligence.Core.dll
- ⚠️ Assembly loading: 2 warnings about dependency versions
- ✅ Call graph construction: 45 direct callers found
- ❌ Test discovery: Only 12 test methods found (expected ~20)

### Execution Flow Analysis

#### 1. Solution Loading (✅ Working)
```
🔍 DEBUG: Loading solution: TestIntelligence.sln
🔍 DEBUG: Found 15 projects to analyze
```

#### 2. Target Method Resolution (✅ Working)
```
🔍 DEBUG: Target method: TestIntelligence.Core.Discovery.NUnitTestDiscovery.DiscoverTestsAsync
🔍 DEBUG: Method located in assembly: TestIntelligence.Core
```

#### 3. Call Graph Construction (⚠️ Partial Issue)
```
🔍 DEBUG: Building call graph for method: DiscoverTestsAsync
🔍 DEBUG: Found 45 direct callers
Issue: Missing some integration test callers due to indirect invocation
```

#### 4. Test Discovery (❌ Major Issue)
```
🔍 DEBUG: Discovered 89 total test methods
🔍 DEBUG: Filtering tests that call target method...
Issue: Filter logic excluding valid tests with indirect calls
```

### Issues Identified

#### ❌ Issue #1: Missing Integration Tests
**Problem**: Integration tests that call TestAnalyzer.AnalyzeAssembly (which calls DiscoverTestsAsync) are not being found
**Root Cause**: Call graph depth limit or indirect call resolution
**Evidence**: Manual grep found 8 integration tests, CLI found only 2
**Debug Trace**: 
```
Manual: grep -r "AnalyzeAssembly" tests/ found 8 matches
CLI Result: Only 2 tests with confidence scores > 0.5
```

#### ❌ Issue #2: False Positive Detection
**Problem**: Test "SomeUnrelatedTest" was found with 0.3 confidence
**Root Cause**: Name collision or incorrect dependency analysis
**Evidence**: Test code shows no actual calls to target method
**Debug Trace**:
```
Test code analysis: Only calls DatabaseHelper.Setup() and Assert methods
No path to DiscoverTestsAsync found in manual trace
```

#### ⚠️ Issue #3: Confidence Score Inaccuracy
**Problem**: Direct test has confidence 0.7, indirect test has 0.8
**Root Cause**: Scoring algorithm may be inverted or considering other factors
**Evidence**: NUnitTestDiscoveryTests.DirectTest should have higher confidence than IntegrationTests.IndirectTest

### Recommended Fixes

1. **Call Graph Depth**: Increase traversal depth for integration tests
2. **Filter Logic**: Review test filtering criteria to include indirect callers
3. **Name Resolution**: Improve method name matching to avoid false positives
4. **Confidence Scoring**: Review algorithm to properly weight direct vs indirect calls

### Next Steps
1. Apply debug fixes to core components
2. Re-run debug session to validate improvements
3. Add unit tests for edge cases discovered
4. Update documentation with known limitations
```

### Step 8: Fix Issues Found

**CRITICAL**: After identifying issues through debugging, you MUST implement fixes for all problems discovered.

#### 8.1 Apply Fixes Based on Root Cause Analysis

Based on the issues identified in the debug report, implement the following fixes:

**Fix #1: Call Graph Depth Issues**
```bash
echo "🔧 Fixing call graph traversal depth..."

# Edit the call graph construction to increase depth limit
# Look for depth limiting code in TestMethodMapper or CallGraphBuilder
grep -r "depth\|limit" src/TestIntelligence.Core/ --include="*.cs" -n

# Implement fix - example:
# Change: const int MAX_DEPTH = 3;
# To:     const int MAX_DEPTH = 5;
```

**Fix #2: Test Filtering Logic**
```bash
echo "🔧 Fixing test filtering to include indirect callers..."

# Find and fix the test filtering logic
# Look for filtering criteria that might be too restrictive
grep -r "filter\|where.*confidence\|threshold" src/TestIntelligence.Core/ --include="*.cs" -A 3 -B 3
```

**Fix #3: Confidence Score Algorithm**
```bash
echo "🔧 Fixing confidence scoring algorithm..."

# Locate confidence scoring logic
grep -r "confidence.*score\|calculateConfidence" src/TestIntelligence.Core/ --include="*.cs" -n

# Review and fix scoring to properly weight:
# - Direct calls: Higher confidence (0.8-1.0)
# - One-hop indirect: Medium confidence (0.6-0.8)  
# - Multi-hop indirect: Lower confidence (0.3-0.6)
# - No relation: Very low confidence (0.0-0.2)
```

**Fix #4: False Positive Prevention**
```bash
echo "🔧 Implementing false positive detection..."

# Add stricter validation for method relationships
# Implement additional verification steps before including tests
```

#### 8.2 Implement Specific Code Changes

For each identified issue, make the actual code changes:

```bash
echo "📝 Implementing code fixes..."

# Example fix for call graph depth
# Find the relevant file and implement the change
# Use Edit tool to modify the source code with the fix
```

#### 8.3 Validate Fixes

After implementing fixes, validate they work:

```bash
echo "✅ Validating fixes..."

# Rebuild the solution
dotnet build

# Clear cache to ensure fresh analysis
dotnet run --project src/TestIntelligence.CLI cache \
  --solution TestIntelligence.sln \
  --action clear

# Re-run the find-tests command to verify improvements
dotnet run --project src/TestIntelligence.CLI find-tests \
  --method "TestIntelligence.Core.Discovery.NUnitTestDiscovery.DiscoverTestsAsync" \
  --solution TestIntelligence.sln \
  --format json \
  --output fixed-find-tests-result.json \
  --verbose

echo "🔍 Comparing before/after results..."
# Compare original debug results with fixed results
echo "Before fix - tests found: $(jq '.foundTests | length' debug-find-tests-result.json)"
echo "After fix - tests found: $(jq '.foundTests | length' fixed-find-tests-result.json)"

# Verify specific issues were resolved:
echo "Checking if missing integration tests are now found..."
echo "Checking if false positives were eliminated..."
echo "Checking if confidence scores are more accurate..."
```

#### 8.4 Run Comprehensive Tests

Ensure fixes don't break other functionality:

```bash
echo "🧪 Running comprehensive tests after fixes..."

# Run all relevant tests
dotnet test tests/TestIntelligence.Core.Tests/ -v normal
dotnet test tests/TestIntelligence.ImpactAnalyzer.Tests/ -v normal

# Test with different target methods to ensure general improvement
dotnet run --project src/TestIntelligence.CLI find-tests \
  --method "TestIntelligence.Core.TestAnalyzer.AnalyzeAssembly" \
  --solution TestIntelligence.sln \
  --output validation-test-2.json

dotnet run --project src/TestIntelligence.CLI find-tests \
  --method "TestIntelligence.DataTracker.DatabaseAnalyzer.AnalyzeDependencies" \
  --solution TestIntelligence.sln \
  --output validation-test-3.json
```

### Step 9: Cleanup and Restoration

```bash
echo "🧹 Cleaning up debug modifications..."

# Restore original files (debug tracing code)
mv src/TestIntelligence.CLI/Commands/FindTestsCommand.cs.debug-backup src/TestIntelligence.CLI/Commands/FindTestsCommand.cs
mv src/TestIntelligence.Core/Services/TestMethodMapper.cs.debug-backup src/TestIntelligence.Core/Services/TestMethodMapper.cs

# Keep debug logs and results for reference
mkdir -p debug-logs
mv debug-find-tests-trace.log debug-logs/
mv debug-find-tests-result.json debug-logs/
mv fixed-find-tests-result.json debug-logs/
mv debug-callgraph.json debug-logs/
mv validation-test-*.json debug-logs/

echo "✅ Debug session complete with fixes applied. Logs saved to debug-logs/"
```

## Usage Instructions for Claude

When running this debug command, you MUST:

1. **Be systematic** - Follow each debug step to identify the exact failure point
2. **Preserve evidence** - Save all debug output and manual verification results
3. **Compare exhaustively** - Cross-reference CLI results with manual code analysis
4. **Focus on root causes** - Don't just identify symptoms, trace to underlying issues
5. **Document thoroughly** - Create detailed reports to help fix the underlying problems
6. **IMPLEMENT FIXES** - Actually modify the source code to resolve identified issues
7. **Validate fixes** - Re-run debug session and tests after applying fixes
8. **Test comprehensively** - Ensure fixes don't break other functionality

**CRITICAL REQUIREMENT**: This command is not complete until you have:
- ✅ Identified all issues through debugging
- ✅ Implemented code fixes for each identified problem
- ✅ Validated that fixes resolve the issues
- ✅ Confirmed no regressions were introduced
- ✅ Updated any relevant tests or documentation

This debug-and-fix approach will not only identify where the find-tests command is failing but will also resolve those issues to improve the overall accuracy and reliability of the TestIntelligence library.