param(
    [string]$maestroInstallation,
    [string]$darcVersion,
    [string]$maestroBearerToken,
    [string]$githubPAT,
    [string]$azdoPAT
)

try {
    Write-Host
    Write-Host "Darc/Maestro Channel tests"
    Write-Host

    # Import common tooling and prep for tests
    . $PSScriptRoot/common.ps1
    
    Write-Host "Channel management tests..."
    Write-Host

    # Test channel name
    $newChannelName = Get-Random
    
    # Add a new channel
    Write-Host "Creating channel '$newChannelName'"
    Darc-Command add-channel --name `'$newChannelName`' --classification 'test'

    # Get the channel and make sure it's there
    Write-Host "Checking that '$newChannelName' exists"
    $channels = Darc-Command get-channels
    if (-not $channels.Contains($newChannelName)) {
        throw "Cannot find `'$newChannelName`' after creating it."
    } else {
        Write-Host "Checking that '$newChannelName' exists"
    }

    Write-Host "Removing '$newChannelName'"
    Darc-Command delete-channel --name `'$newChannelName`'

    # Get the channel and make sure it's no longer there
    $channels = Darc-Command get-channels
    if ($channels.Contains($newChannelName)) {
        throw "Cannot delete '$newChannelName' after creating it"
    }

    Write-Host "Tests passed."
} finally {
    Teardown
}