# AGENTS.md

## Verification
- Build & test: `dotnet build` then `dotnet test --no-build`
- The required .NET SDK is pinned in `global.json` — assume it is installed.
- If verification fails, fix the root cause and re-run.

## Environment
- .NET 10 (see `global.json`). `Directory.Build.props` sets `TreatWarningsAsErrors=true` — unused usings and warnings break the build.
- Build via `dotnet build`, `Build.cmd` (Windows), or `./build.sh` (Linux/macOS); the repo uses the Arcade SDK.

## Guardrails
- Async methods must have an `Async` suffix; prefer async/await over `.Result`/`.Wait()`.
- Never throw generic `Exception`; use structural logging and `ILogger<T>` (not `ILogger`).
- Prefer immutable types (records / readonly); annotate nullable reference types.
- Tests: NUnit + Moq + AwesomeAssertions, AAA pattern.
- Do NOT run `test/ProductConstructionService.ScenarioTests` — they require a deployed service.

## Constraints
- Keep diffs minimal and scoped to the request.
- Update or add tests for any behavior change.
- Do not modify CI, dependency versions, or security settings unless asked.
- Never print, log, or commit secrets.

## Where to find more
- Detailed conventions & build guide: `.github/copilot-instructions.md` and `docs/DevGuide.md`
- Path-specific rules: `.github/instructions/`
- Multi-step workflows: `.github/skills/*/SKILL.md`
