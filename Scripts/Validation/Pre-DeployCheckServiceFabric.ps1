<#
.SYNOPSIS 
Test if Helix Service Fabric cluster exists

#>
param(
  [string]$PublishProfile
)

Import-Module $PSScriptRoot\..\ReleaseUtilities\Deployment\helpers.psm1 -Force

$serviceFabricPublishProfile = Read-PublishProfile $PublishProfile
$clusterConnectionParameters = $serviceFabricPublishProfile.ClusterConnectionParameters
$certificateSource = Read-XmlElementAsHashtable ([Xml] (Get-Content $PublishProfile)).PublishProfile.Item("CertificateSource");

Write-Output "Using the following to connect to the ServiceFabric Cluster:"
Write-Output $certificateSource
$cert = Import-KeyVaultCertificate -KeyVaultName $certificateSource.VaultName -CertificateName $certificateSource.CertificateName -X509StoreName "My"

try
{
  Connect-ServiceFabricCluster @clusterConnectionParameters
  $connected = Test-ServiceFabricClusterConnection
  if(-not $connected)
  {
      Write-Error "Could not connect to Service Fabric Cluster."
      exit 1
  }
}
catch {
  throw $_
}
finally {
  Remove-Certificate $cert -X509StoreName "My"
}
