# AGENTS.md

## Verification
- Build & test: run `scripts/verify` (bash) or `scripts/verify.ps1` (PowerShell). These run `dotnet build` then a scoped `dotnet test` that excludes scenario tests (require a deployed service) and the slow codeflow tests.
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
- Do not run git write commands (`git commit`, `git push`, etc.); leave staging and committing to the user.

## Learning from corrections
- When the user corrects you, rejects an approach, or states a durable preference or convention, store it with Copilot Memory (the `store_memory` tool) so it persists across sessions.
- Only store durable, generally-applicable facts — not ephemeral, task-specific instructions ("for this PR…", "just this once…").
- Before storing, check the surfaced memories; if a similar fact exists, upvote/refine it instead of adding a near-duplicate.
- Keep each memory atomic and short, and don't store anything already covered by AGENTS.md, `.github/instructions/`, or inferable from code — prefer in-repo docs for team conventions, reserving memory for cross-session preferences.
- If a correction contradicts an existing memory, update it: downvote the outdated memory and store the corrected fact.

## Where to find more
- Detailed conventions & build guide: `.github/copilot-instructions.md` and `docs/DevGuide.md`
- Path-specific rules: `.github/instructions/`
- Multi-step workflows: `.github/skills/*/SKILL.md`
