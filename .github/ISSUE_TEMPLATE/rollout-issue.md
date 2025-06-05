---
name: Rollout issue
about: Create issue for arcade-services rollout
title: Rollout YYYY-MM-DD
labels: Rollout
assignees: ''

---

# Purpose

This issue tracks the `arcade-services` repository rollout. It provides the person responsible for the rollout checklist of the steps that need to be performed to rollout services in this repository.
All relevant information, including the rollout PR, issues encountered during the rollout and steps taken to resolve them should be linked or added to this issue to keep a full audit trail of changes rolled out to production.

# Process

## Build status check
- [ ] Check the status of the [dotnet-arcade-services-weekly](https://dev.azure.com/dnceng/internal/_build?definitionId=993) pipeline
- [ ] Check the status of the [arcade-services-internal-ci](https://dev.azure.com/dnceng/internal/_build?definitionId=252) pipeline.

## Rollout preparation
- [ ] Assign this issue to the FR area and to the current sprint.
- [ ] Create the rollout PR:
  - Find a commit on `main` that you want to rollout
  - Create a branch named `rollout/YYYY-MM-DD` from that commit
  - Create a PR on GitHub from the `rollout/YYYY-MM-DD` branch to `production`
  - Name the PR `[Rollout] Production rollout YYYY-MM-DD`
  - Link this issue in the PR description
- [ ] Merge (⚠️ **DO NOT SQUASH**) the prepared rollout PR
- [ ] Verify that a `production => main` PR was opened in `arcade-services` with the rollout merge commit ([example](https://github.com/dotnet/arcade-services/pull/2741)). There should be no changes in the PR to any files. **Do not merge the PR yet**.
- [ ] Ensure the build is green and stops at the `Approval` phase

## Rollout
- [ ] Approve the `Approval` stage of the rollout build.
- [ ] Monitor the rollout build for failures.
  - Note: this [PCS exceptions query](https://ms.portal.azure.com#@72f988bf-86f1-41af-91ab-2d7cd011db47/blade/Microsoft_OperationsManagementSuite_Workspace/Logs.ReactView/resourceId/%2Fsubscriptions%2Ffbd6122a-9ad3-42e4-976e-bccb82486856%2FresourceGroups%2Fproduct-construction-service%2Fproviders%2Fmicrosoft.insights%2Fcomponents%2Fproduct-construction-service-ai-prod/source/LogsBlade.AnalyticsShareLinkToQuery/q/H4sIAAAAAAAAAz2MOw6DMBBE%252B5xiSlsiRZDS5i7GjGQXu0brRSSIwyekoH4fvjMXr0377cBWaIRXYfckC17QtoV4H%252Bcf7KtIsroTua3qIWL6YKoaLn%252FA4ylxgNBLOxOjzrT%252FMJdk%252FgV08ryabQAAAA%253D%253D) might help in diagnosing issues.
  - Keep track of any issues encountered during the rollout either in this issue (or in a dedicated issue linked to this one)
- [ ] Merge (⚠️ **DO NOT SQUASH**) the `production => main` PR in `arcade-services`
- [ ] Close this issue with closing comment describing a high-level summary of issues encountered during the rollout
- In case of rollback, uncomment the *Rollback* section below and follow the steps there

<!-- UNCOMMENT HERE IN CASE OF A ROLLBACK
## Rollback

A rollback was necessary during this rollout.

- [ ] Announce the issues on the [Rollout channel](https://teams.microsoft.com/l/channel/19%3a72e283b51f9e4567ba24a35328562df4%40thread.skype/Rollout?groupId=147df318-61de-4f04-8f7b-ecd328c256bb&tenantId=72f988bf-86f1-41af-91ab-2d7cd011db47), rollout issue in [AzDO](https://dev.azure.com/dnceng/internal/_workitems/)
- [ ] Notify the partners that we'll be rolling back
- [ ] Roll back by re-running a previous rollout's `Deploy` stage
- [ ] Validate the rolled-back services are running as expected
- [ ] Announce successful rollout on the [Rollout channel](https://teams.microsoft.com/l/channel/19%3a72e283b51f9e4567ba24a35328562df4%40thread.skype/Rollout?groupId=147df318-61de-4f04-8f7b-ecd328c256bb&tenantId=72f988bf-86f1-41af-91ab-2d7cd011db47)
- [ ] Notify the partners that the rollback has been finished (as reply on the original email)
-->

# Useful links

- AzDO pipelines
  - [arcade-services-internal-ci](https://dev.azure.com/dnceng/internal/_build?definitionId=252)
  - [dotnet-arcade-services-weekly](https://dev.azure.com/dnceng/internal/_build?definitionId=993)
- [Rollout channel](https://teams.microsoft.com/l/channel/19%3a72e283b51f9e4567ba24a35328562df4%40thread.skype/Rollout?groupId=147df318-61de-4f04-8f7b-ecd328c256bb&tenantId=72f988bf-86f1-41af-91ab-2d7cd011db47)
- [PCS exceptions](https://ms.portal.azure.com#@72f988bf-86f1-41af-91ab-2d7cd011db47/blade/Microsoft_OperationsManagementSuite_Workspace/Logs.ReactView/resourceId/%2Fsubscriptions%2Ffbd6122a-9ad3-42e4-976e-bccb82486856%2FresourceGroups%2Fproduct-construction-service%2Fproviders%2Fmicrosoft.insights%2Fcomponents%2Fproduct-construction-service-ai-prod/source/LogsBlade.AnalyticsShareLinkToQuery/q/H4sIAAAAAAAAAz2MOw6DMBBE%252B5xiSlsiRZDS5i7GjGQXu0brRSSIwyekoH4fvjMXr0377cBWaIRXYfckC17QtoV4H%252Bcf7KtIsroTua3qIWL6YKoaLn%252FA4ylxgNBLOxOjzrT%252FMJdk%252FgV08ryabQAAAA%253D%253D)
- [Deployment Policy](https://github.com/dotnet/core-eng/blob/main/Documentation/Policy/DeploymentPolicy.md)
