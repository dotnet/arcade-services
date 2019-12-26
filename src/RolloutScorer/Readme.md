# Rollout Scorer

The Rollout Scorer is a tool for generating rollout scorecards.

## Tool Description
The rollout scorer takes two commands: `score` and `upload`.

### `score`

The `score` command calculates a particular rollout's score and generates the scorecard which it outputs as a CSV for review. Optionally, it can skip this output step and upload the results directly.

|            Argument            |  Required?   |              Description              |
|:------------------------------:|:------------:|:--------------------------------------|
|       `--repo` or `-r`         | **Required** | The repository to score               |
|      `--branch` or `-b`        |  *Optional*  | The branch of the repo to score(e.g. servicing or prod); defaults to production |
| `--rollout-start-date` or `-s` | **Required** | The date on which the rollout started |
|  `--rollout-end-date` or `-e`  |  *Optional*  | The date on which the rollout ended; defaults to current date |
|     `--manual-rollbacks`       |  *Optional*  | The number of rollbacks which weren't deployed by builds (e.g. clicking a button to return to a previous state); defaults to 0 |
|     `--manual-hotfixes`        |  *Optional*  | Any extra hotfixes that won't be tracked by the tool (e.g. database changes) |
|     `--assume-no-tags`         |  *Optional*  | Assumes no `[HOTFIX]` tags and just calculates hotfixes based on number of deployments after the first |
|    `--downtime` or `-d`        |  *Optional*  | A TimeSpan specifying an amount of downtime which occurred; defaults to 0 |
|     `--failed` or `-f`         |  *Optional*  | Indicates a failed rollout |
|     `--output` or `-o`         |  *Optional*  | File which the generated csv will be outputted to; defaults to `./{repo}-scorecard.csv` |
|       `--skip-output`          |  *Optional*  | Skips the output step and directly uploads results |
|      `--help` or `-h`          |  *Optional*  | Display help message and exit |

### Upload

The `upload` command takes a series of inline arguments which specify the locations of the scorecard CSV files to upload. Each of these files will be combined into a single scorecard document.

"Uploading" the file here means making a PR to core-eng containing adding the scorecard to `/Documentation/Rollout-Scorecards/` and placing the data in Kusto which backs a PowerBI dashboard.

### Usage

The flow for using the Rollout Scorer is as follows:
* Run `RolloutScorer.exe score` and specify the repo, rollout start date, and any optional parameters
* The Rollout Scorer will scrape AzDO for the appropriate data and create a CSV file containing the scorecard data
* User can make manual corrections to the CSV file as necessary
* Run `RolloutScorer.exe upload {csv}` and the Rollout Scorer will upload the CSV file to Kusto and AzDO

## Score Calculation
The Rollout Scorer scrapes the specified repo's build definition for all of the builds (targeting the production branch) that occurred within the specified timeframe. From this data it will calculate:

* **Total rollout time** &mdash; The sum of all build times
* **Number of critical issues** &mdash; Calculated from GitHub issues with the `Rollout Issue` label
* **Number of hotfixes** &mdash; Calculated from number of builds after the first one to reach a stage with "deploy" in its name which are tagged `[HOTFIX]` (but not also tagged `[ROLLBACK]`) and have also reached a stage with "deploy" in its name (unless `--assume-no-tags` is specified, in which case it's simply the number of builds after the first); additional manual hotfixes are calculated from the number of GitHub issues with the `Rollout Hotfix` label and can also be specified by the user
* **Number of rollbacks** &mdash; Same as hotfixes, except for searching only for builds tagged `[ROLLBACK]` and GitHub issues labeled `Rollout Rollback`; doesn't respect `--assume-no-tags`
* **Downtime** &mdash; Calculated from GitHub issues with the `Rollout Downtime` label (downtime is specified in the issue/its comments or else calculated from creation/close time); additional downtime can be specified by the user
* **Failure to rollout** &mdash; If the last deployed build is a failure, the rollout failed; can also be manually specified by the user 

## Config.json

The `config.json` file contains all of the config information needed to calculate the rollout score. It is divided into three sections, defining the configs for Repos, AzDO instances, and rollout score weights.

### RepoConfigs
This is an array of objects which define repo configurations. The values in these object are:

* `Repo` &mdash; the name of the repository which the `--repo` option will expect
* `DefinitionId` &mdash; the ID of the build definition to check on AzDO
* `AzdoInstance` &mdash; the AzDO instance where the build definition lives (e.g. `dnceng`)
* `ExpectedTime` &mdash; the repo's expected rollout time; used when calculating the score for "time to rollout"
* `ExcludeStages` &mdash; stages that shouldn't be counted as part of the build time (i.e. any stage which takes place after deployment has completed)

### AzdoInstanceConfigs
This is an array of objects which define AzDO instance configurations. The values in these object are:

* `Name` &mdash; the name of the AzDO instance which is expected by the `AzdoInstance` property of repo configs
* `Project` &mdash; the project this AzDO config should reference; note that each project in an instance will need its own definition
* `PatSecretName` &mdash; the name of a secret in a key vault which contains a build/code read PAT
* `KeyVaultUri` &mdash; the URI of the key vault which contains the above secret

### RolloutWeightConfig
This is an object which contains the weights used to calculate rollout scores.

* `RolloutMinutesPerPoint` &mdash; for a value of `n`, the rollout time score will be one point per `n` minutes over the repo's `ExpectedTime`
* `PointsPerIssue` &mdash; the number of points for each critical issue in a rollout
* `PointsPerHotfix` &mdash; the number of points per hotfix in a rollout
* `PointsPerRollback` &mdash; the number of points per rollback in a rollout
* `DowntimeMinutesPerPoint` &mdash; for a value of `n`, the rollout downtime score will be one point per `n` minutes
* `FailurePoints` &mdash; the number of points assigned in the case of failing to rollout