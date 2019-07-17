param(
    [string]$maestroInstallation,
    [string]$darcVersion,
    [string]$maestroBearerToken,
    [string]$githubPAT,
    [string]$azdoPAT
)

$subscriptionId = $null
$sourceRepoName = "maestro-test1"
$targetRepoName = "maestro-test2"
$testChannelName = Get-Random
$targetBranch = Get-Random
$sourceBuildNumber = Get-Random
$sourceCommit = Get-Random
$sourceBranch = "master"
$proxyFeed = "https://some-proxy.azurewebsites.net/container/some-container/sig/somesig/se/2020-02-02/darc-int-maestro-test1-bababababab-1/index.json"
$azdoFeed = "https://some_org.pkgs.visualstudio.com/_packaging/darc-int-maestro-test1-efabaababababe-1/nuget/v3/index.json"
$regularFeed = "https://dotnetfeed.blob.core.windows.net/maestro-test1/index.json"

$sourceAssets = @(
    @{
        name = "Foo"
        version = "1.1.0"
        locations = @(
            @{
                location = $proxyFeed
                type = "nugetFeed"
            }
        )
    },
    @{
        name = "Bar"
        version = "2.1.0"
        locations = @(
            @{
                location = $azdoFeed
                type = "nugetFeed"
            },
            @{
                location = $regularFeed
                type = "nugetFeed"
            }
        )
    }
)

try {
    Write-Host
    Write-Host "Azure DevOps Internal feed flow"
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
    $buildId = New-Build -repository $sourceRepoUri -branch $sourceBranch -commit $sourceCommit -buildNumber $sourceBuildNumber -assets $sourceAssets
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

    $expectedDependencies =@(
        "Name:             Foo"
        "Version:          1.1.0",
        "Repo:             $sourceRepoUri",
        "Commit:           $sourceCommit",
        "Type:             Product",
        "Pinned:           False",
        "",
        "Name:             Bar",
        "Version:          2.1.0",
        "Repo:             $sourceRepoUri",
        "Commit:           $sourceCommit",
        "Type:             Product",
        "Pinned:           False",
        ""
    )

    $expectedFeeds = @($proxyFeed, $azdoFeed)
    $notExpectedFeeds = @($regularFeed)

    Write-Host "Waiting on PR to be opened in $targetRepoUri"
    $success = Check-NonBatched-AzDO-PullRequest $sourceRepoName $targetRepoName $targetBranch $expectedDependencies $false $expectedFeeds $notExpectedFeeds

    if ($success) {
        Write-Host "Test passed"
        
    } else {
        throw "Pull request failed to open."
    }
} finally {
    Teardown
}
