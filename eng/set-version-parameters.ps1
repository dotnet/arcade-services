[cmdletbinding()]
param()

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Definition
Import-Module $here/helpers.psm1 3>$null 4>$null

# Get Most recent version Tag
$tag = git tag |
    Select-string -Pattern "^v[0-9]+\.[0-9]+\.[0-9]+$" |
    ForEach-Object{ New-Object PSObject -Property @{tag="$_"; distance=[int]$(git rev-list HEAD ^$_ --count)} } |
    Sort-Object distance |
    Select-Object -First 1 -ExpandProperty tag

if (-not $tag) {
  Write-Verbose "Defaulting to version 1.1.0"
  Write-Host "##vso[task.setvariable variable=VersionPrefix]$versionPrefix"
  exit 0
}

Write-Verbose "Got Previous Version Tag '$tag'"
if ($tag.StartsWith("refs/tags/"))
{
  $tag = $tag.Substring(10)
}
$previousVersion = [System.Version]$tag.Substring(1)
Write-Verbose "Got Previous Version '$previousVersion'"

# Get changes since previous version
$changeInfo = $(git log "$tag..HEAD" --format=format:%b)
if ($changeInfo | select-string "***BREAKING_CHANGE***" -SimpleMatch)
{
  $versionPrefix = New-Object System.Version @(($previousVersion.Major + 1), 0, 0)
  Write-Verbose "Changes since version '$previousVersion' contain a Breaking Change"
}
elseif ($changeInfo | select-string "*NEW_FEATURE*" -SimpleMatch)
{
  $versionPrefix = New-Object System.Version @($previousVersion.Major, ($previousVersion.Minor + 1), 0)
  Write-Verbose "Changes since version '$previousVersion' contain a new feature"
}
else
{
  $versionPrefix = New-Object System.Version @($previousVersion.Major, $previousVersion.Minor, ($previousVersion.Build + 1))
  Write-Verbose "Changes since version '$previousVersion' contain no interesting changes"
}

Write-Verbose "Using Version '$versionPrefix'"


Write-Host "##vso[task.setvariable variable=VersionPrefix]$versionPrefix"
