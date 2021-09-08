param(
    [IO.FileInfo]$PublishProfile,
    [string]$ApplicationName,
    [string]$ApplicationManifestPath,
    [string]$ServicesSourceFolder
)

$VerbosePreference = "Continue"

Import-Module $PSScriptRoot\deployment\helpers.psm1 -Force

$publishProfileXml = [Xml] (Get-Content $PublishProfile)
$certificateSource = Read-XmlElementAsHashtable $publishProfileXml.PublishProfile.Item("CertificateSource");
$clusterConnectionParameters = Read-XmlElementAsHashtable $publishProfileXml.PublishProfile.Item("ClusterConnectionParameters");

$cert = Import-KeyVaultCertificate -KeyVaultName $certificateSource.VaultName -CertificateName $certificateSource.CertificateName -X509StoreName "My"

try {
    Write-Host "Trying to connect to the service fabric cluster..."
    Connect-ServiceFabricCluster @clusterConnectionParameters
    $global:ClusterConnection = $ClusterConnection
    Get-ServiceFabricClusterHealth -ConsiderWarningAsError $False
}
catch {
    Write-Host "Unable to connect to the service fabric cluster. Please check if the right certificates are being used."
}
finally {
    Remove-Certificate $cert -X509StoreName "My"
}
