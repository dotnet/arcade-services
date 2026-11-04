---
name: arcade-services-work-item-analysis
description: 'Analyze one failed PCS/Maestro work item by operation ID, correlate exceptions and traces with repository source, and offer a local fix, PR, further analysis, or issue creation. Use for "why did this work item fail" or "analyze this PCS operation". Teams-free; read-only until an explicit follow-up decision.'
---

# Analyze a Failed PCS Work Item

Investigate one selected work item deeply enough to explain the failure and propose a minimal fix. This skill never reads or posts to Teams and does not require WorkIQ.

## Inputs and reuse

- Accept a full recorded or telemetry operation ID and a UTC window or failure timestamp. Around a supplied timestamp, start with a two-hour window ending two minutes after it; otherwise default to the previous two hours and state that limit.
- Accept an existing failure row, ID mapping, or evidence from `arcade-services-failed-work-items` without repeating discovery.
- If no ID is supplied, invoke [arcade-services-failed-work-items](../arcade-services-failed-work-items/SKILL.md) and let the user select a failure. Do not silently pick an unrelated operation.
- Follow [shared telemetry guidance](../arcade-services-failed-work-items/references/telemetry.md).
- By default, present the analysis and decision prompt below. A calling skill may request **analysis-only** mode: return structured findings without the decision prompt or any write action. This is how `arcade-services-work-item-analysis-teams` reuses the investigation.

## Procedure

1. Retrieve evidence with one call to [Get-PcsWorkItemEvidence.ps1](./scripts/Get-PcsWorkItemEvidence.ps1) from the repository root. Use PowerShell 7 and an already authenticated Azure CLI; the Application Insights CLI extension is **not** required. Do not separately query the clock, check account/extension state, discover the resource, acquire a token, or reconstruct KQL: the helper handles validation, authentication, resource lookup, and retrieval. Reuse existing evidence instead when it already covers the requested window.

    ```powershell
    & ./.github/skills/arcade-services-work-item-analysis/scripts/Get-PcsWorkItemEvidence.ps1 -OperationId '<operation-id>'
   ```

    For a supplied failure timestamp, append `-FailureTimestampUtc '2026-09-21T14:00:00Z'`. For an explicit window, append both `-StartUtc '2026-09-21T12:00:00Z' -EndUtc '2026-09-21T14:00:00Z'`. Timestamps must be ISO 8601 UTC values ending in `Z` or `+00:00`. Without either, the helper uses the previous two hours; state that limit.

    The helper returns one JSON document with the exact `Window` and expanded `EvidenceWindow`, `ApplicationId`, full `Attempts` and `MappedIds`, window-scoped `Outcome`, `SubscriptionIds`, `ContextEvidence`, grouped exception counts and representative stacks, bounded traces, `Gaps`, and `ElapsedSeconds`. It resolves other attempts of the same recorded work item even when their telemetry IDs differ. It acquires one token in memory, uses the [Application Insights REST query API](https://learn.microsoft.com/en-us/rest/api/application-insights/query/execute), and fetches exceptions, traces, and structured context together after resolving the events. Both REST and KQL time bounds are explicit. It does not log in, switch accounts, install extensions, write telemetry to disk, or change Azure resources.

2. Interpret the helper's result before further investigation:
    - `NotFound`: report the exact window and ask for a timestamp or permission to widen it. Do not silently search unrelated operations.
    - `NeedsDisambiguation`: show the candidate recorded IDs and attempt histories, then request a selection. Rerun with `-RecordedOperationId '<selected-id>'` and the same explicit window. Never merge different work items.
    - `Found`: use `Attempts`, `Outcome`, `ExceptionGroups`, and `Traces` directly. An earlier failure followed by success is not an unresolved exhausted work item; these conclusions apply only to the returned window. Telemetry counts can include duplicate reporting or sampling effects.
    - A terminating error is an access, input, HTTP, schema, or partial-query failure, **not** a successful empty result. Report the prerequisite or evidence gap; do not change credentials or install tools.

    Keep the returned `ApplicationId` for subsequent calls using `-ApplicationId '<app-id>'` to skip the resource lookup. Read `Gaps` before generalizing. Defaults retain 20 exception groups, 12 frames per representative stack, 60 traces (higher severity first, then earliest timestamp), and 40 distinct structured context entries. When necessary, rerun the same window with `-MaxExceptionGroups`, `-MaxStackFrames`, `-MaxTraces`, or `-MaxContextEntries` increased; their maximums are 100, 100, 500, and 200. `-MaxTraces 0` omits trace samples but still reports their count and retrieves structured context. Do not dump raw dimension bags or bulk stacks. If the bounded samples remain insufficient, use a focused query following the shared telemetry guidance.

    Related skills can pass up to 50 IDs to `-OperationId` in one call, sharing authentication and the resolved window. Multiple distinct IDs return `Status: Batch`, `Results`, and `AllOperationsResolved`; each result has its own ID and status. `Error` means retrieval failed for that operation, not that it has no evidence. Disambiguation with `-RecordedOperationId` requires a single ID. When a caller supplies one of these results, reuse it and proceed to step 3; do not refetch it or run the decision prompt in analysis-only mode. Sampling gaps still apply even when `AllOperationsResolved` is true.

    Do not redact the outputs, there's no sensitive data in the service logs.

3. Identify the affected subscriptions, repositories, and branches:
    - Start with `SubscriptionIds`: validated GUIDs from named subscription dimensions across **all** correlated events, exceptions, and traces, independently of trace/context sampling (capped at 100; inspect `Gaps`). These are PCS dependency-flow subscriptions, not the Azure subscription selected by the helper's `-SubscriptionId` parameter.
    - Inspect `ContextEvidence`, which retains only allowlisted subscription, repository, branch, directory, build, channel, and updater dimensions as `Name`/`Value` pairs (so `SubscriptionId` and `subscriptionId` can coexist), plus telemetry operation ID, first/last timestamp, and occurrence count. Each entry keeps fields from the same log record together. It includes informational context even when higher-severity traces fill the trace sample. Generic `repo` or `branch` fields can describe local/intermediate state; do not assume they are the source or target.
    - If no structured subscription ID is present, inspect correlated trace messages and exception text, including serialized work-item payloads. Validate candidate GUIDs and their subscription meaning; never treat an arbitrary GUID or operation ID as a subscription. If needed, increase the bounded samples or use a focused query with the same window and correlation rules. A missing ID is a stated evidence gap, not a reason to guess.
    - Resolve confirmed IDs with the read-only command below. `--ids` accepts comma-separated GUIDs, not JSON brackets. Reuse an existing lookup within the investigation, verify returned `id` values, and inspect `sourceRepository`, `targetRepository`, `targetBranch`, `sourceDirectory`, `targetDirectory`, and `channel`. This is **current** subscription configuration, not proof of historical configuration at failure time. A source branch is not supplied by this command; report it only if independently confirmed, never infer it from the channel or target branch.

      ```powershell
      darc get-subscriptions --output-format json --ids '<subscription-id>[,<subscription-id>...]'
      ```

    - If DARC is unavailable, unauthenticated, fails, or no longer finds the subscription, report the lookup gap and retain the log-confirmed ID/context. An available read-only Maestro subscription lookup by exact ID is also acceptable. Do not install tools, change authentication, retry work items, or modify subscriptions. URLs are redacted by the helper; use the confirmed lookup rather than guessing repository names.
    - Keep all confirmed subscriptions separate when several appear; do not collapse a batched operation to the first ID. Group repeated exceptions by type/message and compare occurrences and attempts before generalizing.
4. Map the first actionable repository-owned stack frame to source. Read the implementation, a relevant caller, and nearby tests. Compare event `AppVersion` with the checkout when it identifies a deployed revision; if it is missing or inconclusive, explicitly mark the revision unknown rather than treating line numbers as authoritative.
5. Form a falsifiable root-cause hypothesis with supporting evidence, alternatives, and high/medium/low confidence. Distinguish a confirmed code defect, transient dependency failure, expected guard, and insufficient evidence.
6. Propose the smallest appropriate fix and a focused regression test/validation command without editing. Never recommend retries, branch resets, or production changes merely because an attempt failed.

## Acceptable internal-validation failures

Treat `BackflowNonContinuableNonLinearCodeflowException` as an **acceptable internal-validation failure** only when the affected subscription is confirmed to target `internal/validation/*`. PCS correctly rejected unsafe non-linear backflow into a validation branch with divergent history.

Identify the confirmed target branch and explain the guard. Do not recommend retrying, realigning the branch, or filing an incident unless explicitly requested. If the target is unknown, keep the classification unconfirmed; if it is outside `internal/validation/*`, investigate normally. Apply this per subscription, not to an entire mixed group.

When this is the case, skip most output but include the confirmed subscription ID and target repository/branch. Be concise and end with a clear statement that this is an acceptable failure and no action is recommended.

## Analysis output

Start with a compact Markdown table (`Field | Value`):
- **Subscription ID(s):** Every confirmed PCS subscription GUID in full, or `Unknown` with the evidence/lookup gap. Keep repository/branch details separate from the IDs.
- **Affected repositories and branches:** Source repository/branch when confirmed, and target repository/branch, per subscription. Identify whether these come from failure-time logs or current subscription configuration. Mark unknown fields explicitly.
- **UTC window:** The exact returned `Window`; include `EvidenceWindow` if expanded.
- **Recorded operation ID / telemetry operation IDs:** Preserve the distinction and list the relevant IDs in full.
- **Work-item type:** The type recorded by the work-item events.

Then describe:
- **Attempt/outcome history:** A record of all attempts and their outcomes.
- **Representative redacted exception/stack evidence:** Redacted evidence showing the failure.
- **Likely root cause and confidence:** The hypothesized root cause and the confidence level (high/medium/low).
- **Proposed fix/test or explanation:** The suggested remediation or an explanation of why no action is appropriate.
- **Missing evidence:** Clearly identify any missing evidence.

Be concise and direct. Use bullet points where appropriate instead of prose.

## Decision prompt

After presenting findings, use the available structured question tool (`ask_user` or `vscode_askQuestions`) with these choices, following the Build Insights exception-analysis pattern:

- `Fix the issue locally`
- `Fix the issue and open a PR`
- `Analyze another failed work item`
- `Create an issue in dotnet/arcade-services`
- `Take no action`

Do not substitute a plain-text question when a structured tool is available. If no question tool is available, present the choices and wait for a selection. Only list applicable choices. Never interpret silence as authorization. In analysis-only mode, omit this entire decision flow and return to the caller.

### Selected actions

- **Local fix:** Inspect repository instructions and working-tree state, preserve unrelated changes, implement the smallest complete fix and focused tests, and run targeted validation. Present the diff as a preview; do not branch, commit, push, or open a PR.
- **Fix and PR:** Implement and validate the same focused fix, then publish only if repository instructions permit git writes. Use the intended remote base branch so unrelated local commits are excluded, preserve unrelated changes, and follow contribution instructions. Do not switch credentials, bypass protections, or publish internal telemetry. If git writes or push access are prohibited, leave a local preview and explicitly report that no PR was opened.
- **Another work item:** Reuse the existing summary or invoke `arcade-services-failed-work-items` for the requested window, select another operation, and run this skill again.
- **Create an issue:** Synthesize a redacted title/body with impact, evidence, likely root cause/confidence, suggested fix, and validation. Exclude raw telemetry, operation IDs, private repository/branch details, secrets, and personal/customer data. Check `gh auth status --hostname github.com` without logging in or refreshing credentials, then use `gh issue create --repo dotnet/arcade-services --title $issueTitle --body $issueBody` with quoted PowerShell variables. Report the returned issue URL; on failure, provide the draft and reason without using another write mechanism. Then show the same structured decision prompt **without issue creation**, replacing `Take no action` with `Take no further action`.
- **No action:** End without modifications.

Only the two fix selections authorize source edits. Neither issue creation nor a fix selection authorizes Teams access or production remediation. Repository restrictions always take precedence.
