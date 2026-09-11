---
name: investigate-pcs-workitem-failures
description: 'Investigate failed PCS work items behind a Maestro Teams alert. Use when asked to "investigate the Maestro alert", "why did this work item fail", "look at failed work items", "check the pcs-workitem-failure-alert", or to triage Application Insights exceptions for the Product Construction Service.'
---

# Investigate PCS Work Item Failures

The PCS failure alert (`pcs-workitem-failure-alert`) posts a thread to the **Maestro** Teams channel every ~2h when work items fail. This skill maps an alert to the failing operation IDs and their root-cause exceptions.

## Prerequisites
- Before starting the investigation, verify that the WorkIQ MCP tools are available. Their availability confirms that Agency is running; if they are unavailable, ask the user to start Agency before proceeding.
- `az` CLI logged in (`az login`); access to subscription `fbd6122a-9ad3-42e4-976e-bccb82486856`.
- Extensions: `az extension add -n application-insights -y` (and `resource-graph` if discovering components).
- Teams reads use WorkIQ Graph tools (team `6e5006d7-2236-4537-809f-c2029bb60c8d`, channel `19:95e3fa449f444444a3f222babfc1b4a6@thread.tacv2`). Skip if you already have the window.

## App Insights component
- App: `product-construction-service-ai-prod`, RG: `product-construction-service`, sub: `fbd6122a-9ad3-42e4-976e-bccb82486856`.

## Step 1: Find the alert window and exact thread
The failures occurred in the **2h window ending at the thread's fire time**, in UTC. The fire time = the thread's `createdDateTime` (UTC); thread subjects/createdDateTime are UTC and the user is UTC+2, so convert when matching by local time. Example: thread fires 23:31 UTC -> query 21:31-23:31 UTC.

Before posting, always pull the messages and build an explicit mapping so you reply to the right thread:
- Fetch `messages?$select=id,subject,createdDateTime&$top=20` from the channel.
- For each alert: fire UTC = `createdDateTime`, local = UTC+2, message id, window = [fire-2h, fire].
- Adjacent alerts are 2h apart with near-identical subjects - confirm the `id` matches the intended local time before POSTing. Mismatches go to the wrong thread and cannot be deleted.

## Step 2: List failing operation IDs
Failures are `WorkItemExecuted` events at attempt 3. **`az` defaults to `--offset 1h`, which clips `ago()`/`between` ranges - always pass a wide `--offset` (e.g. `8d`).** Write KQL to a file and pass with `@`:

```kql
customEvents
| where timestamp between (datetime(START_UTC)..datetime(END_UTC))
| where name == "WorkItemExecuted"
| extend attempt = toint(customDimensions["Attempt"]), success = tobool(customDimensions["Success"])
| where success != true and attempt == 3
| project timestamp, operation = operation_Name, operationId = operation_Id
```

```pwsh
az monitor app-insights query --app product-construction-service-ai-prod `
  --resource-group product-construction-service --subscription fbd6122a-9ad3-42e4-976e-bccb82486856 `
  --offset 8d --analytics-query "@q.kql" --query "tables[0].rows" -o json
```

## Step 3: Get exception details
Query `exceptions` with the operation IDs (slightly widen the window +/-2 min):

```kql
exceptions
| where operation_Id in ("id1","id2",...)
| project timestamp, operation_Id, type, outerMessage, problemId
| order by timestamp asc
```

Group identical `type`/`outerMessage` - a single VMR/codeflow change usually fans out one root cause across many subscriptions. To see source/target, read the failing module (e.g. `VmrBackFlower.cs`) at the line in `problemId`. Always list **full** operation IDs (never truncated) so they can be looked up directly.

### Acceptable internal validation failures

Treat `BackflowNonContinuableNonLinearCodeflowException` as an **acceptable failure** when the affected subscription targets an `internal/validation/*` branch. The exception is the expected safety guard preventing backflow from resetting a validation branch with divergent history; do not classify it as an incident requiring branch realignment or retry.

In findings and Teams posts:
- State that the failures are acceptable internal-validation failures.
- Identify the targeted validation branches and explain that PCS correctly rejected unsafe non-linear backflow.
- Do not recommend retries or remediation unless the user explicitly asks for follow-up.
- Continue to investigate and escalate the same exception normally when any affected target is outside `internal/validation/*`.

## Step 4: Post findings to the Teams thread
Reply to the matching alert thread. WorkIQ `create_entity` cannot post (read-only), so use Graph via `az rest` with a Graph token. First find the message id (root subject lists fire time; pick the matching one), then POST a reply:

```pwsh
$content = '<p><b>Findings</b>: ...</p>'
@{ body = @{ contentType='html'; content=$content } } | ConvertTo-Json -Depth 5 | Out-File -Encoding utf8 "$env:TEMP\reply.json"
az rest --method post `
  --uri "https://graph.microsoft.com/v1.0/teams/6e5006d7-2236-4537-809f-c2029bb60c8d/channels/19:95e3fa449f444444a3f222babfc1b4a6@thread.tacv2/messages/<MESSAGE_ID>/replies" `
  --resource "https://graph.microsoft.com" --headers "Content-Type=application/json" --body "@$env:TEMP\reply.json"
```

- Replies allow only `messages/{id}/replies` (no nested replies). Use the alert message id as the parent.
- The available token can post but NOT delete/edit (needs `ChannelMessage.ReadWrite`), so don't post test messages - the first POST is final. Compose the real message once.
- Always list **full** operation IDs in the post (not partial/truncated) - partial IDs are useless for lookup.

## Notes
- Successes log only `Success=True`; non-attempt-3 retries don't reach the alert. Empty results almost always mean a missing `--offset`.
- `customEvents` carries failures; `exceptions` carries stack traces. There are no failed-state customEvents.
