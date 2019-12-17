[cmdletbinding()]
param(
    [Parameter(Mandatory=$True)]
    [string]$Path
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$coverageFile = Get-ChildItem -Path $Path -Filter *.coverage -Recurse -ErrorAction SilentlyContinue -Force
Write-Host "Code coverage files found: "
Write-Host $coverageFile.FullName

$codeCoverageExe = "C:\Program Files (x86)\Microsoft Visual Studio\2017\Enterprise\Team Tools\Dynamic Code Coverage Tools\CodeCoverage.exe"

& $codeCoverageExe analyze /output:$Path\codecoverage.coveragexml $coverageFile.FullName

Write-Host "Was the coverage XML file created?"
Test-Path $Path\codecoverage.coveragexml -PathType Leaf
