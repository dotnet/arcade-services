---
name: Rollout issue
about: Create issue for arcade-services rollout
title: Rollout YYYY-MM-DD
labels: Rollout
assignees: ''

---

# Purpose

This issue tracks the `arcade-services` repository rollout. On top of the [Rollout](https://dev.azure.com/dnceng/internal/_wiki/wikis/DNCEng%20Services%20Wiki/831/Rollout) instructions described on the wiki, it provides the person responsible for the rollout checklist of the steps that need to be performed to rollout services in this repository. All relevant information, including the rollout PR, issues encountered during the rollout and steps taken to resolve them should be linked or added to this issue to keep full audit trail of changes rolled out to production.

# Process

## Build status check
- [ ] Check the status of the [dotnet-arcade-services-weekly](https://dev.azure.com/dnceng/internal/_build?definitionId=993) pipeline
- [ ] Rotate any secrets that need manual rotation
- [ ] Check the status of the [arcade-services-internal-ci](https://dev.azure.com/dnceng/internal/_build?definitionId=252) pipeline. Try to fix issues, if any, so that we have a green build before the rollout day.
- [ ] Check the `Rollout` column in the [Product Construction](https://github.com/orgs/dotnet/projects/276) board - move any issues rolled-out last week into `Done`

## Rollout preparation
- [ ] Create the rollout PR:
  - Find a commit on `main` that you want to rollout
  - Create a branch named `rollout/YYYY-MM-DD` from that commit
  - Create a PR on GitHub from the `rollout/YYYY-MM-DD` branch to `production`
  - Name the PR `[Rollout] Production rollout YYYY-MM-DD`
  - Link this issue in the PR description
- [ ] Link the rollout PR to the [Rollout PRs](#rollout-prs) section of this issue
- [ ] Merge the prepared rollout PR (⚠️ **DO NOT SQUASH**)
- [ ] Link the rollout build to the [Rollout build](#rollout-build) section of this issue
- [ ] Verify that Maestro opened a `production => main` PR in `arcade-services` with the rollout merge commit ([example](https://github.com/dotnet/arcade-services/pull/2741)). There should be no changes in the PR to any files. **Do not merge the PR yet**.
- [ ] Ensure the build is green and stops at the `Approval` phase

## Rollout
- [ ] Approve the `Approval` stage of the rollout build.
- [ ] Monitor the rollout build for failures.
  - Note: this [Maestro exceptions query](https://ms.portal.azure.com/#view/Microsoft_OperationsManagementSuite_Workspace/Logs.ReactView/resourceId/%2Fsubscriptions%2F68672ab8-de0c-40f1-8d1b-ffb20bd62c0f%2FresourceGroups%2Fmaestro-prod-cluster%2Fproviders%2Fmicrosoft.insights%2Fcomponents%2Fmaestro-prod/source/LogsBlade.AnalyticsShareLinkToQuery/q/H4sIAAAAAAAAAz2MOw6DMBBE%252B5xiSlsiRZDS5i7GjGQXu0brRSSIwyekoH4fvjMXr0377cBWaIRXYfckC17QtoV4H%252Bcf7KtIsroTua3qIWL6YKoaLn%252FA4ylxgNBLOxOjzrT%252FMJdk%252FgV08ryabQAAAA%253D%253D) might help in diagnosing issues.
- [ ] Keep track of any issues encountered during the rollout either directly in this issue, or in a dedicated issue linked to this issue
- [ ] When finished, update the rollout stats in the [Stats](#stats) section below. The statistics will be available in Kusto a few minutes after the build was finished
- [ ] Merge the `production => main` PR in `arcade-services` (⚠️ **DO NOT SQUASH**)
- [ ] Move rolled-out issues/PRs in the `Rollout` column of the [Product Construction](https://github.com/orgs/dotnet/projects/276) board into `Done`. Verify that PRs have a reference to the release at the bottom ([example](https://github.com/dotnet/arcade-services/pull/3663)). If needed, manually add a comment with the reference ([example](https://github.com/dotnet/arcade-services/pull/3680#issuecomment-2191186247))
- [ ] Close this issue with closing comment describing a high-level summary of issues encountered during the rollout
- In case of rollback, uncomment the *Rollback* section below and follow the steps there

<!-- UNCOMMENT HERE IN CASE OF A ROLLBACK
## Rollback

In case the services don't work as expected after the rollout, it's necessary to roll back.

- [ ] Announce the issues on the [Rollout channel](https://teams.microsoft.com/l/channel/19%3a72e283b51f9e4567ba24a35328562df4%40thread.skype/Rollout?groupId=147df318-61de-4f04-8f7b-ecd328c256bb&tenantId=72f988bf-86f1-41af-91ab-2d7cd011db47), rollout issue in [AzDO](https://dev.azure.com/dnceng/internal/_workitems/)
- [ ] Notify the partners that we'll be rolling back
- [ ] Rollback as described on the [Rollback / Hotfix](https://dev.azure.com/dnceng/internal/_wiki/wikis/DNCEng%20Services%20Wiki/831/Rollout?anchor=rollback-/-hotfix)  wiki page
- [ ] Validate the rolled-back services are running as expected
- [ ] Announce successful rollout on the [Rollout channel](https://teams.microsoft.com/l/channel/19%3a72e283b51f9e4567ba24a35328562df4%40thread.skype/Rollout?groupId=147df318-61de-4f04-8f7b-ecd328c256bb&tenantId=72f988bf-86f1-41af-91ab-2d7cd011db47)
- [ ] Notify the partners that the rollback has been finished (as reply on the original email)

### Rollback PRs
- `<TO BE FILLED (IF APPICABLE)>`
-->

# Rollout data

## Rollout PRs

* The main PR: <TO BE FILLED>

## Rollout build

* Rollout AzDO build: <TO BE FILLED>

## Rollout times

Use the following [Kusto query](https://dataexplorer.azure.com/clusters/engsrvprod/databases/engineeringdata?query=H4sIAAAAAAAAA52QP0%2FDQAzF934KK0tzUlgYU2UAtUJdUNWyIRSZxG0O3eWCzwHKn%2B%2BOE4oIjNxkvbN%2Fz341VQ6Z4LEnPpYdMnoS4phug3Ohl3WdgwvtAQqzmDkSuCJZ9oxiQwsFpKU9NWRQ7i1H2QkeKIcobEfR4R%2FNwNsM9N1YT862tKUqcB1H7R2eG9JtLnvr6nUNRQFq8OvrWhcE26YTt6mLOTXH3ntk%2B0rqiiy6qteh3VAPxiYDautBxZeJehruODxQJTA5dB%2FYo5SiXbHDNh2mz77YGSRNk3ufaEIfi1mnR8oIup1vmC46pT2hg3RsMvM7pUmIFWru6STNn8QVOAaQaLHp752NDfRRo4MlcpUYk33jQ5T%2F8ZfUuXAcDFbnKxCKEpW7%2BATR1TCdDgIAAA%3D%3D) to gather data about rollout times:

* Pre-Approval run time: `<TO BE FILLED>`
* Post-Approval run time: `<TO BE FILLED>`

# Useful links

- AzDO pipelines
  - [arcade-services-internal-ci](https://dev.azure.com/dnceng/internal/_build?definitionId=252)
  - [dotnet-arcade-services-weekly](https://dev.azure.com/dnceng/internal/_build?definitionId=993)
- [Rollout channel](https://teams.microsoft.com/l/channel/19%3a72e283b51f9e4567ba24a35328562df4%40thread.skype/Rollout?groupId=147df318-61de-4f04-8f7b-ecd328c256bb&tenantId=72f988bf-86f1-41af-91ab-2d7cd011db47)
- [Maestro exceptions](https://ms.portal.azure.com/#view/Microsoft_OperationsManagementSuite_Workspace/Logs.ReactView/resourceId/%2Fsubscriptions%2F68672ab8-de0c-40f1-8d1b-ffb20bd62c0f%2FresourceGroups%2Fmaestro-prod-cluster%2Fproviders%2Fmicrosoft.insights%2Fcomponents%2Fmaestro-prod/source/LogsBlade.AnalyticsShareLinkToQuery/q/H4sIAAAAAAAAAz2MOw6DMBBE%252B5xiSlsiRZDS5i7GjGQXu0brRSSIwyekoH4fvjMXr0377cBWaIRXYfckC17QtoV4H%252Bcf7KtIsroTua3qIWL6YKoaLn%252FA4ylxgNBLOxOjzrT%252FMJdk%252FgV08ryabQAAAA%253D%253D)
- [Deployment Policy](https://github.com/dotnet/core-eng/blob/main/Documentation/Policy/DeploymentPolicy.md)
