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
$sourceAssets = @(
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
    Write-Host "GitHub Dependency Flow, non-batched"
    Write-Host

    # Import common tooling and prep for tests
    . $PSScriptRoot/common.ps1

    Write-Host "Running tests..."
    Write-Host

    $sourceRepoUri = Get-Github-RepoUri $sourceRepoName
    $targetRepoUri = Get-Github-RepoUri $targetRepoName
    
    Write-Host "Creating a test channel '$testChannelName'"
    try { Darc-Command delete-channel --name `'$testChannelName`' } catch {}
    Darc-Add-Channel $testChannelName "test"
    $channelsToDelete += $testChannelName

    Write-Host "Adding a subscription from $sourceRepoName to $targetRepoName"
    $subscriptionId = Darc-Add-Subscription --channel `'$testChannelName`' --source-repo $sourceRepoUri --target-repo $targetRepoUri --update-frequency none --target-branch $targetBranch
    $subscriptionsToDelete += $subscriptionId

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
    # Add the foo and bar dependencies
    try {
        Push-Location -Path $(Get-Repo-Location $targetRepoName)
        Darc-Command add --name 'Foo' --type product --repo $sourceRepoUri
        Darc-Command add --name 'Bar' --type product --repo $sourceRepoUri
    }
    finally {
        Pop-Location
    }

    Write-Host "Pushing branch to remote"
    # Commit and push
    Git-Command $targetRepoName commit -am `"Add dependencies.`"
    Git-Command $targetRepoName push origin HEAD
    $githubBranchesToDelete += @{ branch = $targetBranch; repo = $targetRepoName}

    Write-Host "Trigger the dependency update"
    # Trigger the subscription
    Trigger-Subscription $subscriptionId

    Write-Host "Waiting on PR to be opened in $targetRepoUri"
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
            $pullRequestBaseBranch = $pullRequest.head.ref
            $githubBranchesToDelete += @{ branch = $pullRequestBaseBranch; repo = $targetRepoName}
            $gitHubPRsToClose += @{ number = $pullRequest.number; repo = $targetRepoName }

            $expectedPRTitle = "Update dependencies from ${githubTestOrg}/${sourceRepoName}"
            if ($pullRequest.title -ne $expectedPRTitle) {
                throw "Expected PR title to be $expectedPRTitle, was ${pullrequest.title}"
            }
            
            # Check out the merge commit sha, then use darc to get and verify the
            # dependencies
            Git-Command $targetRepoName fetch
            Git-Command $targetRepoName checkout $pullRequestBaseBranch

            try {
                Push-Location -Path $(Get-Repo-Location $targetRepoName)
                $dependencies = Darc-Command get-dependencies
                $dependencies = $dependencies -join "`r`n"
                $expectedDependencies =
@"
Name:    Foo
Version: 1.1.0
Repo:    $sourceRepoUri
Commit:  $sourceCommit

Name:    Bar
Version: 2.1.0
Repo:    $sourceRepoUri
Commit:  $sourceCommit
"@
                if (-not ($dependencies -match $expectedDependencies)) {
                    Write-Error "Expected $expectedDependencies, got $dependencies"
                    throw "PR did not have expected dependency updates."
                }
            } finally {
                Pop-Location
            }

            $success = $true
            break
        }
        Start-Sleep 60
    }

    if (!$success) {
        throw "Pull request failed to open."
    } else {
        Write-Host "Test passed"
    }
} finally {
    Teardown
}