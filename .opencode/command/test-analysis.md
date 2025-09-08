# Test Analysis Prompt

Run all non-E2E tests in the TestIntelligence solution and analyze any failures.

## Tasks:
1. Execute all test projects except TestIntelligence.E2E.Tests
2. Collect and analyze any test failures
3. If significant issues are found, create a detailed report in `/reports/test-analysis-report.md`

## Test Projects to Run:
- TestIntelligence.API.Tests
- TestIntelligence.CLI.Tests  
- TestIntelligence.Core.Tests
- TestIntelligence.DataTracker.Tests
- TestIntelligence.Framework48Adapter.Tests
- TestIntelligence.ImpactAnalyzer.Tests
- TestIntelligence.NetCoreAdapter.Tests
- TestIntelligence.SelectionEngine.Tests

## Report Requirements:
If failures are found, create a report that includes:
- Summary of test results (pass/fail counts by project)
- Detailed analysis of each failure
- Common patterns or root causes
- Recommendations for fixes
- Priority assessment of issues

## Commands:
Use `dotnet test` with appropriate filters to exclude E2E tests, or run individual test projects.