param(
	[string]$obj,
  [string]$appPackagePath,
  [string]$appParametersFile,
  [Guid]$subscriptionId,
  [string]$resourceGroupName,
  [string]$clusterName,
  [string]$applicationName,
  [string]$location,
  [bool]$autoRollBack,
  [string]$publishProfile
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
Set-StrictMode -Version 2.0

if (Test-Path -PathType Leaf $publishProfile) {
  Select-Xml -Path $publishProfile -XPath "//*[local-name()='ClusterConnectionParameters']" | Select-Object -exp Node | ForEach-Object {
    $script:resourceGroupName = $_.ResourceGroupName
    $script:clusterName = $_.ClusterName
    $script:applicationName = $_.ApplicationName
  }
  Select-Xml -Path $publishProfile -XPath "//*[local-name()='ApplicationParameterFile']" | Select-Object -exp Node | ForEach-Object {
    $script:appParametersFile = Join-Path (Split-Path $publishProfile -Parent) $_.Path
  }
}

if (-not $subscriptionId) {
  $subscriptionId = [Guid]((Get-AzContext).Subscription.Id)
} else {
  Set-AzContext -Subscription $subscriptionId
}

if (-not (Test-Path $obj)) {
  mkdir $obj
}

$shortSubscription = $subscriptionId.ToString("N").Substring(0, 19)
$stagingStorageAccountName = "stage$shortSubscription"

Write-Host "ensuring storage account $stagingStorageAccountName exists"
try {
  $account = New-AzStorageAccount -ResourceGroupName "ARM_Deploy_Staging" -Name $stagingStorageAccountName -Location $location -SkuName Standard_LRS
}
catch {
  $account = Get-AzStorageAccount | Where-Object StorageAccountName -eq $stagingStorageAccountName
}

$manifestFile = Join-Path $appPackagePath ApplicationManifest.xml
$applicationTypeName = Select-Xml -Path $manifestFile -XPath "/*[local-name()='ApplicationManifest']/@ApplicationTypeName" | Select-Object -exp Node | Select-Object -exp '#text'
$applicationTypeVersion = Select-Xml -Path $manifestFile -XPath "/*[local-name()='ApplicationManifest']/@ApplicationTypeVersion" | Select-Object -exp Node | Select-Object -exp '#text'

$appPackageZipName = "$applicationTypeName.$applicationTypeVersion.zip"
$appPackageZip = Join-Path $obj $appPackageZipName
$appPackageFileName = "$applicationTypeName.$applicationTypeVersion.sfpkg"
$appPackageFile = Join-Path $obj $appPackageFileName
Write-Host "Compressing $applicationTypeName version $applicationTypeVersion to $appPackageFile"
Compress-Archive -Path (Join-Path $appPackagePath "*") -DestinationPath $appPackageZip -CompressionLevel Optimal -Force
Rename-Item $appPackageZip $appPackageFileName

Write-Host "Uploading $appPackageFile to staging storage account"
New-AzStorageContainer -Context $account.Context -Name "service-packages" -Permission Off -ErrorAction SilentlyContinue
Set-AzStorageBlobContent -Context $account.Context -File $appPackageFile -Container "service-packages" -Blob $appPackageFileName -Force
$packageSasUrl = New-AzStorageBlobSASToken -Context $account.Context -Container "service-packages" -Blob $appPackageFileName -Permission r -ExpiryTime ((Get-Date).AddHours(2)) -FullUri

$existingAppType = Get-AzServiceFabricApplicationType -ResourceGroupName $resourceGroupName -ClusterName $clusterName -Name $applicationTypeName -ErrorAction SilentlyContinue

$applicationParameters = @{}
Select-Xml -Path $appParametersFile -XPath "//*[local-name()='Parameter']" | Select-Object -exp Node | ForEach-Object {
  $applicationParameters[$_.Name] = $_.Value
}

$deployParameters = @{
  "clusterName" = $clusterName;
  "applicationTypeName" = $applicationTypeName;
  "applicationTypeVersion" = $applicationTypeVersion;
  "applicationPackageUrl" = "REDACTED";
  "applicationName" = $applicationName;
  "appTypeExists" = if ($null -ne $existingAppType) { $true } else { $false };
  "monitoredUpgrade" = $autoRollBack;
  "parameters" = $applicationParameters;
}

Write-Host "Deploying Application with the following parameters:"
$deployParameters | ConvertTo-Json -Depth 10

$deployParameters["applicationPackageUrl"] = $packageSasUrl


$attempt = "$($env:SYSTEM_STAGEATTEMPT).$($env:SYSTEM_PHASEATTEMPT).$($env:SYSTEM_JOBATTEMPT)"

Write-Host "Cleaning up old deployments"
Get-AzResourceGroupDeployment -ResourceGroupName $resourceGroupName |
  Where-Object DeploymentName -match "$applicationTypeName*" |
  Sort-Object Timestamp -Descending |
  Select-Object -skip 3 |
  Remove-AzResourceGroupDeployment

Write-Host "Deploying SF app to cluster..."
New-AzResourceGroupDeployment -ResourceGroupName $resourceGroupName -TemplateFile (Join-Path $PSScriptRoot app.bicep) -TemplateParameterObject $deployParameters -Mode Incremental -Name "$applicationTypeName.$applicationTypeVersion-$attempt"

Write-Host "Removing Old package versions..."
Get-AzServiceFabricApplicationTypeVersion -ResourceGroupName $resourceGroupName -ClusterName $clusterName -Name $applicationTypeName |
  Sort-Object Name -Descending |
  Select-Object -Skip 3 |
  Remove-AzServiceFabricApplicationTypeVersion -Force

