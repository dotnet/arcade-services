# End to End ScenarioM Testing

This directory contains end to end scenario tests to run on a fully deployed Maestro deployment.  The tests will first attempt to clean up existing resources needed for testing, then they will attempty to run various end-to-end scenarios.

## General requirements
The general requirement is that the script is provided with a Bearer token, GitHub PAT, and AzDO PAT required for executing the Maestro commands.

### INT vs. PROD resources

The **INT** deployment is configured to run against following resources. Note that the INT deployment cannot authorize against the dotnet organization.

- GitHub repos:
    - https://github.com/maestro-auth-test/maestro-test1
    - https://github.com/maestro-auth-test/maestro-test2
    - https://github.com/maestro-auth-test/maestro-test3
- AzDO:
    - https://dnceng@dev.azure.com/dnceng/internal/_git/maestro-test1
    - https://dnceng@dev.azure.com/dnceng/internal/_git/maestro-test2
    - https://dnceng@dev.azure.com/dnceng/internal/_git/maestro-test3

The **PROD** deployment is configured to run against the following resources:
- GitHub repos:
    - https://github.com/maestro-auth-test/maestro-test1
    - https://github.com/maestro-auth-test/maestro-test2
    - https://github.com/maestro-auth-test/maestro-test3
- AzDO:
    - https://dnceng@dev.azure.com/dnceng/internal/_git/maestro-test1
    - https://dnceng@dev.azure.com/dnceng/internal/_git/maestro-test2
    - https://dnceng@dev.azure.com/dnceng/internal/_git/maestro-test3

## Scenarios
Each general scenario is represented by a separate powershell script.

