---
name: arcade-services-failed-work-items
description: 'List failed Product Construction Service (PCS/Maestro) work items in a time window and summarize retry-exhausted operations. Use for "list failed work items", "summarize PCS failures", or "what failed in Maestro". Teams-free and read-only; use arcade-services-work-item-analysis for root-cause investigation.'
---

# List Failed PCS Work Items

Discover retry-exhausted work items and produce a compact summary, without requiring or accessing Teams.

## Inputs and boundaries

- Accept explicit UTC start/end timestamps or a positive lookback; default to the previous two hours. Resolve relative times once and report the exact UTC boundaries.
- When another skill calls this one, honor its window and return the summary and full operation identifiers to the caller. Do not launch another skill or prompt for follow-up automatically.
- Follow [shared telemetry guidance](references/telemetry.md) for the production target, authentication, query execution, correlation, and safety rules.
- Do not change source, production state, subscriptions, or retry work items. Do not access or post to Teams.

## Procedure

1. Run the shared [telemetry helper](../arcade-services-work-item-analysis/scripts/Get-PcsWorkItemEvidence.ps1) in listing mode from the repository root. Use PowerShell 7 and an already authenticated Azure CLI; the Application Insights extension is not required. Do not separately query the clock, check account/extension state, discover the resource, acquire a token, or reconstruct KQL. The helper performs those deterministic steps and returns one JSON document:

    ```powershell
    & ./.github/skills/arcade-services-work-item-analysis/scripts/Get-PcsWorkItemEvidence.ps1 -ListFailures
   ```

    Append `-LookbackHours 6` for a requested relative lookback, or both `-StartUtc '2026-09-21T12:00:00Z' -EndUtc '2026-09-21T14:00:00Z'` for an explicit window. Timestamps must be ISO 8601 UTC values ending in `Z` or `+00:00`. Without either, the helper resolves the previous two hours once. Preserve its `Window` and `ApplicationId` for follow-up calls; `-ApplicationId '<app-id>'` skips resource lookup.

2. Use the returned `Counts`, `ByWorkItemType`, and `Failures` directly. A single REST query retrieves retry-exhausted failure rows, exact full-window aggregate counts, and raw `Attempt`/`Success` `DimensionCounts`. Grouping prefers recorded IDs, falls back to telemetry IDs only when recorded IDs are absent, and counts rows with neither ID separately. Counts describe telemetry rows and identified operations, not necessarily unique subscriptions or unresolved outcomes. Keep all returned ID mappings; do not infer root causes from names or proximity. A selected operation still needs its attempt history checked for later success by the analysis skill.
3. Interpret `Status` before summarizing:
    - `Found`: report the failure table and full-window counts.
    - `NoFailures`: events exist, but none match `Success == false and Attempt == 3`; use `DimensionCounts` for the already-fetched diagnostic breakdown.
    - `NoTelemetry`: there are no `WorkItemExecuted` events in the window; do not claim that no work items failed.
    - `Partial`: full-window counts are available, but the failure table is truncated. Read `Gaps` and `RowsComplete`.
    - A terminating error is an input, access, HTTP, schema, or partial-query failure, not an empty successful result. Report the blocker without changing credentials or installing tools.
4. The default failure table cap is 200 rows. For a complete table, rerun the **same exact window** with `-MaxRows` increased (maximum 5000), or split the window and deduplicate overlapping boundary rows using `EventId`. `CountsComplete` covers server-computed aggregates; `RowsComplete` covers the returned table and ID mappings. Do not count the sampled table as the total or sum distinct-operation counts across subwindows, since one operation can span them. Never silently widen the window.
5. Return the summary. Root causes remain uninvestigated; reuse the returned data when another skill is the caller. Results and credentials stay in memory. Redaction is best-effort, so review sensitive fields before sharing. Use manual queries only for evidence this helper does not provide.

## Output

- Exact UTC window and whether results are complete.
- Telemetry-row count, distinct identified operations, and uncorrelated rows.
- Counts by work-item type and first/last failure times.
- Failure table with full recorded/telemetry operation IDs, work-item type, and timestamps.
- State that root causes have not yet been investigated. For a selected row, the reusable next step is `arcade-services-work-item-analysis`; pass both operation IDs, the window, and the returned `ApplicationId`.
