Title: Find-Tests: Exclude mocked service calls and add tests for SimplifiedDiffImpactAnalyzer

Problem
- The find-tests command reports many false positives for
  TestIntelligence.ImpactAnalyzer.Services.SimplifiedDiffImpactAnalyzer.AnalyzeDiffImpactAsync.
- Report summary: 78 tests returned; only 10 are true positives (0.8 confidence) that go through the real
  service flow. 68 are false positives (0.36–0.4 confidence) from CLI characterization tests that use
  mocked services (e.g., RunCliCommandWithMocks). Overall accuracy ~12.8%.

Evidence
- Tests in ProgramCliCharacterizationTests and DiffAnalysisServiceTests frequently register
  Substitute.For<IDiffAnalysisService>() / Substitute.For<ISimplifiedDiffImpactAnalyzer>() and therefore
  never exercise the real implementation.
- Analyzer currently treats interface-based calls the same regardless of whether the instance is a real
  implementation or a mock, leading to erroneous coverage via mocked paths.

Scope / Affected
- src/TestIntelligence.ImpactAnalyzer/Services/TestCoverageAnalyzer.cs (confidence & path filtering)
- src/TestIntelligence.ImpactAnalyzer/Analysis/* (call graph semantics; optional tagging)
- tests/TestIntelligence.CLI.Tests/* (characterization tests with mocks)
- tests/ (new unit/integration tests for real analyzer)

Proposal
1) Mock-aware path filtering (heuristic):
   - Detect common mocking frameworks in call paths and discount or exclude those paths.
     • NSubstitute: types/namespaces containing NSubstitute, Substitute.For, Received, Returns.
     • Moq (future): Moq, It, Setup, Returns, Verify.
   - If a path from test -> target traverses through a known mock/proxy creation or a DI registration that
     supplies a mock for the interface, either:
     • Exclude the path from coverage, or
     • Heavily reduce confidence (e.g., cap at 0.2) so it won’t pass selection thresholds.
   - Implementation approach options:
     • Lightweight: at path scoring, scan CallPath node type names and assembly names for known mock markers
       and adjust/exclude.
     • Enhanced: during call graph build, tag nodes originating from mocking frameworks (symbol name/namespace
       match) and propagate tags onto paths for fast checks.

2) Improve confidence scoring for interface-based calls:
   - Penalize paths where the implementation behind an interface call cannot be resolved to a concrete type
     in our solution assemblies.
   - Boost confidence when the concrete generic type or DI registration resolves to our real implementation
     (e.g., SimplifiedDiffImpactAnalyzer).

3) Add missing tests for real implementation:
   - Unit tests targeting SimplifiedDiffImpactAnalyzer.AnalyzeDiffImpactAsync directly with simple diffs to
     validate behavior and ensure the real implementation appears in coverage.
   - Integration tests through DiffAnalysisService with real DI (no mocks) and a small sample solution to
     confirm end-to-end impact analysis.

4) Error reporting and UX:
   - In find-tests output, include a note when suspected mocked paths are filtered/penalized and how many
     results were discarded due to mocks.

Acceptance Criteria
- For the target method AnalyzeDiffImpactAsync, find-tests returns only the 10 true-positive tests (or a
  superset with clear high confidence) and excludes/penalizes the 68 mock-induced false positives.
- Confidence scoring reflects mock vs real calls; mock-influenced paths do not exceed selection thresholds.
- New unit tests exist for SimplifiedDiffImpactAnalyzer; integration tests exist for DiffAnalysisService
  using the real analyzer implementation.
- Documentation updated (README or docs) describing mock-aware behavior and how to interpret results.

Risks / Considerations
- Static analysis cannot always perfectly identify runtime mocks; heuristic tagging may occasionally exclude
  legitimate advanced proxies. Start with conservative rules and log candidates for visibility.
- Framework coverage: initial support for NSubstitute, plan to extend to Moq/others as needed.
- Performance: scanning path nodes for markers should be lightweight; tagging in call graph can keep runtime costs low.

References
- Target Method: TestIntelligence.ImpactAnalyzer.Services.SimplifiedDiffImpactAnalyzer.AnalyzeDiffImpactAsync
- Tests using mocks: ProgramCliCharacterizationTests, DiffAnalysisServiceTests
- Verification report: 10/78 correct; false positives via mocked services

Labels: area:analysis, bug, correctness, enhancement, test-needed

