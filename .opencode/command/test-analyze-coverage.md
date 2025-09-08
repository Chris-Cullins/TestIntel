# Test Analyze Coverage Command

Instructions for Claude to test the analyze-coverage command by selecting test methods and verifying how well they cover code changes.

## Testing Protocol

### Step 1: Create Test Scenario
1. **Select Target Tests**: Choose 2-3 test methods from different test projects:
   - Pick tests that exercise different parts of the codebase
   - Include both unit tests and integration tests
   - Choose tests you can manually trace through

2. **Create Mock Changes**: Generate a git diff to analyze:
   ```bash
   # Option A: Create actual changes and diff them
   echo "// Test change" >> src/TestIntelligence.Core/Discovery/NUnitTestDiscovery.cs
   git add -A
   git diff --cached > test-changes.patch
   git reset HEAD
   
   # Option B: Use existing git history
   git diff HEAD~1 > recent-changes.patch
   ```

### Step 2: Run the Analyze-Coverage Command
```bash
dotnet run --project src/TestIntelligence.CLI analyze-coverage \
  --solution TestIntelligence.sln \
  --tests "TestClass1.TestMethod1" "TestClass2.TestMethod2" "TestClass3.TestMethod3" \
  --git-command "diff HEAD~1" \
  --verbose \
  --output coverage-analysis.json
```

Alternative with diff file:
```bash
dotnet run --project src/TestIntelligence.CLI analyze-coverage \
  --solution TestIntelligence.sln \
  --tests "TestClass1.TestMethod1" "TestClass2.TestMethod2" \
  --diff-file test-changes.patch \
  --verbose \
  --output coverage-analysis.json
```

### Step 3: Manual Verification Process

#### 3.1 Analyze the Output
1. **Read Coverage Report**: Examine JSON results for:
   - Coverage percentage for each test
   - List of changed methods/classes covered by each test
   - Uncovered changes identified
   - Overall coverage metrics

2. **Understand Change Set**: Review the git diff to identify:
   - Which files were modified
   - Which methods/classes were changed
   - Nature of changes (new code, modifications, deletions)

#### 3.2 Manual Coverage Verification
For each test method:

1. **Trace Test Execution**:
   - Read the test method source code
   - Follow all method calls made by the test
   - Map the execution path through the codebase

2. **Match Against Changes**:
   - Compare test execution path with changed code
   - Identify which changed methods/classes the test actually exercises
   - Note any changed code the test doesn't reach

3. **Validate Coverage Calculation**:
   - Count changed methods covered vs. total changed methods
   - Verify the coverage percentage is mathematically correct
   - Check if the analysis missed any coverage or overcounted

#### 3.3 Gap Analysis
1. **Identify Uncovered Changes**:
   - Find changed code not exercised by any of the selected tests
   - Verify these are truly uncovered (not false negatives)

2. **Find Coverage Gaps**:
   - Look for changed code that should be covered but isn't
   - Search for additional tests that might cover the gaps

### Step 4: Verification Commands

```bash
# Search for tests that might cover a specific changed class
grep -r "ChangedClassName" tests/ --include="*.cs" -l

# Look at specific changed file to understand modifications
git show HEAD~1:src/path/to/ChangedFile.cs | diff - src/path/to/ChangedFile.cs

# Find all tests in a specific test class
grep -n "\[Test\]\|\[Fact\]\|\[TestMethod\]" tests/path/to/TestClass.cs

# Trace method usage across the codebase
grep -r "ChangedMethodName" src/ --include="*.cs"
```

### Step 5: Sample Verification Report Format

```
## Analyze-Coverage Verification Report

**Test Scenario**:
- Selected Tests: 3 tests from Core and DataTracker projects
- Change Set: Modified NUnitTestDiscovery.cs and TestAnalyzer.cs (8 methods changed)
- Git Command: `diff HEAD~1`

**CLI Output Summary**:
- Test1 Coverage: 75% (6/8 changed methods)
- Test2 Coverage: 25% (2/8 changed methods)  
- Test3 Coverage: 0% (0/8 changed methods)
- Overall Coverage: 87.5% (7/8 changed methods covered by at least one test)

**Manual Verification**:

✅ **Test1: NUnitTestDiscoveryTests.DiscoverTestsAsync_ValidAssembly**
   - Claimed Coverage: 75% (6/8 methods)
   - Manual Trace: Calls NUnitTestDiscovery.DiscoverTestsAsync → LoadAssembly → ProcessTypes → ExtractAttributes
   - Actually Covers: NUnitTestDiscovery.DiscoverTestsAsync, LoadAssembly, ProcessTypes, ExtractAttributes, ValidateTest, FormatResults (6/8) ✅
   - Coverage calculation: Correct

✅ **Test2: TestAnalyzerTests.AnalyzeFullSolution**  
   - Claimed Coverage: 25% (2/8 methods)
   - Manual Trace: Calls TestAnalyzer.AnalyzeAssembly → NUnitTestDiscovery.DiscoverTestsAsync → LoadAssembly
   - Actually Covers: AnalyzeAssembly, DiscoverTestsAsync (2/8) ✅
   - Coverage calculation: Correct

⚠️ **Test3: DataTrackerTests.SomeUnrelatedTest**
   - Claimed Coverage: 0% (0/8 methods)
   - Manual Trace: Only exercises database tracking functionality
   - Actually Covers: None of the changed methods ✅
   - Coverage calculation: Correct (true negative)

**Uncovered Changes Verification**:
✅ **TestAnalyzer.ValidateConfiguration** - Correctly identified as uncovered
   - No selected tests call this method
   - Manually confirmed by searching test codebase

❌ **Missing Coverage Detection**:
   - Found integration test `FullPipelineTests.CompleteAnalysis` that exercises ValidateConfiguration
   - This test wasn't in the selected set but would provide coverage
   - CLI correctly reported method as uncovered for the selected tests

**Overall Accuracy**: 100% - All coverage calculations verified as correct
**Coverage Gap Analysis**: 1/8 methods uncovered by selected tests (12.5% gap)

**Recommendations**: 
- Coverage analysis is mathematically accurate
- Consider expanding test selection to include integration tests for better coverage
- The uncovered method has tests available but weren't in the analyzed set
```

### Additional Verification Scenarios

#### Scenario A: High Coverage Test Set
Select tests known to exercise broad functionality:
- Integration tests
- End-to-end workflow tests  
- Tests that call multiple components

#### Scenario B: Low Coverage Test Set
Select very focused unit tests:
- Tests that only exercise one method
- Isolated component tests
- Mock-heavy tests with limited real code execution

#### Scenario C: Mixed Framework Changes
Create changes spanning multiple projects:
- Core library changes
- CLI command changes
- Test framework changes

## Usage Instructions for Claude

When running this command:
1. **Be methodical** - actually trace through test execution paths
2. **Verify math** - check that coverage percentages are calculated correctly
3. **Look for edge cases** - tests that might have unexpected coverage patterns
4. **Consider test types** - unit vs integration tests may have different coverage patterns
5. **Cross-reference** - use grep/search to validate coverage claims
6. **Report thoroughly** - document both correct results and any discrepancies found

This testing ensures the analyze-coverage command accurately maps test execution to code changes, which is critical for intelligent test selection and impact analysis.