$monitoringRootPath = Split-Path $PSScriptRoot
$version = Get-Content "$monitoringRootPath\grafana-init\grafana-version.txt"
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

$cgmanifestContent | Set-Content "$PSScriptRoot\cgmanifest.json" 
