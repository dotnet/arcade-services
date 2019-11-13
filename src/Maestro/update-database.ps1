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
  Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/dotnet-ef/3.0.0/dotnet-ef.3.0.0.nupkg" -OutFile "$env:TEMP\dotnet-ef.3.0.0.zip"
  Expand-Archive -Path "$env:TEMP\dotnet-ef.3.0.0.zip" -DestinationPath "$env:TEMP\dotnet-ef"
  return ((Get-ChildItem -Recurse ef.dll)[0].FullName)
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
