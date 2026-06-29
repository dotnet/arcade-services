---
name: add-darc-command
description: 'Add a new command (verb) to the DARC CLI. Use when asked to "add a darc command", "create a new darc verb", "add a darc operation", or extend the darc tool with a new subcommand.'
---

# Add a DARC CLI Command

Adds a new verb to the DARC CLI by creating a paired CommandLineOptions class and an Operation class. The `CommandLine` library auto-discovers verbs via the `[Verb]` attribute.

## When to Use

- Adding a new `darc <verb>` subcommand.
- Exposing a new BAR / configuration operation through the CLI.

## Process

### Step 1: Create the options class
Create `src/Microsoft.DotNet.Darc/Darc/Options/<Verb>CommandLineOptions.cs`.
- Decorate with `[Verb("my-verb", HelpText = "...")]`.
- Inherit the appropriate base (e.g. `CommandLineOptions<TOperation>`, `ConfigurationManagementCommandLineOptions<TOperation>`) — pick the base used by sibling commands with the same auth/context needs.
- Add `[Option(...)]` properties for each argument; mark required ones `Required = true`.
<!-- TODO: List the options this command needs and which base class is correct -->

### Step 2: Create the operation class
Create `src/Microsoft.DotNet.Darc/Darc/Operations/<Verb>Operation.cs`.
- Inherit the matching operation base used by the options' generic parameter.
- Inject services via the constructor (e.g. `IBarApiClient`, `ILogger<TOperation>`).
- Implement `protected override async Task<int> ExecuteInternalAsync()` and return a process exit code.
<!-- TODO: Document which services this operation needs and the core logic -->

### Step 3: Wire up dependencies
<!-- TODO: Confirm how options map to operations (generic type param) and whether any DI registration is required for new services. Check an existing pair like AddChannelCommandLineOptions / AddChannelOperation. -->

### Step 4: Update the documentation
Update `docs/Darc.md` so the CLI reference stays in sync (see
`.github/instructions/darc-docs.instructions.md`):
- Add a `### **\`my-verb\`**` entry under **Command Reference** documenting the options + a usage example.
- Link the new verb from the **Index** at the top of the file.
- Mirror the formatting and depth of neighboring command entries.

## Constraints
- Async methods must end in `Async`; never block with `.Result`/`.Wait()`.
- Use `ILogger<T>` and structural logging; each method logs its own actions.
- Never throw generic `Exception`.
- Match the existing options/operation base-class conventions — do not invent a new pattern.

## Validation
- `dotnet build` succeeds (warnings are errors in this repo).
- `dotnet run --project src/Microsoft.DotNet.Darc/Darc -- my-verb --help` shows the new command and its options.
- Add/extend unit tests under `test/` (NUnit + Moq + AwesomeAssertions, AAA pattern).
