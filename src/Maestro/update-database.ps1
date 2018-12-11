[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)]
  $migrationsDll,
  [Parameter(Mandatory=$false)]
  $startupDll
)

$ErrorActionPreference = "Stop"

function removeDllExtension {
  param($path)

  if ($path.EndsWith(".dll")) {
    $path = $path.Substring(0, $path.Length - ".dll".Length)
  }
  return $path
}

function Get-EfDllPath {
  $dotnetLocation = Split-Path -Parent (Get-Command dotnet).Path
  $sdkVersion = dotnet --version
  $sdkToolsPath = "$dotnetLocation\sdk\$sdkVersion\DotnetTools"
  $dotnetEfToolPath = Join-Path $sdkToolsPath dotnet-ef
  $dotnetEfVersion = ls $dotnetEfToolPath | select -ExpandProperty Name | sort -Descending | select -First 1

  return "$dotnetEfToolPath\$dotnetEfVersion\tools\netcoreapp2.1\any\tools\netcoreapp2.0\any\ef.dll"
}

$migrationsNamespace = removeDllExtension $migrationsDll


if (-not $startupDll) {
  $startupDll = $migrationsDll
}

$depsJson = (removeDllExtension $startupDll) + ".deps.json"
$runtimeConfig = (removeDllExtension $startupDll) + ".runtimeconfig.json"

$efDll = Get-EfDllPath
Write-Verbose "Using ef.dll from path $efDll"


dotnet exec --depsfile $depsJson --runtimeconfig $runtimeConfig $efDll database update --assembly $migrationsDll --startup-assembly $startupDll --root-namespace $migrationsNamespace --project-dir . --verbose
