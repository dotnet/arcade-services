param(
    [string]$maestroInstallation,
    [string]$darcVersion,
    [string]$maestroBearerToken,
    [string]$githubPAT,
    [string]$githubUser,
    [string]$azdoPAT
)

$testChannel1Name = Get-Random
$testChannel2Name = Get-Random
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
    Write-Host "Default channel handling"
    Write-Host

    # Import common tooling and prep for tests
    . $PSScriptRoot/common.ps1

    Write-Host "Running tests..."
    Write-Host

    $repoUri = Get-Github-RepoUri $repoName

    Write-Host "Creating test channels"
    try { Darc-Delete-Channel -channelName $testChannel1Name } catch {}
    try { Darc-Delete-Channel -channelName "$testChannel2Name" } catch {}
    Darc-Add-Channel -channelName $testChannel1Name -classification "test"
    Darc-Add-Channel -channelName $testChannel2Name -classification "test"

    # Adding default channels'
    Write-Host "Creating default channels"
    try { Darc-Delete-Default-Channel -channelName $testChannel1Name -repoUri $repoUri -branch $branchName } catch {}
    try { Darc-Delete-Default-Channel -channelName $testChannel2Name -repoUri $repoUri -branch $branchNameWithRefsHeads } catch {}
    Darc-Add-Default-Channel -channelName $testChannel1Name -repoUri $repoUri -branch $branchName
    Darc-Add-Default-Channel -channelName $testChannel2Name -repoUri $repoUri -branch $branchNameWithRefsHeads

    # Retrieve the default channel and ensure that the refs/heads section was removed,
    # and ensure that when we query via the API, 'refs/heads' is appropriately stripped out.
    # The publishing infrastructure uses the REST api, while darc uses a query of all default channels and then
    # substring filters for a more flexible user experience.
    $defaultChannels = Darc-Get-Default-Channel-From-Api -repoUri $repoUri -branch $branchName
    if ($defaultChannels.Count -ne 2) {
        throw "Unexpected number of default channels"
    }
    if (($defaultChannels[0].channel.name -ne $testChannel1Name) -and 
        ($defaultChannels[1].channel.name -ne $testChannel1Name)) {
        throw "Expected to find default channel $testChannel1Name for $repoUri on $branchName"
    }
    if (($defaultChannels[0].channel.name -ne $testChannel2Name) -and 
        ($defaultChannels[1].channel.name -ne $testChannel2Name)) {
        throw "Expected to find default channel $testChannel2Name for $repoUri on $branchName"
    }
    
    $defaultChannelsWithRefsHeads = Darc-Get-Default-Channel-From-Api -repoUri $repoUri -branch $branchNameWithRefsHeads
    if ($defaultChannels.Count -ne 2) {
        throw "Unexpected number of default channels"
    }
    if (($defaultChannels[0].channel.name -ne $testChannel1Name) -and 
        ($defaultChannels[1].channel.name -ne $testChannel1Name)) {
        throw "Expected to find default channel $testChannel1Name for $repoUri on $branchName"
    }
    if (($defaultChannels[0].channel.name -ne $testChannel2Name) -and 
        ($defaultChannels[1].channel.name -ne $testChannel2Name)) {
        throw "Expected to find default channel $testChannel2Name for $repoUri on $branchName"
    }

    Write-Host "Set up build for intake into target repository"

    # Create a build for the source repo
    $buildId = New-Build -repository $repoUri -branch $branchName -commit $commit -buildNumber $buildNumber -assets $assets

    # Look up the build and ensure that the channels were added

    $buildInfo = Get-Build $buildId
    if ($buildInfo.id -ne $buildId) {
        throw "Failed to get build with id $buildId"
    }
    if ($buildInfo.channels.length -ne 2) {
        throw "Expected to see build in 2 channels, got ${$buildInfo.channels.length}"
    }
    if ($buildInfo.gitHubBranch -ne $branchName){
        throw "Expected to see build branch was $branchName but was ${$buildInfo.gitHubBranch}"
    }
    $success = ((($buildInfo.channels[0].name -eq $testChannel1Name) -or ($buildInfo.channels[1].name -eq $testChannel1Name)) `
        -and (($buildInfo.channels[0].name -eq $testChannel2Name -or $buildInfo.channels[1].name -eq $testChannel2Name)))

    if (-not $success) {
        throw "Expected build to be applied to $testChannel1Name and $testChannel2Name"
    }

    # Disable the default channel, then create a new build and ensure it doesn't get assigned to that channel

    Darc-Disable-Default-Channel -channelName $testChannel1Name -repoUri $repoUri -branch $branchName

    # Create a build for the source repo
    $buildId = New-Build -repository $repoUri -branch $branchNameWithRefsHeads -commit $commit -buildNumber $buildNumber -assets $assets

    # Look up the build and ensure that the channels were added

    $buildInfo = Get-Build $buildId
    if ($buildInfo.id -ne $buildId) {
        throw "Failed to get build with id $buildId"
    }
    if ($buildInfo.channels.length -ne 1) {
        throw "Expected to see build in 1 channels, got ${$buildInfo.channels.length}"
    }
    $success = ($buildInfo.channels[0].name -eq $testChannel2Name)

    if (-not $success) {
        throw "Expected build to be applied to $testChannel2Name but not $testChannel1Name"
    }

    # Invert the default channel enable/disable and then try one more time

    Darc-Enable-Default-Channel -channelName $testChannel1Name -repoUri $repoUri -branch $branchName
    Darc-Disable-Default-Channel -channelName $testChannel2Name -repoUri $repoUri -branch $branchName

    # Create a build for the source repo
    $buildId = New-Build -repository $repoUri -branch $branchName -commit $commit -buildNumber $buildNumber -assets $assets

    # Look up the build and ensure that the channels were added

    $buildInfo = Get-Build $buildId
    if ($buildInfo.id -ne $buildId) {
        throw "Failed to get build with id $buildId"
    }
    if ($buildInfo.channels.length -ne 1) {
        throw "Expected to see build in 1 channels, got ${$buildInfo.channels.length}"
    }
    $success = ($buildInfo.channels[0].name -eq $testChannel1Name)

    if (-not $success) {
        throw "Expected build to be applied to $testChannel1Name but not $testChannel2Name"
    }

    Write-Host "Test Passed"

} finally {
    Teardown
}
