param(
    [Parameter(Mandatory=$true)][string]$resourceGroupName,
    [Parameter(Mandatory=$true)][string]$containerappName,
    [Parameter(Mandatory=$true)][string]$commitSha,
    [Parameter(Mandatory=$true)][string]$containerRegistryName,
    [Parameter(Mandatory=$true)][string]$imageName
)

$containerappTraffic = az containerapp ingress traffic show --name $containerappName --resource-group $resourceGroupName | ConvertFrom-Json

# find the currently active revision
$activeRevision = $containerappTraffic | Where-Object { $_.weight -eq 100 } 

# detirmine the label of the inactive revision
if ($activeRevision.label -eq "blue") {
    $inactiveLabel = "green"
} else {
    $inactiveLabel = "blue"
}

# remove the label from the inactive revision
az containerapp revision label remove --label $inactiveLabel --name $containerappName --resource-group $resourceGroupName

# deactivate the inactive revision
$inactiveRevision = $containerappTraffic | Where-Object { $_.label -eq $inactiveLabel }

az containerapp revision deactivate --revision $inactiveRevision.revisionName

# deploy the new image
$newImage = "$containerRegistryName.azurecr.io/$imageName`:$commitSha"
az containerapp update --name $containerappName --resource-group $resourceGroupName --image $newImage --revision-suffix $commitSha

$newRevisionName = "$containerappName--$commitSha"

# wait for the new revision to become active
$sleep = $false
DO
{
    if ($sleep -eq $true) 
    {
        Start-Sleep -Seconds 60
    }

    $newRevisionStatus = az containerapp revision show --name $containerappName --resource-group $resourceGroupName --revision-name $newRevisionName --query "properties.active"
    $sleep = $true
} While ($newRevisionStatus -ne "true")

# assign the label to the new revision
az containerapp revision label add --label $inactiveLabel --name $containerappName --resource-group $resourceGroupName --revision-name $newRevisionName

# transfer all traffiic to the new revision
az containerapp ingress traffic set --name $containerappName --resource-group $resourceGroupName --label-weight $inactiveLabel=100