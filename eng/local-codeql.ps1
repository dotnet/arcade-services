$loggerLevel = 'Standard'
$sdlSuite = 'SuiteSDLRecommended'

Set-Location $(Join-Path $PSScriptRoot "..")

Write-Host $pwd

. $(Join-Path $pwd "eng\codeql.ps1")

$GdnCliPath = Install-Gdn -Path $(Join-Path $pwd "artifacts") -Version 0.109.0

if (!$(Test-Path $GdnCliPath)) {
    throw "Unable to find Guardian CLI"
}

Write-Host "Guardian CLI is at $($GdnCliPath)"

Initialize-Gdn `
    -GuardianCliLocation $GdnCliPath `
    -WorkingDirectory $pwd `
    -LoggerLevel $loggerLevel

 New-GdnSemmleConfig -GuardianCliLocation $GdnCliPath `
     -LoggerLevel $loggerLevel `
     -Language 'csharp' `
     -WorkingDirectory $pwd `
     -SourceCodeDirectory $pwd\src `
     -OutputPath $(Join-Path $pwd ".gdn\r\semmle-csharp-configure.gdnconfig") `
     -Suite $sdlSuite `
     -BuildCommand "..\build.cmd -restore -configuration Release" `
     -Force

New-GdnSemmleConfig -GuardianCliLocation $GdnCliPath `
    -LoggerLevel $loggerLevel `
    -Language 'python' `
    -WorkingDirectory $pwd `
    -SourceCodeDirectory $pwd\src\Monitoring\grafana-init `
    -OutputPath $pwd\.gdn\r\semmle-python-configure.gdnconfig `
    -Suite $sdlSuite

.\eng\set-version-parameters.ps1

[xml]$manifest = Get-Content src\Maestro\MaestroApplication\ApplicationPackageRoot\ApplicationManifest.xml
$manifest.SelectSingleNode("/*[local-name()='ApplicationManifest']/*[local-name()='Policies']").RemoveAll()
$manifest.SelectSingleNode("/*[local-name()='ApplicationManifest']/*[local-name()='Principals']").RemoveAll()
$manifest.Save("src\Maestro\MaestroApplication\ApplicationPackageRoot\ApplicationManifest.xml")

[xml]$manifest = Get-Content src\Telemetry\TelemetryApplication\ApplicationPackageRoot\ApplicationManifest.xml
$manifest.SelectSingleNode("/*[local-name()='ApplicationManifest']/*[local-name()='Policies']").RemoveAll()
$manifest.SelectSingleNode("/*[local-name()='ApplicationManifest']/*[local-name()='Principals']").RemoveAll()
$manifest.Save("src\Telemetry\TelemetryApplication\ApplicationPackageRoot\ApplicationManifest.xml")


 Invoke-GdnSemmle -GuardianCliLocation $GdnCliPath `
     -WorkingDirectory $pwd `
     -ConfigurationPath $(Join-Path $pwd ".gdn\r\semmle-csharp-configure.gdnconfig")

Invoke-GdnSemmle -GuardianCliLocation $GdnCliPath `
    -WorkingDirectory $pwd `
    -ConfigurationPath $(Join-Path $pwd ".gdn\r\semmle-python-configure.gdnconfig")