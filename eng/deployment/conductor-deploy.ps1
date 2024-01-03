param(
    [Parameter(Mandatory=$true)][string]$resourceGroupName,
    [Parameter(Mandatory=$true)][string]$containerappName,
    [Parameter(Mandatory=$true)][string]$commitSha,
    [Parameter(Mandatory=$true)][string]$containerRegistryName,
    [Parameter(Mandatory=$true)][string]$imageName,
    [Parameter(Mandatory=$true)][string]$subscriptionName
)

az extension add --name containerapp --upgrade
az provider register --namespace Microsoft.App

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
# deploy the new image
$newImage = "$containerRegistryName.azurecr.io/$imageName`:$commitSha"
Write-Host "Deploying new image $newImage"
az containerapp update --name $containerappName --resource-group $resourceGroupName --image $newImage --revision-suffix $commitSha | Out-Null

$newRevisionName = "$containerappName--$commitSha"

Write-Host "Waiting for new revision $newRevisionName to become active"
# wait for the new revision to become active
$sleep = $false
DO
{
    if ($sleep -eq $true) 
    {
        Start-Sleep -Seconds 60
    }
    $newRevisionStatus = az containerapp revision show --name $containerappName --resource-group $resourceGroupName --revision $newRevisionName --query "properties.active"
    Write-Host "New revision status: $newRevisionStatus"
    $sleep = $true
} While ($newRevisionStatus -ne "true")

Write-Host "Assigning label $inactiveLabel to the new revision"
# assign the label to the new revision
az containerapp revision label add --label $inactiveLabel --name $containerappName --resource-group $resourceGroupName --revision $newRevisionName | Out-Null

# TODO tests..

# transfer all traffiic to the new revision
az containerapp ingress traffic set --name $containerappName --resource-group $resourceGroupName --label-weight $inactiveLabel=100 | Out-Null
Write-Host "All traffic has been redirected to label $inactiveLabel"