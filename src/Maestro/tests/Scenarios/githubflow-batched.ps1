param(
    [string]$maestroInstallation,
    [string]$darcVersion,
    [string]$maestroBearerToken,
    [string]$githubPAT,
    [string]$azdoPAT
)

$pullRequestBaseBranch = $null
$source1RepoName = "maestro-test1"
$source2RepoName = "maestro-test3"
$targetRepoName = "maestro-test2"
$testChannelName = Get-Random
$targetBranch = Get-Random
$sourceBuildNumber = Get-Random
$sourceCommit = Get-Random
$sourceBranch = "master"
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
$source2Assets = @(
    @{
        name = "Pizza"
        version = "3.1.0"
    },
    @{
        name = "Hamburger"
        version = "4.1.0"
    }
)
try {
    Write-Host
    Write-Host "Github Dependency Flow, batched"
    Write-Host

    # Import common tooling and prep for tests
    . $PSScriptRoot/common.ps1

    Write-Host "Running tests..."
    Write-Host

    $source1RepoUri = Get-Github-RepoUri $source1RepoName
    $source2RepoUri = Get-Github-RepoUri $source2RepoName
    $targetRepoUri = Get-Github-RepoUri $targetRepoName

    Write-Host "Creating a test channel '$testChannelName'"
    Darc-Add-Channel -channelName $testChannelName -classification "test"

    Write-Host "Adding a subscription from $source1RepoName to $targetRepoName"
    $subscription1Id = Darc-Add-Subscription @("--channel", "$testChannelName", "--source-repo", "$source1RepoUri", "--target-repo", "$targetRepoUri", "--update-frequency", "none", "--target-branch", "$targetBranch", "--batchable" )

    Write-Host "Adding a subscription from $source2RepoName to $targetRepoName"
    $subscription2Id = Darc-Add-Subscription @( "--channel", "$testChannelName", "--source-repo", "$source2RepoUri", "--target-repo", "$targetRepoUri", "--update-frequency", "none", "--target-branch", "$targetBranch", "--batchable" )

    Write-Host "Set up build1 for intake into target repository"
    # Create a build for the first source repo
    $build1Id = New-Build -repository $source1RepoUri -branch $sourceBranch -commit $sourceCommit -buildNumber $sourceBuildNumber -assets $source1Assets
    # Add the build to the target channel
    Add-Build-To-Channel $build1Id $testChannelName

    Write-Host "Set up build2 for intake into target repository"
    # Create a build for the second  source repo
    $build2Id = New-Build -repository $source2RepoUri -branch $sourceBranch -commit $sourceCommit -buildNumber $sourceBuildNumber -assets $source2Assets
    # Add the build to the target channel
    Add-Build-To-Channel $build2Id $testChannelName

    Write-Host "Cloning target repo to prepare the target branch"
    # Clone the target repo, branch, add the new dependencies and push the branch
    Github-Clone $targetRepoName
    Git-Command $targetRepoName checkout -b $targetBranch

    Write-Host "Adding dependencies to target repo"
    try {
        Push-Location -Path $(Get-Repo-Location $targetRepoName)
        Darc-Command @( "add-dependency", "--name", "Foo", "--type product", "--repo", "$source1RepoUri" )
        Darc-Command @( "add-dependency", "--name", "Bar", "--type product", "--repo", "$source1RepoUri" )
        Darc-Command @( "add-dependency", "--name", "Pizza", "--type product", "--repo", "$source2RepoUri" )
        Darc-Command @( "add-dependency", "--name", "Hamburger", "--type product", "--repo", "$source2RepoUri" )
    }
    finally {
        Pop-Location
    }

    Write-Host "Pushing branch to remote"
    # Commit and push
    Git-Command $targetRepoName commit -am `"Add dependencies.`"
    Git-Command $targetRepoName push origin HEAD
    $global:githubBranchesToDelete += @{ branch = $targetBranch; repo = $targetRepoName}

    Write-Host "Trigger the dependency update"
    # Trigger the subscriptions
    Trigger-Subscription $subscription1Id
    Trigger-Subscription $subscription2Id

    $expectedDependencies =@(
        "Name:             Foo"
        "Version:          1.1.0",
        "Repo:             $sourceRepo1Uri",
        "Commit:           $sourceCommit",
        "Type:             Product",
        "Pinned:           False",
        "",
        "Name:             Bar",
        "Version:          2.1.0",
        "Repo:             $sourceRepo1Uri",
        "Commit:           $sourceCommit",
        "Type:             Product",
        "Pinned:           False",
        "",
        "Name:             Pizza",
        "Version:          3.1.0",
        "Repo:             $sourceRepo2Uri",
        "Commit:           $sourceCommit",
        "Type:             Product",
        "Pinned:           False",
        "",
        "Name:             Hamburger",
        "Version:          4.1.0",
        "Repo:             $sourceRepo2Uri",
        "Commit:           $sourceCommit",
        "Type:             Product",
        "Pinned:           False",
        ""
    )

    Write-Host "Waiting on PR to be opened in $targetRepoUri"
    $success = Check-Batched-Github-PullRequest 2 $targetRepoName $targetBranch $expectedDependencies
    if (!$success) {
        throw "Pull request failed to open."
    } else {
        Write-Host "Test passed"
    }
} finally {
    Teardown
}
