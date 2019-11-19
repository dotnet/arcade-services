param(
    [string]$maestroInstallation,
    [string]$darcVersion,
    [string]$maestroBearerToken,
    [string]$githubPAT,
    [string]$azdoPAT
)

$subscriptionId = $null
$testChannelName = Get-Random
$sourceRepoName = "maestro-test1"
$sourceBranch = "master"
$targetRepoName = "maestro-test2"
$targetBranch = Get-Random

$source1BuildNumber = Get-Random
$source1Commit = Get-Random
$source1Assets = @(
    @{
        name = "Foo"
        version = "1.1.0"
    },
    @{
        name = "Bar"
        version = "2.1.0"
    }
)

$source2BuildNumber = Get-Random
$source2Commit = Get-Random
$source2Assets = @(
    @{
        name = "Foo"
        version = "1.17.0"
    },
    @{
        name = "Bar"
        version = "2.17.0"
    }
)

$expectedDependencies1 =@(
        "Name:             Foo"
        "Version:          1\.1\.0",
        "Repo:             $sourceRepoUri",
        "Commit:           $sourceCommit",
        "Type:             Product",
        "Pinned:           False",
        "",
        "Name:             Bar",
        "Version:          2\.1\.0",
        "Repo:             $sourceRepoUri",
        "Commit:           $sourceCommit",
        "Type:             Product",
        "Pinned:           False",
        ""
    )

$expectedDependencies2 = @(
        "Name:             Foo"
        "Version:          1\.17\.0",
        "Repo:             $sourceRepoUri",
        "Commit:           $source2Commit",
        "Type:             Product",
        "Pinned:           False",
        "",
        "Name:             Bar",
        "Version:          2\.17\.0",
        "Repo:             $sourceRepoUri",
        "Commit:           $source2Commit",
        "Type:             Product",
        "Pinned:           False",
        ""
    )

try {
    Write-Host
    Write-Host "Azure DevOps Dependency Flow, non-batched"
    Write-Host

    # Import common tooling and prep for tests
    . $PSScriptRoot/common.ps1

    Write-Host "Running tests..."
    Write-Host

    $sourceRepoUri = Get-AzDO-RepoUri $sourceRepoName
    $targetRepoUri = Get-AzDO-RepoUri $targetRepoName

    Write-Host "Creating a test channel '$testChannelName'"
    Darc-Add-Channel -channelName $testChannelName -classification "test"

    Write-Host "Adding a subscription from $sourceRepoName to $targetRepoName"
    $subscriptionId = Darc-Add-Subscription --channel "$testChannelName" --source-repo "$sourceRepoUri" --target-repo "$targetRepoUri" --update-frequency none --target-branch "$targetBranch" 

    Write-Host "Set up build for intake into target repository"
    # Create a build for the source repo
    $buildId = New-Build -repository $sourceRepoUri -branch $sourceBranch -commit $source1Commit -buildNumber $source1BuildNumber -assets $source1Assets
    # Add the build to the target channel
    Add-Build-To-Channel $buildId $testChannelName

    Write-Host "Cloning target repo to prepare the target branch"
    # Clone the target repo, branch, add the new dependencies and push the branch
    AzDO-Clone $targetRepoName
    Git-Command $targetRepoName checkout -b $targetBranch

    Write-Host "Adding dependencies to target repo"
    # Add the foo and bar dependencies
    try {
        Push-Location -Path $(Get-Repo-Location $targetRepoName)
        Darc-Command add-dependency --name Foo --type product --repo "$sourceRepoUri"
        Darc-Command add-dependency --name Bar --type product --repo "$sourceRepoUri"
    }
    finally {
        Pop-Location
    }

    Write-Host "Pushing branch to remote"
    # Commit and push
    Git-Command $targetRepoName commit -am `"Add dependencies.`"
    Git-Command $targetRepoName push origin HEAD
    $global:azdoBranchesToDelete += @{ branch = $targetBranch; repo = $targetRepoName}

    Write-Host "Trigger the dependency update"
    # Trigger the subscription
    Trigger-Subscription $subscriptionId

    Write-Host "Checking Opened PR in $targetBranch $targetRepoName ..."
    $pullRequest = Check-AzDO-PullRequest-Created $targetRepoName $targetBranch
    $expectedPRTitle = "[$targetBranch] Update dependencies from $azdoAccount/$azdoProject/$sourceRepoName"
    Validate-AzDO-PullRequest-Contents $pullRequest $expectedPRTitle $targetRepoName $targetBranch $expectedDependencies1

    # Now, add a new build and check that the dependencies were updated in the same pull request.

    Write-Host "Set up another build for intake into target repository"
    # Create a build for the source repo
    $buildId = New-Build -repository $sourceRepoUri -branch $sourceBranch -commit $source2Commit -buildNumber $source2BuildNumber -assets $source2Assets
    # Add the build to the target channel
    Add-Build-To-Channel $buildId $testChannelName

    Write-Host "Trigger the dependency update"
    # Trigger the subscription
    Trigger-Subscription $subscriptionId

    $pullRequestBaseBranch = $pullRequest.sourceRefName.Replace('refs/heads/','')

    Write-Host "Waiting for PR to be updated in $targetRepoUri"
    Validate-PullRequest-Dependencies $targetRepoName $pullRequestBaseBranch $expectedDependencies2 10

    Write-Host "Remove the build from the channel and verify that the original dependencies are restored"
    # Then remove the second build from the channel, trigger the sub again, and it should revert back to the original
    # dependency set
    Remove-Build-From-Channel $buildId $testChannelName

    Write-Host "Trigger the dependency update"
    # Trigger the subscription
    Trigger-Subscription $subscriptionId

    Write-Host "Waiting for PR to be updated in $targetRepoUri"
    Validate-PullRequest-Dependencies $targetRepoName $pullRequestBaseBranch $expectedDependencies1 5

    Write-Host "Test passed"
} finally {
    Teardown
}
