#Requires -Version 7.0
[CmdletBinding(DefaultParameterSetName = 'Recent')]
param(
    [Parameter(ParameterSetName = 'Recent', Mandatory)]
    [Parameter(ParameterSetName = 'AtTime', Mandatory)]
    [Parameter(ParameterSetName = 'Window', Mandatory)]
    [ValidatePattern('^[A-Za-z0-9-]+$')]
    [ValidateLength(1, 128)]
    [ValidateCount(1, 50)]
    [string[]]$OperationId,

    [Parameter(ParameterSetName = 'ListRecent', Mandatory)]
    [Parameter(ParameterSetName = 'ListWindow', Mandatory)]
    [switch]$ListFailures,

    [Parameter(ParameterSetName = 'ListRecent')]
    [ValidateRange(0.001, 8760)]
    [double]$LookbackHours = 2,

    [Parameter(ParameterSetName = 'ListRecent')]
    [Parameter(ParameterSetName = 'ListWindow')]
    [ValidateRange(1, 5000)]
    [int]$MaxRows = 200,

    [Parameter(ParameterSetName = 'AtTime', Mandatory)]
    [string]$FailureTimestampUtc,

    [Parameter(ParameterSetName = 'Window', Mandatory)]
    [Parameter(ParameterSetName = 'ListWindow', Mandatory)]
    [string]$StartUtc,

    [Parameter(ParameterSetName = 'Window', Mandatory)]
    [Parameter(ParameterSetName = 'ListWindow', Mandatory)]
    [string]$EndUtc,

    [Parameter(ParameterSetName = 'Recent')]
    [Parameter(ParameterSetName = 'AtTime')]
    [Parameter(ParameterSetName = 'Window')]
    [ValidatePattern('^[A-Za-z0-9-]+$')]
    [ValidateLength(1, 128)]
    [string]$RecordedOperationId,

    [guid]$SubscriptionId = 'fbd6122a-9ad3-42e4-976e-bccb82486856',
    [ValidatePattern('^[A-Za-z0-9_.()-]+$')]
    [string]$ResourceGroup = 'product-construction-service',
    [ValidatePattern('^[A-Za-z0-9_.()-]+$')]
    [string]$ApplicationName = 'product-construction-service-ai-prod',
    [guid]$ApplicationId,

    [ValidateRange(1, 100)]
    [int]$MaxExceptionGroups = 20,
    [ValidateRange(0, 500)]
    [int]$MaxTraces = 60,
    [ValidateRange(1, 100)]
    [int]$MaxStackFrames = 12,
    [ValidateRange(1, 300)]
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
$timer = [System.Diagnostics.Stopwatch]::StartNew()

function ConvertTo-UtcTimestamp([string]$Value) {
    $parsed = [DateTimeOffset]::MinValue
    if ($Value -notmatch '(Z|\+00:00)$' -or
        -not [DateTimeOffset]::TryParse($Value, [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::None, [ref]$parsed)) {
        throw 'Timestamps must be ISO 8601 UTC values ending in Z or +00:00.'
    }
    $parsed.ToUniversalTime()
}

function Format-UtcTimestamp([DateTimeOffset]$Value) {
    $Value.UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ss.fffZ', [System.Globalization.CultureInfo]::InvariantCulture)
}

function Invoke-AzureCli([string[]]$Arguments) {
    $output = & az @Arguments --only-show-errors 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'Azure CLI access failed. An existing authenticated session with read access is required; no credentials or account context were changed.'
    }
    ($output -join "`n").Trim()
}

function Invoke-TelemetryQuery([string]$Query, [string]$FromUtc, [string]$ToUtc, [string[]]$Columns) {
    $body = @{ query = $Query; timespan = "$FromUtc/$ToUtc" } | ConvertTo-Json -Compress
    try {
        $response = Invoke-RestMethod -Method Post -Uri $queryUri -Headers $headers -ContentType 'application/json' -Body $body -TimeoutSec $TimeoutSeconds -Verbose:$false -Debug:$false
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode
        throw "Application Insights query failed (HTTP $statusCode). Check read access, query syntax, and service availability. Response details are suppressed to avoid exposing telemetry."
    }
    if ($response.error) {
        throw 'Application Insights returned an error or partial result; this is not an empty successful query.'
    }
    $tables = @($response.tables | Where-Object { $_.name -eq 'PrimaryResult' })
    if ($tables.Count -ne 1 -or ($tables[0].columns.name -join ',') -cne ($Columns -join ',')) {
        throw 'Application Insights returned an unexpected result schema.'
    }
    foreach ($row in $tables[0].rows) {
        if ($row.Count -ne $Columns.Count) {
            throw 'Application Insights returned an incomplete row.'
        }
        $record = [ordered]@{}
        for ($columnIndex = 0; $columnIndex -lt $Columns.Count; $columnIndex++) {
            $record[$Columns[$columnIndex]] = $row[$columnIndex]
        }
        [pscustomobject]$record
    }
}

function Protect-EvidenceValue($Value) {
    if ($Value -is [string]) {
        $text = $Value -replace '(?i)https?://[^\s<>"'']+', '[URL REDACTED]'
        $text = $text -replace '(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b', '[EMAIL REDACTED]'
        $text = $text -replace '(?i)\bBearer\s+\S+', 'Bearer [REDACTED]'
        $text = $text -replace '\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b', '[TOKEN REDACTED]'
        $text = $text -replace '(?i)\b(?:gh[pousr]_[A-Za-z0-9_]+|github_pat_[A-Za-z0-9_]+)\b', '[TOKEN REDACTED]'
        $text = $text -replace '(?i)(\b(?:password|pwd|secret|token|access_token|client_secret|AccountKey|SharedAccessKey|sig)\b\s*[=:]\s*)("[^"]*"|''[^'']*''|[^\s;,]+)', '$1[REDACTED]'
        $text = $text -replace '(?i)([A-Z]:\\Users\\|/home/|/Users/)[^\\/\s]+', '$1[USER REDACTED]'
        return $text
    }
    if ($Value -is [System.Collections.IDictionary]) {
        $redacted = [ordered]@{}
        foreach ($key in $Value.Keys) { $redacted[$key] = Protect-EvidenceValue $Value[$key] }
        return $redacted
    }
    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        $redacted = [ordered]@{}
        foreach ($property in $Value.PSObject.Properties) { $redacted[$property.Name] = Protect-EvidenceValue $property.Value }
        return $redacted
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        return ,@($Value | ForEach-Object { Protect-EvidenceValue $_ })
    }
    return $Value
}

if ($PSCmdlet.ParameterSetName -in @('Window', 'ListWindow')) {
    $start = ConvertTo-UtcTimestamp $StartUtc
    $end = ConvertTo-UtcTimestamp $EndUtc
}
elseif ($PSCmdlet.ParameterSetName -eq 'AtTime') {
    $end = (ConvertTo-UtcTimestamp $FailureTimestampUtc).AddMinutes(2)
    $start = $end.AddHours(-2)
}
else {
    $end = [DateTimeOffset]::UtcNow
    $start = $end.AddHours(-$LookbackHours)
}
if ($start -ge $end) { throw 'StartUtc must precede EndUtc.' }
$startText = Format-UtcTimestamp $start
$endText = Format-UtcTimestamp $end
$evidenceStart = Format-UtcTimestamp $start.AddMinutes(-2)
$evidenceEnd = Format-UtcTimestamp $end.AddMinutes(2)

if ($RecordedOperationId -and $OperationId.Count -gt 1) {
    throw 'RecordedOperationId can only disambiguate a single OperationId.'
}
if (-not (Get-Command az -ErrorAction SilentlyContinue)) { throw 'Azure CLI must already be installed and authenticated.' }
if (-not $PSBoundParameters.ContainsKey('ApplicationId')) {
    $resourceId = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Insights/components/$ApplicationName"
    $appIdText = Invoke-AzureCli @('resource', 'show', '--ids', $resourceId, '--api-version', '2020-02-02', '--query', 'properties.AppId', '--output', 'tsv')
    $resolvedId = [guid]::Empty
    if (-not [guid]::TryParse($appIdText, [ref]$resolvedId) -or $resolvedId -eq [guid]::Empty) {
        throw 'Could not resolve the Application Insights application ID.'
    }
    $ApplicationId = $resolvedId
}
if ($ApplicationId -eq [guid]::Empty) { throw 'ApplicationId must not be empty.' }

$token = Invoke-AzureCli @('account', 'get-access-token', '--subscription', "$SubscriptionId", '--resource', 'https://api.applicationinsights.io', '--query', 'accessToken', '--output', 'tsv')
if ([string]::IsNullOrWhiteSpace($token)) { throw 'Azure CLI did not return an access token.' }
$headers = @{ Authorization = "Bearer $token" }
$queryUri = "https://api.applicationinsights.io/v1/apps/$ApplicationId/query"
try {
    if ($ListFailures) {
        $listingQuery = @"
let events = materialize(customEvents
| where timestamp between (datetime($startText)..datetime($endText))
| where name == 'WorkItemExecuted'
| extend RawAttempt = tostring(customDimensions['Attempt']), RawSuccess = tostring(customDimensions['Success']),
    Attempt = toint(customDimensions['Attempt']), Success = tobool(customDimensions['Success']),
    WorkItemType = tostring(customDimensions['WorkItemType']), RecordedOperationId = tostring(customDimensions['OperationId'])
| extend OperationKey = case(isnotempty(RecordedOperationId), strcat('recorded:', RecordedOperationId),
    isnotempty(operation_Id), strcat('telemetry:', operation_Id), ''));
let failures = materialize(events | where Success == false and Attempt == 3);
let operations = materialize(failures | where isnotempty(OperationKey)
| summarize FailureRows = count(), FirstFailureUtc = min(timestamp), LastFailureUtc = max(timestamp) by OperationKey);
let byType = failures
| summarize FailureRows = count(), FirstFailureUtc = min(timestamp), LastFailureUtc = max(timestamp) by WorkItemType, OperationKey
| summarize TelemetryRows = sum(FailureRows), IdentifiedOperations = countif(isnotempty(OperationKey)),
    UncorrelatedRows = sumif(FailureRows, isempty(OperationKey)), RepeatedOperations = countif(isnotempty(OperationKey) and FailureRows > 1),
    FirstFailureUtc = min(FirstFailureUtc), LastFailureUtc = max(LastFailureUtc) by WorkItemType;
union
(failures | order by timestamp asc, itemId asc | take $MaxRows
| project Kind = 'Failure', Payload = bag_pack('Timestamp', timestamp, 'EventId', itemId, 'WorkItemType', WorkItemType,
    'Attempt', Attempt, 'RecordedOperationId', RecordedOperationId, 'TelemetryOperationId', operation_Id, 'OperationName', operation_Name)),
(byType | project Kind = 'WorkItemType', Payload = bag_pack('WorkItemType', WorkItemType, 'TelemetryRows', TelemetryRows,
    'IdentifiedOperations', IdentifiedOperations, 'UncorrelatedRows', UncorrelatedRows, 'RepeatedOperations', RepeatedOperations,
    'FirstFailureUtc', FirstFailureUtc, 'LastFailureUtc', LastFailureUtc)),
(events | summarize Rows = count() by RawAttempt, RawSuccess
| project Kind = 'Dimensions', Payload = bag_pack('Attempt', RawAttempt, 'Success', RawSuccess, 'Rows', Rows)),
(print Kind = 'Counts', Payload = bag_pack('WorkItemExecutedRows', toscalar(events | count),
    'FailureRows', toscalar(failures | count), 'IdentifiedOperations', toscalar(operations | count),
    'UncorrelatedRows', toscalar(failures | where isempty(OperationKey) | count),
    'RepeatedOperations', toscalar(operations | where FailureRows > 1 | count),
    'FirstFailureUtc', toscalar(failures | summarize min(timestamp)), 'LastFailureUtc', toscalar(failures | summarize max(timestamp))))
"@
        $listing = @(Invoke-TelemetryQuery $listingQuery $startText $endText @('Kind', 'Payload'))
        foreach ($record in $listing) {
            if ($record.Payload -is [string]) { $record.Payload = $record.Payload | ConvertFrom-Json }
        }
        $counts = @($listing | Where-Object Kind -eq 'Counts')
        if ($counts.Count -ne 1) { throw 'Listing counts are missing; the result may be incomplete.' }
        $failures = @($listing | Where-Object Kind -eq 'Failure' | ForEach-Object Payload | Sort-Object Timestamp, EventId)
        $result = [ordered]@{
            Status = 'Found'
            ApplicationId = "$ApplicationId"
            Window = @{ StartUtc = $startText; EndUtc = $endText }
            CountsComplete = $true
            RowsComplete = $failures.Count -eq $counts[0].Payload.FailureRows
            Counts = $counts[0].Payload
            ByWorkItemType = @($listing | Where-Object Kind -eq 'WorkItemType' | ForEach-Object Payload | Sort-Object WorkItemType)
            DimensionCounts = @($listing | Where-Object Kind -eq 'Dimensions' | ForEach-Object Payload | Sort-Object Attempt, Success)
            Failures = $failures
            Gaps = @()
            ElapsedSeconds = [math]::Round($timer.Elapsed.TotalSeconds, 2)
            Redaction = 'Best-effort only. Review before sharing. Failure rows are not a determination of unresolved outcomes or root causes.'
        }
        if ($result.Counts.WorkItemExecutedRows -eq 0) {
            $result.Status = 'NoTelemetry'
            $result.Gaps += 'No WorkItemExecuted events were found. This does not establish that no work items failed.'
        }
        elseif ($result.Counts.FailureRows -eq 0) { $result.Status = 'NoFailures' }
        if (-not $result.RowsComplete) {
            $result.Status = 'Partial'
            $result.Gaps += 'Failure rows truncated. Counts cover the full window; increase MaxRows or split the window before claiming a complete failure table. Deduplicate overlapping rows using EventId.'
        }
        Protect-EvidenceValue $result | ConvertTo-Json -Depth 30
        return
    }

    function Get-OperationEvidence([string]$OperationId) {
        $eventQuery = @"
let events = materialize(customEvents
| where timestamp between (datetime($startText)..datetime($endText))
| where name == 'WorkItemExecuted'
| extend RecordedOperationId = tostring(customDimensions['OperationId']),
    WorkItemType = tostring(customDimensions['WorkItemType']),
    Attempt = toint(customDimensions['Attempt']), Success = tobool(customDimensions['Success']));
let matchingIds = events
| where operation_Id == '$OperationId' or RecordedOperationId == '$OperationId'
| where isnotempty(RecordedOperationId)
| distinct RecordedOperationId;
events
| where operation_Id == '$OperationId' or RecordedOperationId == '$OperationId' or RecordedOperationId in (matchingIds)
| project timestamp, WorkItemType, Attempt, Success, RecordedOperationId, TelemetryOperationId = operation_Id, AppVersion = application_Version
| order by timestamp asc
"@
        $attempts = @(Invoke-TelemetryQuery $eventQuery $startText $endText @('timestamp', 'WorkItemType', 'Attempt', 'Success', 'RecordedOperationId', 'TelemetryOperationId', 'AppVersion'))
        $candidateIds = @($attempts.RecordedOperationId | Where-Object { $_ } | Sort-Object -Unique)
        $result = [ordered]@{
            Status = 'NotFound'
            OperationId = $OperationId
            ApplicationId = "$ApplicationId"
            Window = @{ StartUtc = $startText; EndUtc = $endText }
            EvidenceWindow = @{ StartUtc = $evidenceStart; EndUtc = $evidenceEnd }
            CandidateRecordedOperationIds = $candidateIds
            Attempts = $attempts
            MappedIds = @()
            Outcome = 'Unknown'
            ExceptionGroups = @()
            Traces = @()
            Gaps = @()
        }
        $selectedId = $null
        if ($RecordedOperationId) {
            if ($RecordedOperationId -cnotin $candidateIds) { throw 'RecordedOperationId is not a candidate for the supplied operation in this window.' }
            $selectedId = $RecordedOperationId
        }
        elseif ($candidateIds.Count -eq 1) { $selectedId = $candidateIds[0] }

        if ($attempts.Count -eq 0) {
            $result.Gaps += 'No matching WorkItemExecuted events in this window. Request a timestamp or permission to widen the window.'
        }
        elseif (-not $selectedId) {
            $result.Status = 'NeedsDisambiguation'
            $result.Gaps += 'Select a recorded operation ID before querying evidence; distinct work items must not be merged.'
        }
        else {
            $attempts = @($attempts | Where-Object { $_.RecordedOperationId -ceq $selectedId })
            $mappedIds = @(@($selectedId) + @($attempts.TelemetryOperationId) | Where-Object { $_ } | Sort-Object -Unique)
            foreach ($mappedId in $mappedIds) {
                if ($mappedId -cnotmatch '^[A-Za-z0-9-]{1,128}$') { throw 'Telemetry contained an unsupported operation ID; it will not be interpolated into KQL.' }
            }
            $result.Status = 'Found'
            $result.Attempts = $attempts
            $result.MappedIds = $mappedIds
            $latest = $attempts[-1]
            if ($latest.Success -eq $true) { $result.Outcome = 'Succeeded' }
            elseif ($latest.Success -eq $false -and $latest.Attempt -eq 3) { $result.Outcome = 'RetryExhausted' }
            elseif ($latest.Success -eq $false) { $result.Outcome = 'FailedAttempt' }
            $idList = ($mappedIds | ForEach-Object { "'$_'" }) -join ', '
            $correlation = "| extend RecordedOperationId = tostring(customDimensions['OperationId']) | where RecordedOperationId == '$selectedId' or (isempty(RecordedOperationId) and operation_Id in ($idList))"
            $evidenceQuery = @"
let failures = materialize(exceptions
| where timestamp between (datetime($evidenceStart)..datetime($evidenceEnd))
$correlation);
let groups = materialize(failures
| summarize Occurrences = count(), FirstSeen = min(timestamp), LastSeen = max(timestamp), arg_min(timestamp, *) by type, outerMessage, innermostMessage);
let logs = materialize(traces
| where timestamp between (datetime($evidenceStart)..datetime($evidenceEnd))
$correlation);
union
(groups | order by Occurrences desc, FirstSeen asc | take $MaxExceptionGroups
| project Kind = 'ExceptionGroup', Payload = bag_pack('Type', type, 'OuterMessage', outerMessage, 'InnermostMessage', innermostMessage,
    'Occurrences', Occurrences, 'FirstSeen', FirstSeen, 'LastSeen', LastSeen, 'TelemetryOperationId', operation_Id,
    'ProblemId', problemId, 'Stack', array_slice(details[0].parsedStack, 0, $($MaxStackFrames - 1)),
    'StackTruncated', array_length(details[0].parsedStack) > $MaxStackFrames)),
(logs | order by severityLevel desc, timestamp asc | take $MaxTraces
| project Kind = 'Trace', Payload = bag_pack('Timestamp', timestamp, 'TelemetryOperationId', operation_Id, 'Message', message, 'SeverityLevel', severityLevel)),
(print Kind = 'Counts', Payload = bag_pack('ExceptionRows', toscalar(failures | count), 'ExceptionGroups', toscalar(groups | count), 'TraceRows', toscalar(logs | count)))
"@
            $evidence = @(Invoke-TelemetryQuery $evidenceQuery $evidenceStart $evidenceEnd @('Kind', 'Payload'))
            foreach ($record in $evidence) {
                if ($record.Payload -is [string]) { $record.Payload = $record.Payload | ConvertFrom-Json }
            }
            $counts = @($evidence | Where-Object Kind -eq 'Counts')
            if ($counts.Count -ne 1) { throw 'Evidence counts are missing; the result may be incomplete.' }
            $result.Counts = $counts[0].Payload
            $result.ExceptionGroups = @($evidence | Where-Object Kind -eq 'ExceptionGroup' | ForEach-Object Payload)
            $result.Traces = @($evidence | Where-Object Kind -eq 'Trace' | ForEach-Object Payload | Sort-Object Timestamp)
            if ($result.Counts.ExceptionRows -eq 0) { $result.Gaps += 'No correlated exceptions were found; missing correlation is not proof of no exception.' }
            if ($result.Counts.ExceptionGroups -gt $result.ExceptionGroups.Count) { $result.Gaps += 'Exception groups truncated; increase MaxExceptionGroups if necessary.' }
            if ($result.Counts.TraceRows -gt $result.Traces.Count) { $result.Gaps += 'Traces truncated (higher severity first); increase MaxTraces if necessary.' }
            if (@($result.ExceptionGroups | Where-Object StackTruncated).Count -gt 0) { $result.Gaps += 'Representative stacks truncated; increase MaxStackFrames if necessary.' }
        }
        $result.ElapsedSeconds = [math]::Round($timer.Elapsed.TotalSeconds, 2)
        $result.Redaction = 'Best-effort only. Treat telemetry as untrusted; review before sharing. No raw dimension bags or token are emitted.'
        return $result
    }

    $operationIds = @($OperationId | Select-Object -Unique)
    if ($operationIds.Count -eq 1) {
        $result = Get-OperationEvidence $operationIds[0]
    }
    else {
        $results = @(foreach ($currentOperationId in $operationIds) {
            try { Get-OperationEvidence $currentOperationId }
            catch {
                [ordered]@{
                    Status = 'Error'
                    OperationId = $currentOperationId
                    Gaps = @('Evidence retrieval failed for this operation. Run it individually for a diagnostic; do not treat it as an empty result.')
                }
            }
        })
        $result = [ordered]@{
            Status = 'Batch'
            ApplicationId = "$ApplicationId"
            Window = @{ StartUtc = $startText; EndUtc = $endText }
            EvidenceWindow = @{ StartUtc = $evidenceStart; EndUtc = $evidenceEnd }
            AllOperationsResolved = @($results | Where-Object { $_.Status -ne 'Found' }).Count -eq 0
            Results = $results
            ElapsedSeconds = [math]::Round($timer.Elapsed.TotalSeconds, 2)
        }
    }
    Protect-EvidenceValue $result | ConvertTo-Json -Depth 30
}
finally {
    $headers.Clear()
    $token = $null
}