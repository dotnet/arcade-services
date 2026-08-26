#!/usr/bin/env bash
set -euo pipefail
# Verify: repeatable health check. Assumes the required .NET SDK (see global.json) is installed.
# Test filter excludes:
#   - Scenario tests (PostDeployment/Nightly/PreDeployment) - require a deployed service.
#   - Codeflow tests (Microsoft.DotNet.DarcLib.Codeflow.Tests) - slow on-disk git e2e tests.
TEST_FILTER="TestCategory!=PostDeployment&TestCategory!=Nightly&TestCategory!=PreDeployment&FullyQualifiedName!~Microsoft.DotNet.DarcLib.Codeflow.Tests"
"$(dirname "$0")/../eng/common/dotnet.sh" build
"$(dirname "$0")/../eng/common/dotnet.sh" test --no-build --filter "$TEST_FILTER"
