# Codeflow Source-Diff Verification

## Status

Proposed / design. Not yet implemented.

## Goal (high level)

When PCS opens a **forward-flow** codeflow PR (changes flowing from a source repo
into the VMR, e.g. `dotnet/efcore` -> `dotnet/dotnet`), reviewers currently have
no automated confirmation that the PR actually contains the changes from the
source build. The PR description only prints a `darc vmr diff` command they could
run by hand.

The goal of this feature is to give reviewers a **confidence signal**: after the
codeflow commit is pushed, automatically verify that the PR faithfully reflects
the source repo's commit diff (`oldSha..newSha`), and post the result as a
**follow-up PR comment**.

This is a *reporting / confidence* feature, **not** a correctness gate:

- It tells reviewers "this PR == the source diff, minus the expected exclusions
  and no-ops".
- It does **not** prove the VMR mapping configuration itself is correct, because
  it reuses the same `source-mappings.json` exclusions the codeflow used. A bug
  in that config would be invisible to this check. The comment should state this
  limitation so reviewers don't over-trust it.

## Why this is not a trivial 1:1 diff

A correct forward-flow PR is intentionally **not** a byte-for-byte copy of the
source diff. Comparing the two diffs naively produces false mismatches. The known,
legitimate sources of divergence are:

1. **Path remap** — source paths (`test/Foo.cs`) live under `src/<mapping>/` in
   the VMR (`src/efcore/test/Foo.cs`).
2. **Cloaked / excluded paths** — `source-mappings.json` `exclude` rules and
   submodules. Source-side changes to those are expected to be absent from the PR.
3. **`eng/common/**`** — Arcade-managed and not flowed per-repo (except for the
   `arcade` mapping itself).
4. **Version / metadata files** — `src/source-manifest.json`, `Version.Details.xml`,
   `Version.Details.props`, `Versions.props`, `global.json`, etc. get
   codeflow-specific content (BAR id, SHAs) that won't match the source diff.
5. **Already-at-target ("no-op") files** — the source diff changes a file, but the
   VMR copy was already at the new content (brought there by a prior flow or other
   means). Codeflow correctly produces no change for it. A name-only comparison
   would wrongly report this as "missing".
6. **Cosmetic diff-render differences** — hunk-header line offsets (`@@ -27` vs
   `@@ -24`) and trailing-newline markers (`\ No newline at end of file`). The same
   logical change renders differently because surrounding lines differ between the
   source file and its VMR copy.

The verification must therefore be *semantic* (compare the actual changed lines,
after normalizing away the items above), not textual.

## Inputs

Per forward-flow PR:

| Value           | Meaning                                                              | Source |
|-----------------|----------------------------------------------------------------------|--------|
| source repo     | e.g. `https://github.com/dotnet/efcore`                              | subscription |
| `newSha`        | source commit being flowed                                          | `build.Commit` |
| `oldSha`        | previously-flowed source commit                                    | mapping's commit in the **merge-base** `source-manifest.json` |
| `mapping`       | VMR target directory, e.g. `efcore`                                 | subscription `TargetDirectory` |
| VMR repo        | target repo URL                                                    | PR target repo |
| `headBranch`    | the codeflow PR branch                                             | PR head |
| `targetBranch`  | branch the PR targets, e.g. `main`                                 | subscription `TargetBranch` |

`oldSha...newSha` is exactly the compare range shown in the PR description's
"Commit Diff" link.

## Algorithm

Two phases. **Phase 1** cheaply enumerates and partitions the touched files;
**Phase 2** compares the actual changes only where needed.

### Phase 1 — name-only partition

Build two file sets in the same coordinate space (mapping-relative paths):

Version/metadata files that codeflow rewrites with codeflow-specific content
(BAR id, SHAs) change in **both** the source repo and the VMR but with
**different** content. They can never match in Phase 2 and would false-flag, so
they must be dropped from **both** R and A. The canonical set is
`DependencyFileManager.CodeflowDependencyFiles` =
`{ eng/Version.Details.xml, eng/Version.Details.props, global.json,
.config/dotnet-tools.json }`.

Note: `Versions.props` is deliberately **not** in that set ("In VMR repos,
Versions.props doesn't contain dependency versions maintained by automation, so
every change is meaningful"). It flows as a normal file, so we do **not** drop it
— if it was a no-op (VMR already at target) it is correctly cleared by the no-op
check in `R \ A`. The root `src/source-manifest.json` lives outside
`src/<mapping>/`, so scoping A to `src/<mapping>/` already excludes it.

- **R** ("reference", what the source diff should contribute):
  `git diff --name-only oldSha...newSha` in the **source repo**, then
  - drop paths matching the mapping's `exclude`/submodule filters
    (`GetDiffFilters` already computes these from `source-mappings.json`),
  - drop `eng/common/**` for non-arcade mappings,
  - drop the version/metadata files listed above
    (`DependencyFileManager.CodeflowDependencyFiles`).
- **A** ("actual", what the PR changed under the mapping):
  `git diff --name-only targetBranch...headBranch -- src/<mapping>/` in the
  **VMR**, then strip the `src/<mapping>/` prefix, and
  - drop `eng/common/**` for non-arcade mappings,
  - drop the same version/metadata files.

Partition:

| Bucket    | Meaning                              | Action |
|-----------|--------------------------------------|--------|
| `R ∩ A`   | touched in both                      | Phase 2 content compare |
| `R \ A`   | source changed it, PR did not        | no-op file-state check (below) |
| `A \ R`   | changed in PR only                   | **must be empty** — else flag (codeflow wrote changes not traceable to the source diff) |

> **Why `A \ R` must be empty.** The VMR diff is three-dot
> (`targetBranch...headBranch`), so it contains only the changes the PR
> introduced — not unrelated edits already on the target branch. The comment is
> posted right after PCS pushes the branch, before any human edits the PR. So
> after the same exclusions are applied to `A` (version/metadata files and, for
> non-arcade mappings, `eng/common/**`), any remaining file the PR changed under
> `src/<mapping>/` that is absent from the source diff means the codeflow
> produced a change that does not come from the source — a correctness red flag,
> not an expected divergence. (This is what lets the signal *guarantee* the
> codeflow reproduced exactly the source diff, rather than merely "contains" it.)

### Phase 2 — per-file content compare (`R ∩ A`)

For each file, scope a zero-context diff to that single file in each repo:

```bash
git -C <source> diff -U0 oldSha...newSha            -- <path>
git -C <vmr>    diff -U0 targetBranch...headBranch  -- src/<mapping>/<path>
```

Normalize each by removing the lines that are *expected* to differ, then compare
what remains:

- Strip `diff --git`, `index `, `@@ `, `--- `, `+++ ` (this is exactly
  `CodeflowChangeAnalyzer.IgnoredDiffLines`). With `-U0` there are no context
  lines, so only `+`/`-` change lines remain.
- Compare the remaining `+`/`-` lines.
  - equal -> **match** (change applied faithfully).
  - different -> **flag**: "changes don't match".

Decision to record: whether to strip trailing-newline markers
(`\ No newline at end of file`). They are a genuine byte difference; for mapped
source files we keep them (only observed on excluded version files so far).

### No-op check (`R \ A`)

A file in `R \ A` is only legitimate if the VMR is **already at the source's new
state**. Diff content can't distinguish "correctly no-op'd" from "wrongly
dropped", so compare the resulting **file state**:

```
content(VMR_head : src/<mapping>/<file>)   vs   content(source@newSha : <file>)
```

- equal, **or both absent** (deletion already reconciled) -> legitimate no-op,
  ignore.
- different -> **flag**: "changes don't match".

## Output

The goal is simple: run the check and post a short PR comment expressing our
confidence. It is a positive/negative signal, not a detailed report.

- Empty flag set (`R ∩ A` all match, `R \ A` all no-ops, `A \ R` empty) => post a
  short confirmation, e.g.:

  ```
  ### Source diff verification
  ✅ This PR matches the source diff 60e46e1...5932bea of dotnet/wpf.
  ```

- Anything flagged (mismatching content or unexpected files) => post a short
  "couldn't verify, please review manually" comment, e.g.:

  ```
  ### Source diff verification
  ⚠️ Couldn't verify that this PR matches the source diff 60e46e1...5932bea of
  dotnet/wpf. Please review the changes manually.
  ```

Both comments carry a footnote that this is a best-effort signal validated under
the current source-mappings config.

## Reuse / where it plugs in

- **`CodeflowChangeAnalyzer`** (`DarcLib/VirtualMonoRepo`) already computes the
  VMR-side diff against the merge base, classifies source vs version/metadata
  files, handles the non-arcade `eng/common` exclusion, and defines
  `IgnoredDiffLines`. This feature lives **inside** `CodeflowChangeAnalyzer` as
  `VerifyForwardFlowAsync` and reuses these helpers/constants directly.
- **`GetDiffFilters`** (`Darc/Operations/VirtualMonoRepo/VmrDiffOperation.cs`)
  already turns `source-mappings.json` excludes + submodules into git pathspec
  exclusion rules; reuse it to filter **R**.
- **`ForwardFlowMergePolicy`** already obtains `mergeBase`, head/merge-base
  `SourceManifest`s, and validates them; the SHAs and manifests this feature
  needs are already available at evaluation time.
- PCS already has the VMR cloned locally during flow
  (`ILocalGitRepoFactory`); the source `oldSha...newSha` is available either from
  the patch codeflow already built or via a remote diff call.

The analyzer returns the bucketed result (list of mismatching files; empty =
match). PCS renders it into a follow-up PR comment, separate from PR creation so
it never slows down or fails the codeflow push.

## Scope / limitations

- Trusts `R ∩ A` on presence + content-line equality; it does not re-run the
  3-way merge, so it validates "PR reflects the source diff under the current
  mapping config", not "the config is correct".
- `A \ R` must be empty after the standard exclusions: a non-empty set means the
  codeflow changed files under `src/<mapping>/` that don't trace back to the
  source diff. This assumes the check runs right after PCS pushes, before a human
  adds conflict-resolution or unrelated commits to the PR branch (which would also
  show up in `A`); that is the only intended call site.
- Large flows (e.g. `runtime`) can touch thousands of files; Phase 1 is name-only
  and cheap, but Phase 2/no-op content fetches should be bounded (only `R ∩ A`
  and `R \ A`). Consider a size cap or running fully asynchronously as a comment.

## Validation done so far

Manually verified the algorithm by hand against:

- **dotnet/dotnet#7107** (efcore, small): R = A = 5 files, `R \ A` empty, all 5
  files match content 1:1.
- **dotnet/dotnet#7142** (wpf, larger): applying the full algorithm —
  `R∩A` = 40 files (all content-match), `R\A` = 2 files
  (`.github/policies/resourceManagement.yml`, `eng/restore-toolset.ps1`), both
  confirmed genuine no-ops (VMR head equals source@newSha), and `A\R` = 0 after
  exclusions — the 17 PR-only files were all `eng/common/**`, which is dropped
  from `A` for non-arcade mappings before the `A\R` check, so nothing remained to
  flag. No mismatches flagged. Version files
  (`Versions.props`, `Version.Details.xml`, `global.json`, ...) were excluded from
  both sides as designed.
