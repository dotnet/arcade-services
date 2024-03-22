if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
  Write-Warning "Script must be run in Admin Mode!"
  exit 1
}

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

& "$PSScriptRoot\MaestroApplication\setup-localdb.ps1"
if (-not $?) {
  Write-Error "Failed to set up local db"
  exit $?
}

$groupName = "DncEngConfigurationUsers"
$userName = $(whoami)

$groupExists = Get-LocalGroup -Name $groupName -ErrorAction SilentlyContinue
$userIsMember = $groupExists | Get-LocalGroupMember | Where-Object { $_.Name -eq $userName }

if (-not $groupExists) {
  New-LocalGroup -Name $groupName -ErrorAction Continue
}

if (-not $userIsMember) {
  Add-LocalGroupMember -Group $groupName -Member $userName -ErrorAction Continue
}

dotnet tool restore
dotnet bootstrap-dnceng-configuration -r "https://vault.azure.net" -r "https://management.azure.com"
