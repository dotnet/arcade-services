---
applyTo: 'src/Microsoft.DotNet.Darc/DarcLib/VirtualMonoRepo/**/*.cs,test/Darc/Microsoft.DotNet.DarcLib.Codeflow.Tests/**/*.cs'
---
# Run the codeflow tests when changing codeflow
**When to read:** Changing VMR codeflow logic (`DarcLib/VirtualMonoRepo/**`) or the codeflow tests
themselves.

The default verification (`scripts/verify` / `scripts/verify.ps1`) **skips** the codeflow tests
(`Microsoft.DotNet.DarcLib.Codeflow.Tests`) because they are slow on-disk git end-to-end tests. When
your change touches codeflow, that default is not enough — run the codeflow tests explicitly:

```ps1
dotnet test test/Darc/Microsoft.DotNet.DarcLib.Codeflow.Tests
```

- These tests require `git` on PATH and create real temp git repositories; they are slower than
  ordinary unit tests, so run them deliberately rather than skipping them.
- Extend `CodeFlowTestsBase` and reuse its repo/VMR setup + `GitOperations` helper (see
  `.github/instructions/testing.instructions.md`).
- If running them is not possible in the current environment, say so explicitly and ask the user to
  run them rather than silently relying on the scoped `verify` run.
