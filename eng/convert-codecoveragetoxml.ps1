[cmdletbinding()]
param(
    [Parameter(Mandatory=$True)]
    [string]$Path,
    [Parameter(Mandatory=$True)]
    [string]$NugetPackagesPath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$coverageFile = Get-ChildItem -Path $Path -Filter *.coverage -Recurse -ErrorAction SilentlyContinue -Force
Write-Host "Code coverage files found: "
Write-Host $coverageFile.FullName

$codeCoverageExe = (Get-ChildItem -Path $NugetPackagesPath -Filter "CodeCoverage.exe" -Recurse -ErrorAction SilentlyContinue -Force | Select-Object FullName -First 1).FullName

& $codeCoverageExe analyze /output:$Path\codecoverage.coveragexml $coverageFile.FullName

Write-Host "Was the coverage XML file created?"
Test-Path $Path\codecoverage.coveragexml -PathType Leaf
