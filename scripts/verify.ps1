$ErrorActionPreference = 'Stop'
# Verify: repeatable health check. Assumes the required .NET SDK (see global.json) is installed.
dotnet build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet test --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
