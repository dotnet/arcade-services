$ErrorActionPreference = 'Stop';
Set-PSDebug -Trace 1

$packageName= 'sql-server-express'
$url        = 'https://download.microsoft.com/download/8/4/c/84c6c430-e0f5-476d-bf43-eaaa222a72e0/SQLEXPR_x64_ENU.exe'
$silentArgs = "/IACCEPTSQLSERVERLICENSETERMS /Q /ACTION=install /INSTANCEID=SQLEXPRESS /INSTANCENAME=SQLEXPRESS /UPDATEENABLED=FALSE"

$tempDir = Join-Path (Get-Item $env:TEMP).FullName "$packageName"

if (![System.IO.Directory]::Exists($tempDir)) { [System.IO.Directory]::CreateDirectory($tempDir) | Out-Null }
$fileFullPath = "$tempDir\SQLEXPR.exe"

(New-Object System.Net.WebClient).DownloadFile($url, $fileFullPath )

Write-Host "Extracting..."
$extractPath = "$tempDir\SQLEXPR"
Start-Process $fileFullPath "/Q /x:`"$extractPath`"" -Wait

Write-Host "Installing..."
$setupPath = "$extractPath\setup.exe"
Start-Process $setupPath $silentArgs -Wait

Write-Host "Removing extracted files..."
Remove-Item -Recurse $extractPath
