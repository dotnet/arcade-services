# This script deploys the Product Construction Service using the blue/green deployment pattern.
# The script determines the color of the currently active revision, deactivates the old inactive revision, 
# and deploys the new revision, switching all traffic to it if the health probes pass.
param(
    [Parameter(Mandatory=$true)][string]$subscriptionId,
    [Parameter(Mandatory=$true)][string]$resourceGroupName,
    [Parameter(Mandatory=$true)][string]$containerappName,
    [Parameter(Mandatory=$true)][string]$workspaceName,
    [Parameter(Mandatory=$true)][string]$newImageTag,
    [Parameter(Mandatory=$true)][string]$containerRegistryName,
    [Parameter(Mandatory=$true)][string]$imageName,
    [Parameter(Mandatory=$true)][string]$token,
    [Parameter(Mandatory=$true)][string]$containerjobNames
)

$containerapp = az containerapp show -g $resourceGroupName -n $containerappName | ConvertFrom-Json
$pcsUrl = "https://$($containerapp.properties.configuration.ingress.fqdn)"
$pcsStatusUrl = $pcsUrl + "/status"
$pcsStopUrl = $pcsStatusUrl + "/stop"
$pcsStartUrl = $pcsStatusUrl + "/start"
$authenticationHeader = @{
    "Authorization" = "Bearer $token"
}

function Wait() {
    Start-Sleep -Seconds 20
    return $true
}

function StopAndWait([string]$pcsStatusUrl, [string]$pcsStopUrl, [hashtable]$authenticationHeader) {
    try {
        
        $stopResponse = Invoke-WebRequest -Uri $pcsStopUrl -Method Put -Headers $authenticationHeader

        if ($stopResponse.StatusCode -ne 200) {
            Write-Warning "Service isn't responding to the stop request. Deploying the new revision without stopping the service."
            return
        }

        # wait for the service to finish processing the current job
        do
        {
            $pcsStateResponse = Invoke-WebRequest -Uri $pcsStatusUrl -Method Get
            if ($pcsStateResponse.StatusCode -ne 200) {
                Write-Warning "Service isn't responding to the status request. Deploying the new revision without stopping the service."
                return
            }
            Write-Host "Product Construction Service state: $($pcsStateResponse.Content)"
        } while ($pcsStateResponse.Content -notmatch "Stopped" -and $(Wait))
    }
    catch {
        Write-Warning "An error occurred: $($_.Exception.Message).  Deploying the new revision without stopping the service."
    }
    return
}

function GetLogsLink {
    param (
        [string]$revisionName,
        [string]$resourceGroup,
        [string]$workspaceName,
        [string]$subscriptionId
    )

    $query = "ContainerAppConsoleLogs_CL `
| where RevisionName_s == '$revisionName' `
| project TimeGenerated, Log_s"

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($query)
    $memoryStream = New-Object System.IO.MemoryStream
    $compressedStream = New-Object System.IO.Compression.GZipStream($memoryStream, [System.IO.Compression.CompressionMode]::Compress, $true)

    $compressedStream.Write($bytes, 0, $bytes.Length)
    $compressedStream.Close()
    $memoryStream.Seek(0, [System.IO.SeekOrigin]::Begin) | Out-Null
    $data = $memoryStream.ToArray()
    $encodedQuery = [Convert]::ToBase64String($data)
    $encodedQuery = [System.Web.HttpUtility]::UrlEncode($encodedQuery)
    return "https://ms.portal.azure.com#@72f988bf-86f1-41af-91ab-2d7cd011db47/blade/Microsoft_OperationsManagementSuite_Workspace/Logs.ReactView/" +
           "resourceId/%2Fsubscriptions%2F$subscriptionId%2FresourceGroups%2F$resourceGroup%2Fproviders%2FMicrosoft.OperationalInsights%2Fworkspaces%2F" +
           "$workspaceName/source/LogsBlade.AnalyticsShareLinkToQuery/q/$encodedQuery/timespan/P1D/limit/1000"
}

az extension add --name containerapp --upgrade

Write-Host "Fetching all revisions to determine the active label"
$containerappTraffic = az containerapp ingress traffic show --name $containerappName --resource-group $resourceGroupName | ConvertFrom-Json
# find the currently active revision
$activeRevision = $containerappTraffic | Where-Object { $_.weight -eq 100 }

Write-Host "Currently active revision: $($activeRevision.revisionName) with label $($activeRevision.label)"

# detirmine the label of the inactive revision
if ($activeRevision.label -eq "blue") {
    $inactiveLabel = "green"
} else {
    $inactiveLabel = "blue"
}

Write-Host "Next revision will be deployed with label $inactiveLabel"
Write-Host "Removing label $inactiveLabel from the inactive revision"
# remove the label from the inactive revision
$revisionRemovalOutput = az containerapp revision label remove --label $inactiveLabel --name $containerappName --resource-group $resourceGroupName 2>&1

if ($revisionRemovalOutput -match "Please specify a label name with an associated traffic weight") {
    Write-Host "Couldn't find a revision with label $inactiveLabel. Skipping deactivation of inactive revision"
} 
else
{
    Write-Host "Deactivating inactive revision"
    # deactivate the inactive revision

    $inactiveRevision = $containerappTraffic | Where-Object { $_.label -eq $inactiveLabel }

    az containerapp revision deactivate --revision $inactiveRevision.revisionName --name $containerappName --resource-group $resourceGroupName
}

# Tell the service to stop processing jobs after it finishes the current one
Write-Host "Stopping the service from processing new jobs"
StopAndWait -pcsStatusUrl $pcsStatusUrl -pcsStopUrl $pcsStopUrl -authenticationHeader $authenticationHeader

$newRevisionName = "$containerappName--$newImageTag"
$newImage = "$containerRegistryName.azurecr.io/$imageName`:$newImageTag"

# Kick off the deployment of the new image
Write-Host "Deploying container app / $newImageTag"
az containerapp update --name $containerappName --resource-group $resourceGroupName --image $newImage --revision-suffix $newImageTag | Out-Null

# Deploy jobs
Write-Host "Deploying container jobs / $newImageTag"
foreach ($containerjobName in $containerjobNames.Split(',')) {
    Write-Host "Updating job $containerjobName"
    az containerapp job update --name $containerjobName --resource-group $resourceGroupName --image $newImage | Out-Null
}

# Wait for the service to come up
try
{
    # Wait for the new revision to pass health probes and become active
    Write-Host "Waiting for new revision $newRevisionName to become active"
    do
    {
        $newRevisionRunningState = az containerapp revision show --name $containerappName --resource-group $resourceGroupName --revision $newRevisionName --query "properties.runningState"
        Write-Host "New revision running state: $newRevisionRunningState"
    } while ($newRevisionRunningState -notmatch "Running" -and $newRevisionRunningState -notmatch "Failed" -and $(Wait))

    if ($newRevisionRunningState -match "Running") {
        Write-Host "Assigning label $inactiveLabel to the new revision"
        # assign the label to the new revision
        az containerapp revision label add --label $inactiveLabel --name $containerappName --resource-group $resourceGroupName --revision $newRevisionName | Out-Null

        # transfer all traffic to the new revision
        az containerapp ingress traffic set --name $containerappName --resource-group $resourceGroupName --label-weight "$inactiveLabel=100" | Out-Null
        Write-Host "All traffic has been redirected to label $inactiveLabel"
    }
    else {
        Write-Warning "New revision failed to start. Deactivating the new revision.."
        $link = GetLogsLink `
            -revisionName "$newRevisionName"    `
            -subscriptionId "$subscriptionId"   `
            -resourceGroup "$resourceGroupName" `
            -workspaceName "$workspaceName"
            
        Write-Host " Check revision $newRevisionName logs in the inactive revision: `
        $link"

        az containerapp revision deactivate --revision $newRevisionName --name $containerappName --resource-group $resourceGroupName
        exit 1
    }
}
finally {
    # Start the service. This either starts the new revision or the old one if the new one failed to start
    Write-Host "Starting the product construction service"
    Invoke-WebRequest -Uri $pcsStartUrl -Method Put -Headers $authenticationHeader
}
