<#
.SYNOPSIS
    Generates cgmanifest.json for Grafana

.DESCRIPTION
    Grafana is not automatically detected by component governance scanner.
    Because of that we need to register it manually using cgmanifest.json file.
    This script dynamically generates cgmanifest.json based on the Grafana version
    we use to ensure that the version is always up to date in component governance.

.LINK
    https://docs.opensource.microsoft.com/tools/cg/cgmanifest.html
#>

$ErrorActionPreference = 'Stop'

$monitoringRootPath = Split-Path $PSScriptRoot
$version = Get-Content "$monitoringRootPath\grafana-init\grafana-version.txt"

Write-Host "Found Grafana version '$version'"

$cgmanifestContent = @"
{
    "Registrations": [
      {
        "Component": {
          "Type": "other",
          "Other": {
            "Name": "Grafana",
            "Version": "$version",
            "DownloadUrl": "https://dl.grafana.com/oss/release/grafana_${version}_amd64.deb"
          }
        }
      }
    ]
  }
"@

$cgmanifestPath = "$PSScriptRoot\cgmanifest.json" 

Write-Host "Writing cgmanifest to file '$cgmanifestPath'"
$cgmanifestContent | Set-Content $cgmanifestPath
