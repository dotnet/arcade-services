param(
    [string]$maestroInstallation,
    [string]$darcVersion,
    [string]$maestroBearerToken,
    [string]$githubPAT,
    [string]$githubUser,
    [string]$azdoPAT
)

$testChannelName = Get-Random
$repoName = "maestro-test1"
$branchName = Get-Random
$branchNameWithRefsHeads = "refs/heads/$branchName"
$buildNumber = Get-Random
$commit = Get-Random
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
    Write-Host "Build promotion handling"
    Write-Host

    # Import common tooling and prep for tests
    . $PSScriptRoot/common.ps1

    Write-Host "Running tests..."
    Write-Host

    $repoUri = Get-Github-RepoUri $repoName

    Write-Host "Creating test channel"
    try { Darc-Delete-Channel -channelName $testChannelName } catch {}
    Darc-Add-Channel -channelName $testChannelName -classification "test"

    Write-Host "Creating test build"

    $buildId = New-Build -repository $repoUri -branch $branchName -commit $commit -buildNumber $buildNumber -assets $assets

    # Look up the build to ensure it were persisted

    $buildInfo = Get-Build $buildId
    if ($buildInfo.id -ne $buildId) {
        throw "Failed to get build with id $buildId"
    }
    if ($buildInfo.channels.length -ne 0) {
        throw "Expected to see build in 0 channels, got ${$buildInfo.channels.length}"
    }
    if ($buildInfo.gitHubBranch -ne $branchName){
        throw "Expected to see build branch was $branchName but was ${$buildInfo.gitHubBranch}"
    }

    # Promote the build and check that it was correctly assigned to the channel

    Write-Host "Promoting build to channel ${$testChannelName}"

    Add-Build-To-Channel -buildId $buildInfo.id -channelName $testChannelName

    $buildInfo = Get-Build $buildInfo.id

    if ($buildInfo.channels.length -ne 1) {
        throw "Expected to see build in 1 channels, got ${$buildInfo.channels.length}"
    }

    if ($buildInfo.channels[0].name -ne $testChannelName) {
        throw "Expected build to be applied to $testChannelName and it was applied to ${$buildInfo.channels[0].name}"
    }

    # Remove the build from the channel and test that it was correctly removed

    Remove-Build-From-Channel -buildId $buildInfo.id -channelName $testChannelName

    $buildInfo = Get-Build $buildInfo.id

    if ($buildInfo.channels.length -ne 0) {
        throw "Expected to see build in 0 channels, got ${$buildInfo.channels.length}"
    }

    Write-Host "Test Passed"

} finally {
    Teardown
}
