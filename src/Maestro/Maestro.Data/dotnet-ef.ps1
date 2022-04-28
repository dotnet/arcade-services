## How to Use
## 
## Call this script before running any `dotnet ef` commands. For example: 
## to run `dotnet ef database update`, at the command line, call: 
## .\dotnet-ef.ps1 database update

$extPath = Resolve-Path "..\..\..\artifacts\obj\Maestro.Data"
Write-Output "Adding MSBuild Project Extension Path: '$extPath'"

dotnet ef --msbuildprojectextensionspath "$extPath" @args
