$ErrorActionPreference = 'Stop'
# Verify: repeatable health check. Assumes the required .NET SDK (see global.json) is installed.
# Test filter excludes:
#   - Scenario tests (PostDeployment/Nightly/PreDeployment) - require a deployed service.
#   - Codeflow tests (Microsoft.DotNet.DarcLib.Codeflow.Tests) - slow on-disk git e2e tests.
$testFilter = "TestCategory!=PostDeployment&TestCategory!=Nightly&TestCategory!=PreDeployment&FullyQualifiedName!~Microsoft.DotNet.DarcLib.Codeflow.Tests"
& "$PSScriptRoot\..\eng\common\dotnet.ps1" build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& "$PSScriptRoot\..\eng\common\dotnet.ps1" test --no-build --filter $testFilter
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
