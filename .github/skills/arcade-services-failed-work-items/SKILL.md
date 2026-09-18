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

1. Resolve the requested window and run the following query using the shared query procedure. Replace the timestamp placeholders with validated UTC timestamps, not arbitrary input:

   ```kql
   customEvents
   | where timestamp between (datetime(START_UTC)..datetime(END_UTC))
   | where name == "WorkItemExecuted"
   | extend Attempt = toint(customDimensions["Attempt"]),
       Success = tobool(customDimensions["Success"]),
       WorkItemType = tostring(customDimensions["WorkItemType"]),
       RecordedOperationId = tostring(customDimensions["OperationId"])
   | where Success == false and Attempt == 3
   | project timestamp, WorkItemType, Attempt,
       RecordedOperationId, TelemetryOperationId = operation_Id,
       OperationName = operation_Name
   | order by timestamp asc
   ```

2. Distinguish telemetry rows from distinct operations. Prefer the recorded work-item operation ID for grouping; use the telemetry operation ID only when the recorded ID is absent. Retain all observed mappings between the two. Keep rows with neither ID in a separate "uncorrelated" count rather than treating them as one work item.
3. Summarize counts by work-item type, first/last seen, and repeated operations. Include a table of full recorded and telemetry operation IDs with UTC timestamps. Do not infer root causes from work-item names or temporal proximity.
4. If no failures are returned, check the query's effective time range and run a compact count of `WorkItemExecuted` events grouped by raw `Attempt` and `Success` dimensions in the same window. Distinguish "no retry-exhausted failures" from missing telemetry, missing access, or a failed query. Do not silently widen the window; clearly label any separately requested wider investigation.
5. Return the summary. If a result is truncated, explicitly mark the summary as partial and split the time window to retrieve the remainder; do not report partial counts as totals. Deduplicate overlapping boundary rows before counting.

## Output

- Exact UTC window and whether results are complete.
- Telemetry-row count, distinct identified operations, and uncorrelated rows.
- Counts by work-item type and first/last failure times.
- Failure table with full recorded/telemetry operation IDs, work-item type, and timestamps.
- State that root causes have not yet been investigated. For a selected row, the reusable next step is `arcade-services-work-item-analysis`; pass both operation IDs and the window.
