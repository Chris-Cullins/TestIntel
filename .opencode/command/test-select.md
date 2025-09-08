# Test Select Command

Instructions for Claude to test the select command by creating code changes and verifying intelligent test selection accuracy across different confidence levels.

## Testing Protocol

### Step 1: Create Test Scenarios

#### Scenario A: Single File Change (Core Component)
```bash
# Make a targeted change to a core component
cp src/TestIntelligence.Core/Discovery/NUnitTestDiscovery.cs src/TestIntelligence.Core/Discovery/NUnitTestDiscovery.cs.backup
echo "        // Test change for select command validation" >> src/TestIntelligence.Core/Discovery/NUnitTestDiscovery.cs
```

#### Scenario B: Multi-File Change (Cross-Component)
```bash
# Make changes across multiple components
echo "        // Cross-component change test" >> src/TestIntelligence.Core/TestAnalyzer.cs
echo "        // Related CLI change" >> src/TestIntelligence.CLI/Commands/AnalyzeCommand.cs
```

#### Scenario C: Interface/Contract Change
```bash
# Modify an interface or base class that affects multiple implementations
echo "        // Interface change affecting multiple implementations" >> src/TestIntelligence.Core/Interfaces/ITestDiscovery.cs
```

### Step 2: Test Different Confidence Levels

#### 2.1 Fast Confidence (30 sec, 70% confidence)
```bash
dotnet run --project src/TestIntelligence.CLI select \
  --path TestIntelligence.sln \
  --changes "src/TestIntelligence.Core/Discovery/NUnitTestDiscovery.cs" \
  --confidence Fast \
  --max-tests 20 \
  --output select-fast.json \
  --verbose
```

#### 2.2 Medium Confidence (5 min, 85% confidence)  
```bash
dotnet run --project src/TestIntelligence.CLI select \
  --path TestIntelligence.sln \
  --changes "src/TestIntelligence.Core/Discovery/NUnitTestDiscovery.cs" \
  --confidence Medium \
  --max-tests 50 \
  --output select-medium.json \
  --verbose
```

#### 2.3 High Confidence (15 min, 95% confidence)
```bash
dotnet run --project src/TestIntelligence.CLI select \
  --path TestIntelligence.sln \
  --changes "src/TestIntelligence.Core/Discovery/NUnitTestDiscovery.cs" \
  --confidence High \
  --max-tests 100 \
  --output select-high.json \
  --verbose
```

#### 2.4 Full Confidence (Complete suite, 100% confidence)
```bash
dotnet run --project src/TestIntelligence.CLI select \
  --path TestIntelligence.sln \
  --changes "src/TestIntelligence.Core/Discovery/NUnitTestDiscovery.cs" \
  --confidence Full \
  --output select-full.json \
  --verbose
```

### Step 3: Manual Verification Process

#### 3.1 Analyze Selection Results
For each confidence level, examine:
1. **Number of tests selected**
2. **Test categories included** (Unit, Integration, Database, API, UI)
3. **Confidence scores** for each selected test
4. **Execution time estimates**
5. **Tests organized by priority/relevance**

#### 3.2 Validate Test Selection Logic

**Direct Impact Tests (Should be in Fast)**:
```bash
# Find tests that directly test the changed class
grep -r "NUnitTestDiscovery" tests/ --include="*.cs" -l
# These should appear in Fast confidence with high scores
```

**Indirect Impact Tests (Should be in Medium)**:
```bash
# Find tests that use classes that depend on the changed class
grep -r "TestAnalyzer\|Discovery" tests/ --include="*.cs" -l
# These should appear in Medium confidence with medium scores
```

**Integration Tests (Should be in High)**:
```bash
# Find integration tests that exercise the full pipeline
grep -r "Integration\|EndToEnd\|FullPipeline" tests/ --include="*.cs" -l
# These should appear in High confidence with lower scores
```

#### 3.3 Confidence Level Validation

Verify that each confidence level follows the expected pattern:
- **Fast ⊂ Medium ⊂ High ⊂ Full** (each level includes previous levels)
- **Decreasing relevance scores** as confidence level increases
- **Appropriate test count limits** respected
- **Time estimates** align with confidence level targets

### Step 4: Cross-Reference with Other Commands

#### 4.1 Verify Against Find-Tests
```bash
# For each selected test, verify it actually relates to the changed code
dotnet run --project src/TestIntelligence.CLI find-tests \
  --method "TestIntelligence.Core.Discovery.NUnitTestDiscovery.DiscoverTestsAsync" \
  --solution TestIntelligence.sln \
  --output find-tests-cross-check.json
```

#### 4.2 Verify Against Coverage Analysis
```bash
# Check if selected tests actually provide good coverage of changes
dotnet run --project src/TestIntelligence.CLI analyze-coverage \
  --solution TestIntelligence.sln \
  --tests $(jq -r '.selectedTests[].testName' select-medium.json | tr '\n' ' ') \
  --changes "src/TestIntelligence.Core/Discovery/NUnitTestDiscovery.cs" \
  --output coverage-cross-check.json
```

### Step 5: Sample Verification Report Format

```
## Select Command Verification Report

**Test Scenario**: Modified NUnitTestDiscovery.cs (single method change)
**Change Type**: Core component modification affecting test discovery logic

### Confidence Level Analysis

#### Fast Confidence (Target: 30 sec, 70% confidence)
- **Selected**: 8 tests
- **Time Estimate**: 25 seconds ✅
- **Categories**: Unit (6), Integration (2)
- **Top Tests**:
  ✅ NUnitTestDiscoveryTests.DiscoverTestsAsync_ValidAssembly (Score: 0.95)
  ✅ NUnitTestDiscoveryTests.DiscoverTestsAsync_EmptyAssembly (Score: 0.92)  
  ✅ TestAnalyzerTests.AnalyzeAssembly_WithNUnitTests (Score: 0.78)

#### Medium Confidence (Target: 5 min, 85% confidence)  
- **Selected**: 23 tests (includes all Fast + 15 more) ✅
- **Time Estimate**: 4.2 minutes ✅
- **Categories**: Unit (15), Integration (6), Database (2)
- **Additional Tests**:
  ✅ CoreIntegrationTests.FullDiscoveryPipeline (Score: 0.65)
  ✅ MultiFrameworkTests.NUnitAndXUnit (Score: 0.58)

#### High Confidence (Target: 15 min, 95% confidence)
- **Selected**: 47 tests (includes all Medium + 24 more) ✅  
- **Time Estimate**: 12.8 minutes ✅
- **Categories**: Unit (25), Integration (15), Database (4), API (3)
- **Additional Tests**:
  ✅ E2ETests.CompleteAnalysisWorkflow (Score: 0.35)
  ⚠️ UnrelatedUITests.SomeUITest (Score: 0.12) - Questionable relevance

#### Full Confidence
- **Selected**: All 215 tests ✅
- **Includes**: Every test in the solution

### Manual Verification Results

#### Direct Impact Validation ✅
**Expected**: Tests that directly call NUnitTestDiscovery methods
- Found 6 direct tests in TestIntelligence.Core.Tests
- All 6 appeared in Fast confidence with scores > 0.8 ✅
- Scores appropriately reflect call directness ✅

#### Indirect Impact Validation ✅  
**Expected**: Tests that call TestAnalyzer which uses NUnitTestDiscovery
- Found 8 indirect tests across Core and CLI test projects
- 7/8 appeared in Medium confidence ✅
- 1 missing test: CLIIntegrationTests.AnalyzeCommand_WithNUnit ❌
- Scores appropriately lower (0.5-0.8 range) ✅

#### Integration Test Validation ⚠️
**Expected**: End-to-end tests that exercise full discovery pipeline  
- Found 12 integration tests
- 10/12 appeared in High confidence ✅
- 2 missing: PerformanceTests.LargeSolutionAnalysis, StressTests.ConcurrentDiscovery ❌
- Some questionable inclusions with very low relevance scores

### Cross-Reference Validation

#### Find-Tests Cross-Check ✅
- Selected tests from Fast confidence all verified via find-tests command
- No false positives detected in direct impact tests
- Confidence scores align between select and find-tests commands

#### Coverage Analysis Cross-Check ⚠️
- Medium confidence tests provide 78% coverage of changed code
- Expected: ~85% based on confidence level target
- Gap: Some edge cases in error handling not covered by selected tests
- Recommendation: Include additional error handling tests

### Selection Logic Assessment

**Strengths**:
- ✅ Confidence levels properly nested (Fast ⊂ Medium ⊂ High ⊂ Full)
- ✅ Time estimates realistic and within targets
- ✅ Direct impact tests correctly prioritized
- ✅ Test categories appropriately distributed
- ✅ Relevance scores generally accurate

**Issues Found**:
- ❌ 2 high-relevance tests missed in Medium confidence
- ❌ Some very low relevance tests included in High confidence  
- ❌ Coverage gap vs confidence level expectations

**Overall Accuracy**: 85% - Good test selection with minor gaps
**Recommendation**: 
- Review inclusion threshold for High confidence
- Investigate why 2 relevant tests were missed
- Consider adjusting relevance scoring for integration tests
```

### Advanced Test Scenarios

#### Scenario D: Configuration Change Impact
```bash
# Test with configuration/settings changes that might affect many components
echo "    // Configuration change" >> src/TestIntelligence.Core/Configuration/AnalysisSettings.cs
```

#### Scenario E: Multiple File Changes
```bash  
# Test selection with multiple related changes
dotnet run --project src/TestIntelligence.CLI select \
  --path TestIntelligence.sln \
  --changes "src/TestIntelligence.Core/Discovery/NUnitTestDiscovery.cs" "src/TestIntelligence.Core/TestAnalyzer.cs" \
  --confidence Medium \
  --output select-multifile.json
```

#### Scenario F: Max Tests Constraint
```bash
# Test that max-tests parameter is respected
dotnet run --project src/TestIntelligence.CLI select \
  --path TestIntelligence.sln \
  --changes "src/TestIntelligence.Core/Discovery/NUnitTestDiscovery.cs" \
  --confidence High \
  --max-tests 10 \
  --output select-constrained.json
```

### Cleanup
```bash
# Restore original files after testing
mv src/TestIntelligence.Core/Discovery/NUnitTestDiscovery.cs.backup src/TestIntelligence.Core/Discovery/NUnitTestDiscovery.cs
git checkout -- src/TestIntelligence.Core/TestAnalyzer.cs src/TestIntelligence.CLI/Commands/AnalyzeCommand.cs
```

## Usage Instructions for Claude

When running this command:
1. **Test all confidence levels** - verify the nested relationship and appropriateness
2. **Validate time estimates** - check if they align with confidence level targets  
3. **Cross-reference results** - use find-tests and analyze-coverage to verify selections
4. **Check edge cases** - test with multiple files, constraints, and different change types
5. **Assess relevance scoring** - ensure tests are ranked appropriately by impact likelihood
6. **Verify completeness** - look for missing tests that should be included
7. **Report systematically** - document accuracy, gaps, and recommendations

This testing ensures the select command provides intelligent, accurate test selection that balances coverage with execution efficiency across different confidence levels.