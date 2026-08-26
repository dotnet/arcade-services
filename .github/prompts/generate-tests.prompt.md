---
description: Generate NUnit unit tests for the active file following repo conventions.
---

# Generate Tests

Generate unit tests for `${file}` using this repo's conventions.

- Framework: NUnit (`[Test]`, `[TestFixture]`).
- Mocking: Moq. Assertions: AwesomeAssertions (fluent).
- Structure each test with the Arrange-Act-Assert (AAA) pattern.
- Place the test in the matching `test/` project; reuse existing fixtures/helpers where available.
- Do NOT target `test/ProductConstructionService.ScenarioTests` (requires a deployed service).

<!-- TODO: Add any repo-specific test naming or fixture rules you want enforced -->
