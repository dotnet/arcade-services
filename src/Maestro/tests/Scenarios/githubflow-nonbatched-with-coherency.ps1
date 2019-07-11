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
$childSourceRepoName = "maestro-test1"
$parentSourceRepoName = "maestro-test2"
$targetRepoName = "maestro-test3"
$targetBranch = Get-Random
$parentSourceBuildNumber = Get-Random
# source commit is set to the HEAD commit of the "coherecncy-tree" branch
$parentSourceCommit = "cc1a27107a1f4c4bc5e2f796c5ef346f60abb404"
$parentSourceBranch = "coherency-tree"
$parentSourceAssets = @(
    @{
        name = "Foo"
        version = "1.1.0"
    },
    @{
        name = "Bar"
        version = "2.1.0"
    }
)
# source commit is set to the HEAD commit of the "coherecny-tree" branch
$childSourceBuildNumber = Get-Random
$childSourceCommit = "8460158878d4b7568f55d27960d4453877523ea6"
$childSourceBranch = "coherency-tree"
# Child needs to produce what the parent references, along with
# a separate output (referenced in target)
$childSourceAssets = @(
    @{
        name = "Baz"
        version = "1.3.0"
    },
    @{
        name = "Bop"
        version = "1.0"
    }
)

try {
    Write-Host
    Write-Host "GitHub Dependency Flow, non-batched with required coherency updates"
    Write-Host

    # Import common tooling and prep for tests
    . $PSScriptRoot/common.ps1

    Write-Host "Running tests..."
    Write-Host

    $childSourceRepoUri = Get-Github-RepoUri $childSourceRepoName
    $parentSourceRepoUri = Get-Github-RepoUri $parentSourceRepoName
    $targetRepoUri = Get-Github-RepoUri $targetRepoName

    Write-Host "Creating a test channel '$testChannelName'"
    try { Darc-Command "delete-channel" "--name" "$testChannelName" } catch {}
    Darc-Add-Channel -channelName $testChannelName -classification "test"

    Write-Host "Adding a subscription from $parentSourceRepoName to $targetRepoName"
    $subscriptionId = Darc-Add-Subscription "--channel" "$testChannelName" "--source-repo" "$parentSourceRepoUri" "--target-repo" "$targetRepoUri" "--update-frequency" "none" "--target-branch" "$targetBranch" 

    Write-Host "Set up new builds for intake into target repository"
    # Create a build for the parent source repo.
    $parentBuildId = New-Build -repository $parentSourceRepoUri -branch $parentSourceBranch -commit $parentSourceCommit -buildNumber $parentSourceBuildNumber -assets $parentSourceAssets
    # Add the build to the target channel
    Add-Build-To-Channel $parentBuildId $testChannelName

    # Create a build for the child source repo.
    $childBuildId = New-Build -repository $childSourceRepoUri -branch $childSourceBranch -commit $childSourceCommit -buildNumber $childSourceBuildNumber -assets $childSourceAssets
    # Add the build to the target channel
    Add-Build-To-Channel $childBuildId $testChannelName

    Write-Host "Cloning target repo to prepare the target branch"
    # Clone the target repo, branch, add the new dependencies and push the branch
    GitHub-Clone $targetRepoName
    Git-Command $targetRepoName checkout -b $targetBranch

    Write-Host "Adding dependencies to target repo. Two dependencies on parent and one on child tied to parent"
    # Add the foo and bar dependencies
    try {
        Push-Location -Path $(Get-Repo-Location $targetRepoName)
        Darc-Command "add-dependency" "--name" "Foo" "--type" "product" "--repo" "$parentSourceRepoUri" 
        Darc-Command "add-dependency" "--name" "Bar" "--type" "product" "--repo" "$parentSourceRepoUri" 
        Darc-Command "add-dependency" "--name" "Baz" "--type" "product" "--repo" "$childSourceRepoUri" "--coherent-parent" "Foo" 
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
    # Trigger the subscription
    Trigger-Subscription $subscriptionId

    $expectedDependencies =@(
        "Name:             Foo"
        "Version:          1.1.0",
        "Repo:             $parentSourceRepoUri",
        "Commit:           $parentSourceCommit",
        "Type:             Product",
        "Pinned:           False",
        "",
        "Name:             Bar",
        "Version:          2.1.0",
        "Repo:             $parentSourceRepoUri",
        "Commit:           $parentSourceCommit",
        "Type:             Product",
        "Pinned:           False",
        "",
        "Name:             Baz",
        "Version:          1.3.0",
        "Repo:             $childSourceRepoUri",
        "Commit:           $childSourceCommit",
        "Type:             Product",
        "Pinned:           False",
        "Coherent Parent:  Foo"
        ""
    )

    Write-Host "Waiting on PR to be opened in $targetRepoUri"

    $success = Check-NonBatched-Github-PullRequest $parentSourceRepoName $targetRepoName $targetBranch $expectedDependencies

    if (!$success) {
        throw "Pull request failed to open."
    } else {
        Write-Host "Test passed"
    }
} finally {
    Teardown
}
