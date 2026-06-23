---
applyTo: 'src/Microsoft.DotNet.Darc/Darc/Options/**/*.cs,src/Microsoft.DotNet.Darc/Darc/Operations/**/*.cs'
---
# Keeping `docs/Darc.md` in sync
**When to read:** Adding, removing, or changing a DARC command (verb) — i.e. editing any
`Options/*CommandLineOptions.cs` or `Operations/*Operation.cs` file.

`docs/Darc.md` is the user-facing reference for the DARC CLI. Its **Command Reference** section
(and the **Index** at the top) has a `### **\`<verb>\`**` entry for each command documenting its
options, behavior, and example usage.

After a change that affects the CLI surface, check whether `docs/Darc.md` needs updating and update
it in the same change:

- **New verb** (`[Verb("my-verb", ...)]` added): add a matching `### **\`my-verb\`**` entry under
  Command Reference, link it from the Index, and document its options + a usage example.
- **Renamed / removed verb:** update or remove the corresponding entry and Index link.
- **Changed options** (added/removed/renamed `[Option(...)]`, changed `Required`, changed help
  text): update the verb's documented options and examples to match.
- **No CLI-surface change** (internal refactor only): no doc update needed.

Mirror the existing formatting and depth of neighboring command entries; do not invent a new
documentation style. Documentation-only edits do not need to be built or tested.
