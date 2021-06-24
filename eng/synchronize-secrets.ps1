param(
  [string]$ConfigDir,
  [string]$Command = "dotnet secret-manager"
)

$configPath = Join-Path $ConfigDir "*.yaml"

Get-ChildItem $configPath | %{
  Invoke-Expression "$Command synchronize $($_.FullName)"
}
