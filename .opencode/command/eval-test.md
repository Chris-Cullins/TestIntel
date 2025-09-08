# Test Evaluation Prompt: eval-test

You are a test quality assessment expert. Your task is to evaluate the existing tests in this TestIntelligence project and provide comprehensive recommendations for improvement.

## Analysis Tasks

### 1. Test Coverage Analysis
Examine the relationship between the source code in `/src` and the corresponding tests in `/tests` to evaluate:
- **Code Coverage**: How well do the tests cover the actual implementation?
- **Critical Path Coverage**: Are the most important code paths (error handling, edge cases, core business logic) adequately tested?
- **Missing Tests**: Identify classes, methods, or components that lack proper test coverage
- **Coverage Gaps**: Highlight specific scenarios or code branches that are not tested

### 2. Test Quality Assessment
Review all test files to identify:
- **Poor Quality Tests**: Tests that are brittle, unclear, or don't properly validate behavior
- **Test Smells**: Common anti-patterns like:
  - Tests that test implementation details rather than behavior
  - Overly complex or fragile tests
  - Tests with unclear or misleading assertions
  - Tests that are difficult to understand or maintain
  - Duplicated test logic across multiple test classes
  - Tests that don't properly isolate the unit under test
- **Ineffective Tests**: Tests that don't provide meaningful validation or could pass even when the code is broken

### 3. Test Structure & Organization
Evaluate the overall test suite structure:
- **Test Organization**: Are tests logically grouped and easy to navigate?
- **Naming Conventions**: Do test names clearly communicate what is being tested?
- **Test Categories**: Are tests properly categorized (Unit, Integration, E2E)?
- **Setup and Teardown**: Is test setup and cleanup handled consistently?

## Analysis Scope

Focus on these key project components:
- **TestIntelligence.Core**: Assembly loading, caching, test discovery
- **TestIntelligence.ImpactAnalyzer**: Roslyn-based analysis, call graph generation
- **TestIntelligence.CLI**: Command handlers and services
- **TestIntelligence.DataTracker**: Database dependency analysis
- **TestIntelligence.SelectionEngine**: Test selection algorithms
- **TestIntelligence.API**: RESTful controllers

## Deliverable

Generate a comprehensive markdown report in the `/reports` directory named `test-evaluation-report.md` that includes:

### Report Structure
1. **Executive Summary**: High-level assessment of test quality and coverage
2. **Coverage Analysis**: 
   - Overall coverage assessment by component
   - Critical gaps in test coverage
   - Recommendations for new tests needed
3. **Quality Issues**:
   - List of specific tests that need improvement or removal
   - Detailed explanation of why each test is problematic
   - Suggested fixes or replacement approaches
4. **Structural Recommendations**:
   - Test organization improvements
   - Testing strategy enhancements
   - Best practices to implement
5. **Priority Matrix**:
   - High-priority items (critical gaps, broken tests)
   - Medium-priority items (quality improvements)
   - Low-priority items (nice-to-have enhancements)
6. **Action Items**: Concrete, actionable steps to improve the test suite

### Focus Areas for Recommendations
- Tests that should be **removed** (provide clear justification)
- Tests that should be **improved** (with specific suggestions)
- **New tests** that should be added (with rationale for why they're important)
- **Refactoring opportunities** to make tests more maintainable
- **Performance improvements** for the test suite

## Methodology

1. **Read and analyze** all test files in the `/tests` directory
2. **Map tests to source code** to identify coverage gaps
3. **Examine test patterns** across the codebase for consistency and quality
4. **Identify critical business logic** that requires robust testing
5. **Evaluate test maintainability** and readability
6. **Assess test execution efficiency** and identify slow or problematic tests

Your analysis should be thorough, actionable, and prioritized to help improve the overall quality and effectiveness of the test suite.