---
name: dotnet-servicing-cqb-status
description: 'Determine the end-to-end state of a .NET 8 or .NET 9 servicing CQB/QB by resolving the latest runtime BAR build and tracing it through dependency-flow PRs and Azure DevOps CI builds. Use when asked "where is the .NET 8/9 servicing build", "check CQB status", or "what is blocking the build". Only evaluates AzDO PRs created by ProductConstructionServiceProd.'
compatibility: 'Requires DARC/BAR access and read access to the dnceng internal Azure DevOps project.'
---

# .NET Servicing CQB Status

Resolve the latest runtime build for a selected .NET 8/9 servicing train, trace it through the servicing graph, and report whether every node has a successful, coherent build containing all required parent builds.

This skill is read-only. It diagnoses and recommends actions; it does not merge PRs, queue builds, trigger subscriptions, or alter release state.

## Required input and scope

1. Require only the servicing product major: **.NET 8** or **.NET 9**. If neither can be inferred from the request, ask which one and stop.
2. Resolve the root runtime build:

   ```powershell
   darc get-latest-build --repo runtime --channel ".NET 8 Internal"
   darc get-latest-build --repo runtime --channel ".NET 9 Internal"
   ```

   Run only the command matching the selected product major. If the user explicitly supplies a runtime BAR build ID, use `darc get-build --id <runtime-build-id>` instead for a reproducible historical investigation.
3. Confirm that the resolved build:
   - comes from the runtime repository;
   - belongs to .NET 8 or .NET 9 servicing;
   - exposes enough branch/channel information to select the servicing branches.
4. Stop as out of scope for .NET 10 or later. Do not reinterpret a .NET 10+ build as a legacy CQB.
5. Use [the repository graph](../../../docs/dotnet-graph.mmd) as the authoritative topology and [the servicing repository reference](references/repositories.md) for AzDO repositories and CI definitions.

## Non-negotiable PR safety rule

Only inspect, evaluate, recommend action on, or otherwise use an Azure DevOps PR after confirming its creator is exactly `ProductConstructionServiceProd`.

- Retrieve only the minimum PR identity metadata needed to check the creator, then validate the creator before reading changes, policies, or build status.
- If creator identity cannot be confirmed, treat the PR as unusable evidence.
- If a relevant PR was created by anyone else, label it `Unsupported PR creator`; do not inspect it further and do not suggest merging or modifying it.
- Never broaden the investigation to unrelated AzDO PRs.

## Branch selection

- Derive the servicing major from the root build's branch/channel instead of asking the user to repeat it.
- For product repositories, inspect the matching `internal/release/<major>.0` branch.
- For SDK, inspect every active servicing band branch matching `internal/release/<major>.0.<band>xx`, including `1xx`.
- Inspect installer bands only when the authoritative graph includes installer nodes for the selected product major; the current graph includes them for .NET 8, not .NET 9.
- Discover active bands from Maestro subscriptions/builds for the selected channel and branch family. Do not assume only one SDK band exists.
- Preserve the branch spelling returned by DARC/Maestro/AzDO. Do not silently substitute `release/*` for `internal/release/*`, or vice versa.

## Investigation workflow

### 1. Establish the root identity

Record the root runtime build's:

- BAR build ID and build number;
- repository and commit;
- branch and channel;
- released state;
- product major.

Prefer fresh Maestro data when equivalent tools are available. `maestro_build` and `maestro_build_graph` can supplement DARC, but the DARC-resolved runtime BAR build remains the investigation root.

### 2. Expand the graph

Evaluate nodes in topological order:

1. `runtime`
2. `efcore`, `winforms`
3. `aspnetcore`, `wpf`
4. `windowsdesktop`
5. every active SDK band
6. for .NET 8, the installer for each applicable SDK band

Direct parent requirements are defined only by `docs/dotnet-graph.mmd`. A child is not complete until one successful child build contains all required direct-parent builds.

### 3. Trace each parent-to-child flow

For every graph edge:

1. Find the matching Maestro subscription for the selected channel and target branch.
2. Find its dependency-flow PR.
3. Apply the `ProductConstructionServiceProd` creator check before using the PR.
4. Record whether the trusted PR is absent, open, abandoned, conflicted, or merged.
5. For a merged PR, record the merge commit and completion time.
6. Resolve the BAR build for relevant repository commits when needed:

   ```powershell
   darc get-build --repo <repository-url> --commit <commit>
   ```

Use `maestro_subscriptions`, `maestro_codeflow_pr`, subscription history/outcomes, and build graph tools when available. Use uncached/fresh queries when cached state conflicts with AzDO.

### 4. Correlate Azure DevOps CI

Use the CI definition from `references/repositories.md`.

For each target branch:

1. Find the latest branch commit.
2. Find CI runs for the exact pipeline definition and branch.
3. Check whether the latest branch commit has a run. Missing coverage may indicate a misconfigured or missed pipeline trigger.
4. For completeness, select a successful build whose source commit contains every required merged dependency-flow PR.
5. Record builds that are queued, running, failed, canceled, or missing.
6. Do not treat a successful build as sufficient merely because it is newer by time; verify parent-build containment through BAR build/dependency data.

Use `azdo_builds` for definition/branch lookup and `azdo_build` for a selected run when available. Query organization `dnceng`, project `internal`.

### 5. Determine node completeness

A node is `Complete` only when:

- every direct parent has a trusted PCS dependency-flow PR merged into the selected branch;
- a successful CI build contains all those parent builds;
- its transitive runtime provenance satisfies the coherence rule below.

Use these intermediate states:

| State | Meaning |
|---|---|
| `Waiting for PR` | No trusted PCS PR exists yet. |
| `Waiting for merge` | A trusted PCS PR is open and otherwise usable. |
| `PR blocked` | The trusted PCS PR is conflicted, abandoned, or policy-blocked. |
| `Waiting for build` | Required PRs merged, but no qualifying CI build completed. |
| `Build running` | A qualifying run is queued or in progress. |
| `Build failed` | The qualifying/latest required run failed or was canceled. |
| `Trigger missing` | The latest branch commit has no run for the expected definition. |
| `Incomplete inputs` | A build exists but does not contain every required parent. |
| `Incoherent` | Runtime provenance violates the coherence rule. |
| `Complete` | All conditions are satisfied. |

### 6. Enforce runtime coherence

The final product build must have one active runtime provenance everywhere.

- Compare runtime BAR build identity/provenance throughout the graph, not just timestamps or superficially similar package versions.
- Multiple runtime builds may coexist only when every runtime build except the selected root is marked released.
- An unreleased runtime build different from the selected root makes the affected node and downstream product `Incoherent`.
- A direct `runtime -> aspnetcore` PR alone does not complete aspnetcore when the graph also requires `efcore -> aspnetcore`. Wait for a successful aspnetcore build containing both parent flows.
- Do not hide incoherence behind a newer green CI run.

## Recommended actions

Recommend the smallest concrete next action:

| Condition | Recommendation |
|---|---|
| Trusted PR open | Validate and merge the linked PCS PR. |
| Trusted PR conflicted/policy-blocked | Resolve the reported PR blocker; do not bypass policy. |
| Expected trusted PR absent | Check the matching subscription outcome and trigger the specific subscription if authorized. |
| PR merged, build queued/running | Wait for the linked run. |
| PR merged, latest commit has no run | Manually queue the repository's listed CI definition and investigate its branch trigger. |
| Qualifying build failed | Investigate the linked run and its first actionable failure. |
| Green build lacks required parent | Wait for/flow the missing parent, then produce a new child build. |
| Runtime provenance incoherent | Stop downstream advancement; complete the missing flows or replace stale unreleased inputs before rebuilding. |

Never perform a recommended write action without a separate explicit user request.

## Output format

Lead with `CQB status: Complete`, `In progress`, `Blocked`, or `Incoherent`, followed by the root runtime build and selected branch/channel.

Then emit one row per repository/branch:

| Node / branch | Required parents | Trusted PCS PRs | Qualifying CI build | Runtime provenance | State | Next action |
|---|---|---|---|---|---|---|
| `aspnetcore` / `internal/release/8.0` | runtime, efcore | Linked PR IDs and merged/open state | Linked run ID and result, or missing | Selected runtime build ID; list other unreleased IDs | `Waiting for build` | Wait for/build after both PRs merge |

Requirements:

- Use clickable AzDO PR/build links.
- Include every active SDK and installer band as a separate row.
- Name the missing parent or exact blocker; never say only "waiting".
- Distinguish no PR, open PR, merged PR without build, running build, failed build, trigger missing, incomplete inputs, and incoherence.
- Add a short **Critical path** line naming the first unresolved graph edge(s).
- Add a short **Actions** list ordered by dependency critical path, not by repository name.
- State evidence gaps explicitly. Never infer `Complete` from missing data.
