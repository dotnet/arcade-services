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

1. Resolve the supplied ID to `WorkItemExecuted` events in the chosen window:

   ```kql
   customEvents
   | where timestamp between (datetime(START_UTC)..datetime(END_UTC))
   | where name == "WorkItemExecuted"
   | extend RecordedOperationId = tostring(customDimensions["OperationId"]),
       WorkItemType = tostring(customDimensions["WorkItemType"]),
       Attempt = toint(customDimensions["Attempt"]),
       Success = tobool(customDimensions["Success"])
   | where operation_Id == "OPERATION_ID" or RecordedOperationId == "OPERATION_ID"
   | project timestamp, WorkItemType, Attempt, Success,
       RecordedOperationId, TelemetryOperationId = operation_Id
   | order by timestamp asc
   ```

   Validate values before replacing placeholders. Retain the complete mapping and attempt history. If the telemetry ID maps to multiple recorded IDs, disambiguate rather than merging different work items. An earlier failure followed by success is not an unresolved exhausted work item.
2. Query exceptions for the mapped IDs, widening the evidence window by two minutes on each side. Expand the CLI time range as well:

   ```kql
   exceptions
   | where timestamp between (datetime(EVIDENCE_START_UTC)..datetime(EVIDENCE_END_UTC))
   | where operation_Id in ("MAPPED_ID_1", "MAPPED_ID_2")
       or tostring(customDimensions["OperationId"]) in ("MAPPED_ID_1", "MAPPED_ID_2")
   | project timestamp, operation_Id, type, outerMessage, innermostMessage,
       problemId, details
   | order by timestamp asc
   ```

   Use only nonempty, validated mapped IDs. Read representative stack details, not a bulk dump. If necessary, query `traces` with the same time/ID filters, projecting `timestamp`, `operation_Id`, `message`, and `severityLevel`. Missing correlation is an evidence gap, not proof that the work item had no exception.
3. Identify the subscription, source/target repositories, and target branch from correlated evidence or read-only subscription lookup. Do not guess them from the work-item type. Group repeated exceptions by type/message and compare occurrences and attempts before generalizing.
4. Map the first actionable repository-owned stack frame to source. Read the implementation, a relevant caller, and nearby tests. Check whether the checkout matches the deployed revision before treating line numbers as authoritative.
5. Form a falsifiable root-cause hypothesis with supporting evidence, alternatives, and high/medium/low confidence. Distinguish a confirmed code defect, transient dependency failure, expected guard, and insufficient evidence.
6. Propose the smallest appropriate fix and a focused regression test/validation command without editing. Never recommend retries, branch resets, or production changes merely because an attempt failed.

## Acceptable internal-validation failures

Treat `BackflowNonContinuableNonLinearCodeflowException` as an **acceptable internal-validation failure** only when the affected subscription is confirmed to target `internal/validation/*`. PCS correctly rejected unsafe non-linear backflow into a validation branch with divergent history.

Identify the confirmed target branch and explain the guard. Do not recommend retrying, realigning the branch, or filing an incident unless explicitly requested. If the target is unknown, keep the classification unconfirmed; if it is outside `internal/validation/*`, investigate normally. Apply this per subscription, not to an entire mixed group.

## Analysis output

Return the exact UTC window, full recorded/telemetry operation IDs, work-item type, attempt/outcome history, affected subscription and branch when known, representative redacted exception/stack evidence, likely root cause and confidence, and proposed fix/test or an explanation of why no remediation is appropriate. Clearly identify missing evidence.

## Decision prompt

After presenting findings, use the available structured question tool (`ask_user` or `vscode_askQuestions`) with these choices, following the Build Insights exception-analysis pattern:

- `Fix the issue locally (preview the fix)`
- `Fix the issue and open a PR`
- `Analyze another failed work item`
- `Create an issue in dotnet/arcade-services`
- `Take no action`

Do not substitute a plain-text question when a structured tool is available. If no question tool is available, present the choices and wait for a selection. Never interpret silence as authorization. In analysis-only mode, omit this entire decision flow and return to the caller.

### Selected actions

- **Local fix:** Inspect repository instructions and working-tree state, preserve unrelated changes, implement the smallest complete fix and focused tests, and run targeted validation. Present the diff as a preview; do not branch, commit, push, or open a PR.
- **Fix and PR:** Implement and validate the same focused fix, then publish only if repository instructions permit git writes. Use the intended remote base branch so unrelated local commits are excluded, preserve unrelated changes, and follow contribution instructions. Do not switch credentials, bypass protections, or publish internal telemetry. If git writes or push access are prohibited, leave a local preview and explicitly report that no PR was opened.
- **Another work item:** Reuse the existing summary or invoke `arcade-services-failed-work-items` for the requested window, select another operation, and run this skill again.
- **Create an issue:** Synthesize a redacted title/body with impact, evidence, likely root cause/confidence, suggested fix, and validation. Exclude raw telemetry, operation IDs, private repository/branch details, secrets, and personal/customer data. Check `gh auth status --hostname github.com` without logging in or refreshing credentials, then use `gh issue create --repo dotnet/arcade-services --title $issueTitle --body $issueBody` with quoted PowerShell variables. Report the returned issue URL; on failure, provide the draft and reason without using another write mechanism. Then show the same structured decision prompt **without issue creation**, replacing `Take no action` with `Take no further action`.
- **No action:** End without modifications.

Only the two fix selections authorize source edits. Neither issue creation nor a fix selection authorizes Teams access or production remediation. Repository restrictions always take precedence.
