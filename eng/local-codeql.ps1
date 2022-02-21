$loggerLevel = 'Standard'

Set-Location $(Join-Path $PSScriptRoot "..")

Write-Host $pwd

. $(Join-Path $pwd "eng\codeql.ps1")

$GdnCliPath = Install-Gdn -Path $(Join-Path $pwd "artifacts")

if (!$(Test-Path $GdnCliPath)) {
    throw "Unable to find Guardian CLI"
}

Write-Host "Guardian CLI is at $($GdnCliPath)"

Initialize-Gdn `
    -GuardianCliLocation $GdnCliPath `
    -WorkingDirectory $pwd `
    -LoggerLevel $loggerLevel

New-GdnSemmelConfig -GuardianCliLocation $GdnCliPath `
    -LoggerLevel 'Standard' `
    -Language 'csharp' `
    -WorkingDirectory $pwd `
    -SourceCodeDirectory $pwd\src `
    -OutputPath $(Join-Path $pwd ".gdn\r\semmle-csharp-configure.gdnconfig") `
    -Suite "SuiteSDLRecommended" `
    -BuildCommand "..\build.cmd -configuration Release"

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