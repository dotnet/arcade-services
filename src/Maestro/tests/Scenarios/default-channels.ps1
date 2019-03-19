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
    try { Darc-Delete-Channel $testChannel1Name } catch {}
    try { Darc-Delete-Channel $testChannel2Name } catch {}
    Darc-Add-Channel $testChannel1Name "test"
    Darc-Add-Channel $testChannel2Name "test"
    $channelsToDelete += $testChannel1Name
    $channelsToDelete += $testChannel2Name

    # Adding default channels'
    Write-Host "Creating default channels"
    try { Darc-Delete-Default-Channel $testChannel1Name $repoUri $branchName } catch {}
    try { Darc-Delete-Default-Channel $testChannel2Name $repoUri $branchName } catch {}
    Darc-Add-Default-Channel $testChannel1Name $repoUri $branchName
    Darc-Add-Default-Channel $testChannel2Name $repoUri $branchName
    $defaultChannelsToDelete += @{ channel = $testChannel1Name; repo = $repoUri; branch = $branchName }
    $defaultChannelsToDelete += @{ channel = $testChannel2Name; repo = $repoUri; branch = $branchName }

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
    $success = ((($buildInfo.channels[0].name -eq $testChannel1Name) -or ($buildInfo.channels[1].name -eq $testChannel1Name)) `
        -and (($buildInfo.channels[0].name -eq $testChannel2Name -or $buildInfo.channels[1].name -eq $testChannel2Name)))
    
    if (-not $success) {
        throw "Expected build to be applied to $testChannel1Name and $testChannel2Name"
    } else {
        Write-Host "Test passed"
    }
} finally {
    Teardown
}
