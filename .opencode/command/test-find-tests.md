# Test Find-Tests Command

Instructions for Claude to test the find-tests command by selecting a random method and verifying the output accuracy.

## Testing Protocol

### Step 1: Select a Random Method
1. Use `find` or `grep` to discover methods in the src/ directory
2. Pick a method from a Core, Categorizer, DataTracker, ImpactAnalyzer, or SelectionEngine class
3. Choose a method that's likely to be tested (public methods, important functionality)

### Step 2: Run the Find-Tests Command
```bash
dotnet run --project src/TestIntelligence.CLI find-tests \
  --method "FullNamespace.ClassName.MethodName" \
  --solution TestIntelligence.sln \
  --format json \
  --output find-tests-result.json \
  --verbose
```

### Step 3: Manual Verification Process
1. **Read the Output**: Examine the JSON results for:
   - List of test methods that allegedly exercise the target method
   - Confidence scores for each test
   - Call path depth information

2. **Code Analysis**: For each test found:
   - Read the test method source code
   - Trace through the test execution path
   - Verify the test actually calls (directly or indirectly) the target method
   - Check if the confidence score seems reasonable based on call depth

3. **Completeness Check**: 
   - Search the test codebase for the target method name
   - Look for any tests that should have been found but weren't
   - Verify no false positives (tests that don't actually exercise the method)

4. **Report Results**:
   - Summarize accuracy: "X out of Y tests correctly identified"
   - Note any false positives or missed tests
   - Comment on confidence score appropriateness
   - Highlight any patterns or issues discovered

### Step 4: Sample Commands to Help with Verification

```bash
# Search for direct method calls in tests
grep -r "MethodName" tests/ --include="*.cs"

# Search for class usage in tests
grep -r "ClassName" tests/ --include="*.cs"

# Look at specific test file
cat tests/path/to/TestClass.cs
```

### Example Verification Report Format

```
## Find-Tests Verification Report

**Target Method**: TestIntelligence.Core.Discovery.NUnitTestDiscovery.DiscoverTestsAsync

**CLI Output Summary**:
- Found: 5 tests
- Confidence scores: High(2), Medium(2), Low(1)

**Manual Verification**:
✅ TestIntelligence.Core.Tests.Discovery.NUnitTestDiscoveryTests.DiscoverTestsAsync_ValidAssembly_ReturnsTests
   - Directly calls target method
   - Confidence: High (appropriate)

✅ TestIntelligence.Core.Tests.TestAnalyzerTests.AnalyzeAssembly_WithNUnitTests_IncludesNUnitTests  
   - Calls TestAnalyzer.AnalyzeAssembly which calls target method
   - Confidence: Medium (appropriate for 1-hop call)

❌ TestIntelligence.SelectionEngine.Tests.SomeUnrelatedTest
   - False positive - doesn't actually call target method
   - Issue: Possible name collision or incorrect call graph

**Missing Tests**:
- TestIntelligence.Core.Tests.Integration.FullAnalysisTests.CompleteAnalysis
  - This test exercises the full pipeline including the target method
  - Should have been found with Low confidence

**Overall Accuracy**: 4/5 correct (80%)
**Recommendations**: 
- Investigate false positive detection
- Review call graph completeness for integration tests
```

## Usage Instructions for Claude

When running this command:
1. Be thorough in your verification - actually read the test code
2. Don't just trust the CLI output - verify by examining source code
3. Look for both false positives and false negatives
4. Consider the appropriateness of confidence scores
5. Report your findings in a clear, structured format
6. If you find issues, suggest potential causes or improvements

This testing helps ensure the find-tests command is working accurately and can be trusted for real-world usage.