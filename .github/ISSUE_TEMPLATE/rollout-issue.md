---
name: Rollout issue
about: 'Create issue for arcade-services rollout '
title: Rollout DD-MM-YYYY
labels: Rollout
assignees: ''

---

# Purpose

This issue tracks the `arcade-services` repository rollout. On top of the [Rollout](https://dev.azure.com/dnceng/internal/_wiki/wikis/DNCEng%20Services%20Wiki/831/Rollout) instructions described on the wiki, it provides the person responsible for the rollout checklist of the steps that should to be performed to rollout services in this repository. All relevant information, including the rollout PR, issues encountered during the rollout and steps taken to resolve them should be linked or added to this issue to keep full audit trail of changes rolled out to production.

# Process

## Build status check (Monday)
- [ ] Check the status of the [dotnet-arcade-services-weekly](https://dev.azure.com/dnceng/internal/_build?definitionId=993) pipeline
- [ ] Ensure the [arcade-services-internal-ci](https://dev.azure.com/dnceng/internal/_build?definitionId=252) pipeline is green
- [ ] In case there is a problem with the CI build, notify the [Rollout channel](https://teams.microsoft.com/l/channel/19%3a72e283b51f9e4567ba24a35328562df4%40thread.skype/Rollout?groupId=147df318-61de-4f04-8f7b-ecd328c256bb&tenantId=72f988bf-86f1-41af-91ab-2d7cd011db47)

## Rollout preparation (Tuesday)
- [ ] Wait for the vendor to prepare the rollout. This includes a thread on the [Rollout channel](https://teams.microsoft.com/l/channel/19%3a72e283b51f9e4567ba24a35328562df4%40thread.skype/Rollout?groupId=147df318-61de-4f04-8f7b-ecd328c256bb&tenantId=72f988bf-86f1-41af-91ab-2d7cd011db47), rollout issue in [AzDO](https://dev.azure.com/dnceng/internal/_workitems/) and the rollout PR
- [ ] Link the rollout PR to the "Rollout data" section of this issue
- [ ] Double-check that the release notes contain all information
- [ ] Merge the already prepard rollout PR
- [ ] Ensure the build is green and stops on the "Approval" phase

## Rollout day (Wednesday)
- [ ] Approve the rollout build (that has been already started the day before)
- [ ] Monitor the rollout build for failures. The [Maestro exceptions](https://ms.portal.azure.com/#view/Microsoft_OperationsManagementSuite_Workspace/Logs.ReactView/resourceId/%2Fsubscriptions%2F68672ab8-de0c-40f1-8d1b-ffb20bd62c0f%2FresourceGroups%2Fmaestro-prod-cluster%2Fproviders%2Fmicrosoft.insights%2Fcomponents%2Fmaestro-prod/source/LogsBlade.AnalyticsShareLinkToQuery/q/H4sIAAAAAAAAAz2MOw6DMBBE%252B5xiSlsiRZDS5i7GjGQXu0brRSSIwyekoH4fvjMXr0377cBWaIRXYfckC17QtoV4H%252Bcf7KtIsroTua3qIWL6YKoaLn%252FA4ylxgNBLOxOjzrT%252FMJdk%252FgV08ryabQAAAA%253D%253D) query might help in diagnosing issues.
- [ ] Keep track of any issues encountered during the rollout either directly in this issue, or in a dedicated issue linked to this issue
- [ ] Update the rollout stats in the "Stats" section below
- [ ] Notify the [Rollout channel](https://teams.microsoft.com/l/channel/19%3a72e283b51f9e4567ba24a35328562df4%40thread.skype/Rollout?groupId=147df318-61de-4f04-8f7b-ecd328c256bb&tenantId=72f988bf-86f1-41af-91ab-2d7cd011db47)
- [ ]  Close this issue with closing comment describing a high-level summary of issues encountered during the rollout

## Rollback

In case the services don't work as expected after the rollout, it's necessary to roll back.

- [ ] Announce the issues on the [Rollout channel](https://teams.microsoft.com/l/channel/19%3a72e283b51f9e4567ba24a35328562df4%40thread.skype/Rollout?groupId=147df318-61de-4f04-8f7b-ecd328c256bb&tenantId=72f988bf-86f1-41af-91ab-2d7cd011db47), rollout issue in [AzDO](https://dev.azure.com/dnceng/internal/_workitems/)
- [ ] Notify the partners that we'll be rolling back
- [ ] Rollback as described on the [Rollback / Hotfix](https://dev.azure.com/dnceng/internal/_wiki/wikis/DNCEng%20Services%20Wiki/831/Rollout?anchor=rollback-/-hotfix)  wiki page
- [ ] Validate the rolled-back services are running as expected
- [ ] Announce successful rollout on the [Rollout channel](https://teams.microsoft.com/l/channel/19%3a72e283b51f9e4567ba24a35328562df4%40thread.skype/Rollout?groupId=147df318-61de-4f04-8f7b-ecd328c256bb&tenantId=72f988bf-86f1-41af-91ab-2d7cd011db47)
- [ ] Notify the partners that the rollback has been finished (as reply on the original email)

# Rollout data

## Rollout PRs

* The main PR: `<TO BE FILLED>`
* Rollback PRs: `<TO BE FILLED, IF APPICABLE>`

## Rollout times

Use the following [Kusto query](https://dataexplorer.azure.com/clusters/engsrvprod/databases/engineeringdata?query=H4sIAAAAAAAAA51Ry07DQAy85yusXEiq0EIPIBr1QJWqVEJQtcAFoWpJ3GbRPqJdhzf/jhMKChzZkzX2zHi8CgmWVilb07yAMQx6k+v5eQbzDGaX0xWcTZfTBLC/7cNweHhwcnTcG6SBYtYMKaudIGkN86K1LEagrNkmsN5I52lFYosj8ORkCyrxB4vhLQB+V1KjkgaXmFtX+BZ7h6cSHcKklqpoFhsDG/xqXQiNIE3Uceu6xLthX2stnHxFdhWOeFXNpFVTN8YxhzNNcC2eO+iOXDn7gDlBJ+jGOi1oTTzlK2Gihr3/pZ1AWJYjrcM4DT7SoOKQ1Ard7i0cnlas9igURO1QvHfHamR9LpRwUeea0c9/sGB7gJCLRX2vpC+h9nw6yITLwzhOvuWtp//pZ1gp+9IY3AglC0EIRQtpNMQO6SfbzDU/IQIAAA==) to gather data about rollout times:

Pre-Approval run time: `<TO BE FILLED>`
Post-Approval run time: `<TO BE FILLED>`

# Useful links

- AzDO pipelines
  - [arcade-services-internal-ci](https://dev.azure.com/dnceng/internal/_build?definitionId=252)
  - [dotnet-arcade-services-weekly](https://dev.azure.com/dnceng/internal/_build?definitionId=993)
- [Rollout channel](https://teams.microsoft.com/l/channel/19%3a72e283b51f9e4567ba24a35328562df4%40thread.skype/Rollout?groupId=147df318-61de-4f04-8f7b-ecd328c256bb&tenantId=72f988bf-86f1-41af-91ab-2d7cd011db47)
- [Maestro exceptions](https://ms.portal.azure.com/#view/Microsoft_OperationsManagementSuite_Workspace/Logs.ReactView/resourceId/%2Fsubscriptions%2F68672ab8-de0c-40f1-8d1b-ffb20bd62c0f%2FresourceGroups%2Fmaestro-prod-cluster%2Fproviders%2Fmicrosoft.insights%2Fcomponents%2Fmaestro-prod/source/LogsBlade.AnalyticsShareLinkToQuery/q/H4sIAAAAAAAAAz2MOw6DMBBE%252B5xiSlsiRZDS5i7GjGQXu0brRSSIwyekoH4fvjMXr0377cBWaIRXYfckC17QtoV4H%252Bcf7KtIsroTua3qIWL6YKoaLn%252FA4ylxgNBLOxOjzrT%252FMJdk%252FgV08ryabQAAAA%253D%253D)
- [Deployment Policy](https://github.com/dotnet/core-eng/blob/main/Documentation/Policy/DeploymentPolicy.md)
