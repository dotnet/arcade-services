param(
    [string]$maestroInstallation,
    [string]$darcVersion,
    [string]$maestroBearerToken,
    [string]$githubPAT,
    [string]$githubUser,
    [string]$azdoPAT
)

$subscriptionId = $null
$testChannelName = Get-Random
$sourceRepoName = "arcade"
$targetRepoName = "maestro-test2"
$targetBranch = Get-Random
$sourceBuildNumber = Get-Random
# Need a real branch and commit hash for Maestro
$sourceCommit = "0b36b99e29b1751403e23cfad0a7dff585818051"
$sourceBranch = "dependencyflow-tests"
$sourceAssets = @(
    @{
        name = "Microsoft.Dotnet.Arcade.Sdk"
        version = "2.1.0"
    }
)

try {
    Write-Host
    Write-Host "Arcade dependency flow"
    Write-Host

    # Import common tooling and prep for tests
    . $PSScriptRoot/common.ps1

    Write-Host "Running tests..."
    Write-Host

    $sourceRepoUri = Get-ArcadeRepoUri
    $targetRepoUri = Get-Github-RepoUri $targetRepoName

    Write-Host "Creating a test channel '$testChannelName'"
    try { Darc-Command delete-channel --name "$testChannelName"  } catch {}
    Darc-Add-Channel -channelName $testChannelName -classification "test"

    Write-Host "Adding a subscription from $sourceRepoName to $targetRepoName"
    $subscriptionId = Darc-Add-Subscription --channel "$testChannelName" --source-repo "$sourceRepoUri" --target-repo "$targetRepoUri" --update-frequency none --target-branch "$targetBranch" 

    Write-Host "Set up build for intake into target repository"
    # Create a build for the source repo
    $buildId = New-Build -repository $sourceRepoUri -branch $sourceBranch -commit $sourceCommit -buildNumber $sourceBuildNumber -assets $sourceAssets
    # Add the build to the target channel
    Add-Build-To-Channel $buildId $testChannelName

    Write-Host "Cloning target repo to prepare the target branch"
    # Clone the target repo, branch, add the new dependencies and push the branch
    GitHub-Clone $targetRepoName
    Git-Command $targetRepoName checkout -b $targetBranch

    Write-Host "Adding dependencies to target repo"
    # Add the Arcade SDK dependency
    try {
        Push-Location -Path $(Get-Repo-Location $targetRepoName)
        Darc-Command add-dependency --name Microsoft.Dotnet.Arcade.Sdk --type toolset --repo "$sourceRepoUri"
    }
    finally {
        Pop-Location
    }

    Write-Host "Pushing branch to remote"
    # Commit and push
    Git-Command $targetRepoName commit -am "Add dependencies."
    Git-Command $targetRepoName push origin HEAD
    $global:githubBranchesToDelete += @{ branch = $targetBranch; repo = $targetRepoName}

    Write-Host "Trigger the dependency update"
    # Trigger the subscription
    Trigger-Subscription $subscriptionId

    Write-Host "Waiting on PR to be opened in $targetRepoUri"

    $expectedDependencies =@(
        "Name:             Microsoft.Dotnet.Arcade.Sdk"
        "Version:          2.1.0",
        "Repo:             $sourceRepoUri",
        "Commit:           $sourceCommit",
        "Type:             Toolset",
        "Pinned:           False",
        ""
    )

    $pullRequest = Check-Github-PullRequest-Created $targetRepoName $targetBranch
    if (!$pullRequest) {
        throw "Pull request failed to open."
    }
    $expectedPRTitle = "[$targetBranch] Update dependencies from dotnet/arcade"
    Validate-Arcade-PullRequest-Contents $pullRequest $expectedPRTitle $targetRepoName $targetBranch $expectedDependencies
    Write-Host "Test passed"
} finally {
    Teardown
}
