---
applyTo: 'test/**/*.cs'
---
# Testing Conventions
**When to read:** Writing or modifying test files.

- Use NUnit (`[Test]`, `[TestFixture]`), Moq for mocks, AwesomeAssertions for fluent assertions.
- Follow the Arrange-Act-Assert (AAA) pattern.
- Do NOT add tests to `test/ProductConstructionService.ScenarioTests` unless intentionally writing deployed-service scenarios — they require a full service deployment and are excluded from local/agent verification.

## Codeflow tests
- `test/Darc/Microsoft.DotNet.DarcLib.Codeflow.Tests` are local end-to-end tests: they create real on-disk git repositories and exercise the real codeflow classes through a real `ServiceProvider` (no mocks except the BAR/API client). They require `git` on PATH.
- Extend `CodeFlowTestsBase` for these; reuse its repo/VMR setup and `GitOperations` helper rather than rolling your own.
- They are slower than ordinary unit tests — keep them deterministic and clean up their temp directories.
