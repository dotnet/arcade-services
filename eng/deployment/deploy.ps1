param(
  [IO.FileInfo]$PublishProfile,
  [IO.DirectoryInfo]$ApplicationPackage,
  [string]$ApplicationName,
  [string]$ApplicationManifestPath,
  [string]$ServicesSourceFolder,
  [switch]$ForceUpgrade = $false
)

$VerbosePreference = "Continue"

Import-Module $PSScriptRoot\helpers.psm1 -Force

$publishProfileXml = [Xml] (Get-Content $PublishProfile)
$certificateSource = Read-XmlElementAsHashtable $publishProfileXml.PublishProfile.Item("CertificateSource");
$clusterConnectionParameters = Read-XmlElementAsHashtable $publishProfileXml.PublishProfile.Item("ClusterConnectionParameters");

$cert = Import-KeyVaultCertificate -KeyVaultName $certificateSource.VaultName -CertificateName $certificateSource.CertificateName -X509StoreName "My"

try {
  Connect-ServiceFabricCluster @clusterConnectionParameters
  $global:ClusterConnection = $ClusterConnection

  & "$PSScriptRoot/../refresh-service-fabric-services.ps1" -ApplicationName $ApplicationName -ApplicationManifestPath $ApplicationManifestPath -ServicesSourceFolder $ServicesSourceFolder

  $parameters = @{
    "PublishProfileFile" = $PublishProfile;
    "ApplicationPackagePath" = $ApplicationPackage;
    "UseExistingClusterConnection" = $true;
    "UnregisterUnusedApplicationVersionsAfterUpgrade" = $true;
  }

  if ($ForceUpgrade) {
      $parameters["OverrideUpgradeBehavior"] = "ForceUpgrade"
  }

  & "$PSScriptRoot\Deploy-FabricApplication.ps1" @parameters
}
catch {
  throw $_
}
finally {
  Remove-Certificate $cert -X509StoreName "My"
}
