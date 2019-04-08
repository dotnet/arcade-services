param(
    [string]$maestroInstallation,
    [string]$darcVersion,
    [string]$maestroBearerToken,
    [string]$githubPAT,
    [string]$githubUser,
    [string]$azdoPAT
)

$subscriptionId = $null
$pullRequestBaseBranch = $null
$testChannelName = Get-Random
$sourceRepoName = "maestro-test1"
$targetRepoName = "maestro-test2"
$targetBranch = Get-Random
$sourceBuildNumber = Get-Random
$sourceCommit = Get-Random
$sourceBranch = "master"
$testReleasePipelineId = 45
$assets = @(
    @{
        name = "Foo"
        version = "1.1.0"
    },
    @{
        name = "Bar"
        version = "2.1.0"
    }
)

try {
    Write-Host
    Write-Host "Github Dependency flow with a release pipeline associated to a channel"
    Write-Host

    # Import common tooling and prep for tests
    . $PSScriptRoot/common.ps1

    Write-Host "Running tests..."
    Write-Host

    $sourceRepoUri = Get-Github-RepoUri $sourceRepoName
    $targetRepoUri = Get-Github-RepoUri $targetRepoName

    Write-Host "Creating test channel"
    try { Darc-Delete-Channel $testChannelName } catch {}
    Darc-Add-Channel $testChannelName "test"
    $channelsToDelete += $testChannelName

    Write-Host "Creating test pipeline"
    $pipelineId = Create-Pipeline $testReleasePipelineId
    $pipelinesToDelete += $pipelineId
    Write-Host "Created Pipeline $pipelineId"

    Write-Host "Adding test release pipeline mapping to test channel"
    Add-Pipeline-To-Channel $testChannelName $pipelineId
    Write-Host "Associated test release pipeline $pipelineId to test channel"
    $channelPipelinesToDelete[$testChannelName] += $pipelineId

    Write-Host "Creating default channel"
    try { Darc-Delete-Default-Channel $testChannelName $sourceRepoUri $sourceBranch } catch {}
    Darc-Add-Default-Channel $testChannelName $sourceRepoUri $sourceBranch
    $defaultChannelsToDelete += @{ channel = $testChannelName; repo = $sourceRepoUri; branch = $sourceBranch }

    Write-Host "Adding a subscription from $sourceRepoName to $targetRepoName"
    $subscriptionId = Darc-Add-Subscription --channel `'$testChannelName`' --source-repo $sourceRepoUri --target-repo $targetRepoUri --update-frequency everyBuild --target-branch $targetBranch
    $subscriptionsToDelete += $subscriptionId

    Write-Host "Cloning target repo to prepare the target branch"
    # Clone the target repo, branch, add the new dependencies and push the branch
    GitHub-Clone $targetRepoName
    Git-Command $targetRepoName checkout -b $targetBranch

    Write-Host "Adding dependencies to target repo"
    # Add the foo and bar dependencies
    try {
        Push-Location -Path $(Get-Repo-Location $targetRepoName)
        Darc-Command add-dependency --name 'Foo' --type product --repo $sourceRepoUri
        Darc-Command add-dependency --name 'Bar' --type product --repo $sourceRepoUri
    }
    finally {
        Pop-Location
    }

    Write-Host "Pushing branch to remote"
    # Commit and push
    Git-Command $targetRepoName commit -am `"Add dependencies.`"
    Git-Command $targetRepoName push origin HEAD
    $githubBranchesToDelete += @{ branch = $targetBranch; repo = $targetRepoName}

    Write-Host "Set up build for intake into target repository"
    $buildId = New-Build -repository $sourceRepoUri -branch $sourceBranch -commit $sourceCommit -buildNumber $sourceBuildNumber -assets $assets "true"
    Write-Host "Created build: $buildId"

    # Release Pipeline will run, and if it finishes, add the build to the channel. 
    # This will trigger the dependency update. If we don't see the PR created after 10 attempts
    # and the build is not in the channel, fail the test

    Write-Host "Waiting on Release Pipeline https://dnceng.visualstudio.com/internal/_release?definitionId=$testReleasePipelineId to complete, and a PR to be opened in $targetRepoUri"
    # Check that the PR was created properly. poll github 
    $tries = 10
    $success = $false
    while ($tries-- -gt 0) {
        Write-Host "Checking for PRs, ${tries} tries remaining"
        $pullRequest = Get-GitHub-PullRequests $targetRepoName $targetBranch
        if ($pullRequest) {
            # Find and verify PR info
            if ($pullRequest.Count -ne 1) {
                throw "Unexpected number of pull requests opened."
            }
            $pullRequest = $pullRequest[0]

            $pullRequestBaseBranch = $pullRequest.head.ref
            $githubBranchesToDelete += @{ branch = $pullRequestBaseBranch; repo = $targetRepoName}
            $gitHubPRsToClose += @{ number = $pullRequest.number; repo = $targetRepoName }

            $expectedPRTitle = "[$targetBranch] Update dependencies from $githubTestOrg/$sourceRepoName"
            if ($pullRequest.title -ne $expectedPRTitle) {
                throw "Expected PR title to be $expectedPRTitle, was $($pullRequest.title)"
            }
            
            # Check out the merge commit sha, then use darc to get and verify the
            # dependencies
            Git-Command $targetRepoName fetch
            Git-Command $targetRepoName checkout $pullRequestBaseBranch

            try {
                Push-Location -Path $(Get-Repo-Location $targetRepoName)
                $dependencies = Darc-Command get-dependencies
                $expectedDependencies =@(
                    "Name:    Foo"
                    "Version: 1.1.0",
                    "Repo:    $sourceRepoUri",
                    "Commit:  $sourceCommit",
                    "Type:    Product",
                    "",
                    "Name:    Bar",
                    "Version: 2.1.0",
                    "Repo:    $sourceRepoUri",
                    "Commit:  $sourceCommit",
                    "Type:    Product",
                    ""
                )

                if ($dependencies.Count -ne $expectedDependencies.Count) {
                    Write-Error "Expected $($expectedDependencies.Count) dependencies, Actual $($dependencies.Count) dependencies."
                    throw "PR did not have expected dependency updates."
                }
                for ($i = 0; $i -lt $expectedDependencies.Count; $i++) {
                    if ($dependencies[$i] -notmatch $expectedDependencies[$i]) {
                        Write-Error "Dependencies Line $i not matched`nExpected $($expectedDependencies[$i])`nActual $($dependencies[$i])"
                        throw "PR did not have expected dependency updates."
                    }
                }
            } finally {
                Pop-Location
            }

            $success = $true
            break
        }
        Start-Sleep 60
    }

    $buildInfo = Get-Build $buildId
    if ($buildInfo.id -ne $buildId) {
        throw "Failed to get build with id $buildId"
    }
    if ($buildInfo.channels.length -ne 1) {
        throw "Expected to see build in 1 channel, got $($buildInfo.channels.length)"
    }
    $success = ($success) -and ($buildInfo.channels[0].name -eq $testChannelName)

    if (-not $success) {
        throw "Expected build to be applied to $testChannelName"
    } else {
        Write-Host "Test passed"
    }
} finally {
    Teardown
}
