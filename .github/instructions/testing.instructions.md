---
applyTo: 'test/**/*.cs'
---
# Testing Conventions
**When to read:** Writing or modifying test files.

- Use NUnit (`[Test]`, `[TestFixture]`), Moq for mocks, AwesomeAssertions for fluent assertions.
- Follow the Arrange-Act-Assert (AAA) pattern.
- Do NOT add tests to `test/ProductConstructionService.ScenarioTests` unless intentionally writing deployed-service scenarios — they require a full service deployment and are excluded from local/agent verification.
