#!/usr/bin/env pwsh

[CmdletBinding()]
param(
    [int]$Port = 53180,
    [string]$AzDoOrganization = "dnceng-public",
    [string]$AzDoProject = "public",
    [string]$KeyVaultName = "build-insights-dev",
    [int]$GitHubAppId = 2892253,
    [switch]$SkipGitHub,
    [switch]$SkipAzDo
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSVersion.Major -lt 7)
{
    throw "PowerShell 7 or later is required. Start this script with 'pwsh'."
}

$gitHubWebhookPath = "/api/github/webhooks"
$azDoHookDefinitions = @(
    @{
        Name = "BuildCompleted"
        PublisherId = "tfs"
        EventType = "build.complete"
        ResourceVersion = "2.0"
        RelativePath = "/api/azdo/servicehooks/build.complete"
        PublisherInputs = {
            param([string]$projectId)
            @{
                projectId = $projectId
            }
        }
    },
    @{
        Name = "PipelineRunStateChanged"
        PublisherId = "pipelines"
        EventType = "ms.vss-pipelines.run-state-changed-event"
        ResourceVersion = "5.1-preview.1"
        RelativePath = "/api/azdo/servicehooks/ms.vss-pipelines.run-state-changed-event"
        PublisherInputs = {
            param([string]$projectId)
            @{
                projectId = $projectId
                runStateId = "Completed"
            }
        }
    },
    @{
        Name = "PipelineStageStateChanged"
        PublisherId = "pipelines"
        EventType = "ms.vss-pipelines.stage-state-changed-event"
        ResourceVersion = "5.1-preview.1"
        RelativePath = "/api/azdo/servicehooks/ms.vss-pipelines.stage-state-changed-event"
        PublisherInputs = {
            param([string]$projectId)
            @{
                projectId = $projectId
                stageStateId = "Completed"
            }
        }
    }
)

$script:state = [ordered]@{
    CleanedUp = $false
    OriginalGitHubWebhookConfig = $null
    AzDoSubscriptionIds = [System.Collections.Generic.List[string]]::new()
    DevTunnel = $null
}

function Write-Section
{
    param([string]$Message)

    Write-Host ""
    Write-Host "=== $Message ===" -ForegroundColor Cyan
}

function Assert-CommandAvailable
{
    param(
        [string]$Name,
        [string]$InstallHint
    )

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue))
    {
        throw "$Name was not found. $InstallHint"
    }
}

function ConvertTo-Base64UrlString
{
    param([byte[]]$Bytes)

    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-GitHubAppJwt
{
    param(
        [int]$AppId,
        [string]$PrivateKeyPem
    )

    $now = [DateTimeOffset]::UtcNow
    $payload = @{
        iat = $now.AddMinutes(-1).ToUnixTimeSeconds()
        exp = $now.AddMinutes(9).ToUnixTimeSeconds()
        iss = $AppId
    }

    $headerJson = '{"alg":"RS256","typ":"JWT"}'
    $payloadJson = $payload | ConvertTo-Json -Compress

    $unsignedToken = "$(ConvertTo-Base64UrlString ([System.Text.Encoding]::UTF8.GetBytes($headerJson))).$(ConvertTo-Base64UrlString ([System.Text.Encoding]::UTF8.GetBytes($payloadJson)))"

    $rsa = [System.Security.Cryptography.RSA]::Create()
    try
    {
        $rsa.ImportFromPem($PrivateKeyPem)
        $signatureBytes = $rsa.SignData(
            [System.Text.Encoding]::UTF8.GetBytes($unsignedToken),
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    }
    finally
    {
        $rsa.Dispose()
    }

    return "$unsignedToken.$(ConvertTo-Base64UrlString $signatureBytes)"
}

function Get-PlainTextFromSecureString
{
    param([Security.SecureString]$SecureString)

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureString)
    try
    {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally
    {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

function Get-SecretValue
{
    param(
        [string]$EnvironmentVariableName,
        [string]$KeyVaultSecretName
    )

    $environmentValue = [Environment]::GetEnvironmentVariable($EnvironmentVariableName)
    if (-not [string]::IsNullOrWhiteSpace($environmentValue))
    {
        return $environmentValue
    }

    $secretValue = & az keyvault secret show --vault-name $KeyVaultName --name $KeyVaultSecretName --query value --output tsv --only-show-errors

    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($secretValue))
    {
        throw "Unable to read '$KeyVaultSecretName' from Key Vault '$KeyVaultName'."
    }

    return $secretValue.Trim()
}

function Get-AzDoPat
{
    $environmentPat = [Environment]::GetEnvironmentVariable("AZDO_PAT")
    if (-not [string]::IsNullOrWhiteSpace($environmentPat))
    {
        return $environmentPat
    }

    $securePat = Read-Host -Prompt "Enter the Azure DevOps PAT (Service Hooks scope required)" -AsSecureString
    return Get-PlainTextFromSecureString -SecureString $securePat
}

function Invoke-GitHubRestMethod
{
    param(
        [ValidateSet("GET", "PATCH")]
        [string]$Method,
        [string]$Uri,
        [object]$Body = $null
    )

    $jwt = New-GitHubAppJwt -AppId $GitHubAppId -PrivateKeyPem $script:GitHubPrivateKeyPem
    $headers = @{
        Accept = "application/vnd.github+json"
        Authorization = "Bearer $jwt"
        "X-GitHub-Api-Version" = "2022-11-28"
        "User-Agent" = "build-insights-dev-tunnel"
    }

    $parameters = @{
        Method = $Method
        Uri = $Uri
        Headers = $headers
    }

    if ($null -ne $Body)
    {
        $parameters.ContentType = "application/json"
        $parameters.Body = $Body | ConvertTo-Json -Compress -Depth 10
    }

    return Invoke-RestMethod @parameters
}

function Invoke-AzDoRestMethod
{
    param(
        [ValidateSet("GET", "POST", "DELETE")]
        [string]$Method,
        [string]$Uri,
        [object]$Body = $null
    )

    $headers = @{
        Accept = "application/json"
        Authorization = "Basic $script:AzDoAuthorizationHeader"
    }

    $parameters = @{
        Method = $Method
        Uri = $Uri
        Headers = $headers
    }

    if ($null -ne $Body)
    {
        $parameters.ContentType = "application/json"
        $parameters.Body = $Body | ConvertTo-Json -Compress -Depth 10
    }

    return Invoke-RestMethod @parameters
}

function Get-AzDoProjectId
{
    $projectName = [Uri]::EscapeDataString($AzDoProject)
    $uri = "https://dev.azure.com/$AzDoOrganization/_apis/projects/$projectName?api-version=7.1"
    $project = Invoke-AzDoRestMethod -Method GET -Uri $uri
    return $project.id
}

function New-AzDoWebhookSubscription
{
    param(
        [string]$PublisherId,
        [string]$EventType,
        [string]$ResourceVersion,
        [hashtable]$PublisherInputs,
        [string]$TargetUrl,
        [string]$SecretHeaderValue
    )

    $payload = @{
        publisherId = $PublisherId
        eventType = $EventType
        resourceVersion = $ResourceVersion
        consumerId = "webHooks"
        consumerActionId = "httpRequest"
        publisherInputs = $PublisherInputs
        consumerInputs = @{
            url = $TargetUrl
            httpHeaders = "X-BuildAnalysis-Secret:$SecretHeaderValue"
        }
    }

    $uri = "https://dev.azure.com/$AzDoOrganization/_apis/hooks/subscriptions?api-version=7.1"
    return Invoke-AzDoRestMethod -Method POST -Uri $uri -Body $payload
}

function Remove-AzDoWebhookSubscription
{
    param([string]$SubscriptionId)

    $uri = "https://dev.azure.com/$AzDoOrganization/_apis/hooks/subscriptions/$SubscriptionId?api-version=7.1"
    Invoke-AzDoRestMethod -Method DELETE -Uri $uri | Out-Null
}

function Wait-ForHealthyUrl
{
    param(
        [string]$Url,
        [timespan]$Timeout = ([TimeSpan]::FromSeconds(45))
    )

    $deadline = [DateTimeOffset]::UtcNow.Add($Timeout)
    $lastError = $null

    while ([DateTimeOffset]::UtcNow -lt $deadline)
    {
        try
        {
            $response = Invoke-WebRequest -Uri $Url -Method GET -SkipCertificateCheck
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300)
            {
                return
            }
        }
        catch
        {
            $lastError = $_
        }

        Start-Sleep -Milliseconds 500
    }

    if ($null -ne $lastError)
    {
        throw "Timed out waiting for '$Url' to become healthy. Last error: $($lastError.Exception.Message)"
    }

    throw "Timed out waiting for '$Url' to become healthy."
}

function Get-LogFileContent
{
    $output = if ($script:state.DevTunnel -and (Test-Path $script:state.DevTunnel.StdOutPath))
    {
        Get-Content -Path $script:state.DevTunnel.StdOutPath -Raw
    }
    else
    {
        ""
    }

    $error = if ($script:state.DevTunnel -and (Test-Path $script:state.DevTunnel.StdErrPath))
    {
        Get-Content -Path $script:state.DevTunnel.StdErrPath -Raw
    }
    else
    {
        ""
    }

    return ($output, $error -join [Environment]::NewLine).Trim()
}

function Start-DevTunnelHost
{
    param([int]$Port)

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $stdOutPath = Join-Path ([System.IO.Path]::GetTempPath()) "build-insights-devtunnel-$timestamp-out.log"
    $stdErrPath = Join-Path ([System.IO.Path]::GetTempPath()) "build-insights-devtunnel-$timestamp-err.log"

    $startProcessParameters = @{
        FilePath = "devtunnel"
        ArgumentList = @("host", "-p", $Port.ToString(), "--protocol", "https", "--allow-anonymous")
        PassThru = $true
        NoNewWindow = $true
        RedirectStandardOutput = $stdOutPath
        RedirectStandardError = $stdErrPath
    }

    $process = Start-Process @startProcessParameters

    $script:state.DevTunnel = [ordered]@{
        Process = $process
        StdOutPath = $stdOutPath
        StdErrPath = $stdErrPath
        TunnelId = $null
        TunnelUrl = $null
        InspectUrl = $null
    }

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
    while ([DateTimeOffset]::UtcNow -lt $deadline)
    {
        if ($process.HasExited)
        {
            break
        }

        $content = Get-LogFileContent

        if (-not $script:state.DevTunnel.TunnelUrl -and $content -match 'Hosting port \d+ at (?<url>https://[^\s,]+)')
        {
            $script:state.DevTunnel.TunnelUrl = $matches['url'].TrimEnd('/')
        }

        if (-not $script:state.DevTunnel.InspectUrl -and $content -match 'inspect it at (?<inspect>https://[^\s,]+)')
        {
            $script:state.DevTunnel.InspectUrl = $matches['inspect'].TrimEnd('/')
        }

        if (-not $script:state.DevTunnel.TunnelId -and $content -match 'Ready to accept connections for tunnel: (?<id>[a-z0-9\-]+)')
        {
            $script:state.DevTunnel.TunnelId = $matches['id']
        }

        if ($script:state.DevTunnel.TunnelUrl)
        {
            return $script:state.DevTunnel
        }

        Start-Sleep -Milliseconds 500
    }

    $content = Get-LogFileContent
    if ($process.HasExited)
    {
        throw "devtunnel host exited before it published a public URL.`n$content"
    }

    throw "Timed out waiting for devtunnel to report its public URL.`n$content"
}

function Invoke-Cleanup
{
    if ($script:state.CleanedUp)
    {
        return
    }

    $script:state.CleanedUp = $true

    Write-Section "Cleaning up"

    foreach ($subscriptionId in @($script:state.AzDoSubscriptionIds) | Sort-Object -Descending)
    {
        try
        {
            Write-Host "Removing Azure DevOps subscription $subscriptionId..."
            Remove-AzDoWebhookSubscription -SubscriptionId $subscriptionId
        }
        catch
        {
            Write-Warning "Failed to remove Azure DevOps subscription '$subscriptionId': $($_.Exception.Message)"
        }
    }

    if ($script:state.OriginalGitHubWebhookConfig)
    {
        try
        {
            Write-Host "Restoring the GitHub App webhook URL..."
            Invoke-GitHubRestMethod -Method PATCH -Uri "https://api.github.com/app/hook/config" -Body @{
                url = $script:state.OriginalGitHubWebhookConfig.url
                content_type = $script:state.OriginalGitHubWebhookConfig.content_type
                insecure_ssl = $script:state.OriginalGitHubWebhookConfig.insecure_ssl
            } | Out-Null
        }
        catch
        {
            Write-Warning "Failed to restore the GitHub App webhook configuration: $($_.Exception.Message)"
        }
    }

    if ($script:state.DevTunnel -and $script:state.DevTunnel.Process -and -not $script:state.DevTunnel.Process.HasExited)
    {
        try
        {
            Write-Host "Stopping the dev tunnel host process..."
            Stop-Process -Id $script:state.DevTunnel.Process.Id -Force
        }
        catch
        {
            Write-Warning "Failed to stop the dev tunnel host process: $($_.Exception.Message)"
        }
    }

    foreach ($logPath in @($script:state.DevTunnel?.StdOutPath, $script:state.DevTunnel?.StdErrPath) | Where-Object { $_ })
    {
        if (Test-Path $logPath)
        {
            Remove-Item -Path $logPath -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Section "Build Insights local webhook tunnel"
Write-Host "This workflow re-points the shared Build Insights dev webhook targets."
Write-Host "Only one developer should run it at a time."

Assert-CommandAvailable -Name "pwsh" -InstallHint "Install PowerShell 7 and make sure 'pwsh' is on PATH."
Assert-CommandAvailable -Name "az" -InstallHint "Install Azure CLI and run 'az login'."
Assert-CommandAvailable -Name "devtunnel" -InstallHint "Install the dev tunnel CLI with 'winget install Microsoft.devtunnel' and run 'devtunnel user login'."

$null = & az account show --output none --only-show-errors
if ($LASTEXITCODE -ne 0)
{
    throw "Azure CLI is not logged in. Run 'az login' first."
}

$devTunnelUserStatus = & devtunnel user show 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $devTunnelUserStatus -match 'not logged in|no user|sign in')
{
    throw "devtunnel is not logged in. Run 'devtunnel user login' first."
}

try
{
    Register-EngineEvent -SourceIdentifier PowerShell.Exiting -Action { Invoke-Cleanup } | Out-Null

    Write-Section "Checking local service health"
    $localHealthUrl = "https://localhost:$Port/health"
    Wait-ForHealthyUrl -Url $localHealthUrl
    Write-Host "Local API is healthy at $localHealthUrl"

    Write-Section "Starting the dev tunnel"
    $devTunnel = Start-DevTunnelHost -Port $Port
    Write-Host "Tunnel URL: $($devTunnel.TunnelUrl)"
    if ($devTunnel.InspectUrl)
    {
        Write-Host "Inspect URL: $($devTunnel.InspectUrl)"
    }

    Wait-ForHealthyUrl -Url "$($devTunnel.TunnelUrl)/health"
    Write-Host "Public tunnel health check succeeded."

    if (-not $SkipGitHub)
    {
        Write-Section "Updating the GitHub App webhook"
        $script:GitHubPrivateKeyPem = Get-SecretValue -EnvironmentVariableName "BUILD_INSIGHTS_GITHUB_APP_PRIVATE_KEY" -KeyVaultSecretName "github-app-private-key"

        $script:state.OriginalGitHubWebhookConfig = Invoke-GitHubRestMethod -Method GET -Uri "https://api.github.com/app/hook/config"
        Write-Host "Current GitHub webhook URL: $($script:state.OriginalGitHubWebhookConfig.url)"

        $newGitHubWebhookUrl = "$($devTunnel.TunnelUrl)$gitHubWebhookPath"
        Invoke-GitHubRestMethod -Method PATCH -Uri "https://api.github.com/app/hook/config" -Body @{
            url = $newGitHubWebhookUrl
            content_type = $script:state.OriginalGitHubWebhookConfig.content_type
            insecure_ssl = $script:state.OriginalGitHubWebhookConfig.insecure_ssl
        } | Out-Null

        Write-Host "GitHub webhook URL updated to $newGitHubWebhookUrl"
    }
    else
    {
        Write-Host "Skipping GitHub webhook configuration."
    }

    if (-not $SkipAzDo)
    {
        Write-Section "Creating Azure DevOps service hook subscriptions"
        $azDoPat = Get-AzDoPat
        $script:AzDoAuthorizationHeader = [Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes(":$azDoPat"))
        $azDoSecretHeaderValue = Get-SecretValue -EnvironmentVariableName "BUILD_INSIGHTS_AZDO_SERVICE_HOOK_SECRET" -KeyVaultSecretName "azdo-service-hook-secret"

        $projectId = Get-AzDoProjectId
        Write-Host "Resolved Azure DevOps project '$AzDoProject' to $projectId"

        foreach ($hookDefinition in $azDoHookDefinitions)
        {
            $targetUrl = "$($devTunnel.TunnelUrl)$($hookDefinition.RelativePath)"
            $publisherInputs = & $hookDefinition.PublisherInputs $projectId

            $subscription = New-AzDoWebhookSubscription -PublisherId $hookDefinition.PublisherId -EventType $hookDefinition.EventType -ResourceVersion $hookDefinition.ResourceVersion -PublisherInputs $publisherInputs -TargetUrl $targetUrl -SecretHeaderValue $azDoSecretHeaderValue

            $script:state.AzDoSubscriptionIds.Add($subscription.id)
            Write-Host "Created $($hookDefinition.Name) subscription: $($subscription.id)"
        }
    }
    else
    {
        Write-Host "Skipping Azure DevOps service hook configuration."
    }

    Write-Section "Ready"
    Write-Host "GitHub endpoint: $($devTunnel.TunnelUrl)$gitHubWebhookPath"
    foreach ($hookDefinition in $azDoHookDefinitions)
    {
        Write-Host "Azure DevOps endpoint: $($devTunnel.TunnelUrl)$($hookDefinition.RelativePath)"
    }

    Write-Host ""
    Write-Host "Press Ctrl+C to stop the tunnel and restore the shared webhook configuration." -ForegroundColor Yellow

    Wait-Process -Id $devTunnel.Process.Id
}
finally
{
    Invoke-Cleanup
}
