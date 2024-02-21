# Maestro

## Dependency update flow

The following diagram shows how a dependency update PR progresses from opening to merging. The flow is triggered when a subscription is triggered by a user or on a schedule.

```mermaid
flowchart
    SubscriptionTrigger(Subscription is triggered)
    Exist{Does a PR\nalready exist?}
    State{What state\nis the PR in?}
    PolicyState{What state\nare the check\npolicies in?}
    Create(Create a new PR)
    CleanUp((Clean up\nbranch))
    TagPeople(Notify/tag people)
    UpdatePR(Update PR,\nif possible)
    MergePR(Merge PR)
    UpdateLastBuild((Update\nLastAppliedBuild\nin BAR))
    Timer(Periodic timer)

    Exist--Yes-->State
    Exist--No -->Create
    SubscriptionTrigger-->Exist

    State--Open-->PolicyState
    State--Merged-->UpdateLastBuild
    State--Closed-->CleanUp

    PolicyState--Checks OK-->MergePR
    MergePR-->UpdateLastBuild
    PolicyState--Failed checks-->TagPeople
    TagPeople-->UpdatePR
    %% Cannot update
    PolicyState--Pending policies--Set timer-->Timer
    %% Can update
    PolicyState--Conflict-->UpdatePR
    Timer--Check PR-->State
    Create--Set timer-->Timer
    UpdatePR--Set timer-->Timer

subgraph Legend
    MaestroAction(Action)
    ExternalImpulse(Trigger)
    EndOfFlow((End of flow))
end

classDef Action fill:#00DD00,stroke:#006600,stroke-width:1px,color:#006600
classDef End fill:#9999EE,stroke:#0000AA,stroke-width:1px,color:#0000AA
classDef External fill:#FFEE00,stroke:#FF9900,stroke-width:1px,color:#666600
class Create,TagPeople,NoAction,UpdatePR,MergePR,MaestroAction Action
class UpdateLastBuild,CleanUp,EndOfFlow End
class SubscriptionTrigger,Timer,ExternalImpulse External
linkStyle 2,12 stroke-width:2px,fill:none,stroke:#FFEE00,color:#FF9900
```

## Validation Process in dev and int environments

For any non-deployment code changes, the expectation is to have run the tests corresponding to the service locally to confirm that the change works before putting up the PR. The tests for each of the major areas in arcade-services are as below:
- [Maestro](/test/Maestro.ScenarioTests)
- [Darc](tests/Microsoft.DotNet.Darc.Tests)

For any deployment changes, the only way to test would be to kick off the [build pipeline](https://dev.azure.com/dnceng/internal/_build?definitionId=252&_a=summary) that deploys the intended service from the dev branch to staging / int environment.

<Details>

<Summary>
:warning: :sweat: :boom:

**This comes with a significant overhead of a possibility of leaving the int deployments in a broken or non-responsive state, which then would require significant manual effort to undo the damage especially with the Service Fabric Clusters. This process should only be done if and only if absolutely necessary and after obtaining management approval.**

</Summary>

Steps:
- Run the `arcade-official-ci` pipeline (based on `azure-pipelines.yml`) from your dev branch.

- Once the testing is done, rerun the pipeline for main branch to return the deployment to a last known good.

</Details>

## BARViz Best Practices

### How to Understand the State of a Build and Channel

Each of the links on the left side bar represent a channel for a product. Clicking on one of these links will load the current build state for a channel.

The two main states of a channel’s current build are as follows:

- ✅ Current build is good.

- ⚠️ There are issues with this build. The message at the top of the build's page will give you more information as to the issue, but most likely is one of the following issues:
  - The repository is not running the latest version of the Arcade toolset. Older versions of the Arcade toolset are incompatible with determining the state of the build.
  - Latest builds are failing

While looking at a build, you may notice highlighted (in yellow) dependencies. These denote the dependencies that are an older version than another of the same dependency used in the build. This should help the teams to identify issues with dependency coherency within a build.

When mousing over a dependency, you may notice icons that show up along the left-hand side of the list. The icon at the top of the column provides help for understanding these icons. These icons are to assist in understanding the hierarchy of the dependency graph and to point out conflicting dependencies within the build.

### How to Understand Versions and Hashes Used for a Build

The number at the top of the build's page, next to the URL for the repository, is the current build number of the repository. The link of the hash below the URL will take you to the AzDO repository and the hash referenced so that the user can see all the commits that are a part of the latest build.

Similarly, each dependency denotes the version of that dependency used and a link to the hash of the latest commit that was contained in the build.

### How to Onboard a New Repository

If you would like to see your repository on BARViz, it needs to be published to the Build Asset Registry. Please see the [Darc documentation](https://github.com/dotnet/arcade/blob/main/Documentation/Darc.md).