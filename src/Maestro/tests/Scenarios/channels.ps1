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
    
    Write-Host "Running tests..."
    Write-Host

    # Test channel name
    $newChannelName = "Maestro Test Channel"
    
    Write-Host "Removing $newChannelName if it exists"
    try { Darc delete-channel --name `'$newChannelName`' } catch { }
    
    # Add a new channel
    Write-Host "Creating channel '$newChannelName'"
    Darc add-channel --name `'$newChannelName`' --classification 'test'

    # Get the channel and make sure it's there
    Write-Host "Checking that '$newChannelName' exists"
    $channels = Darc get-channels
    if (-not $channels.Contains($newChannelName)) {
        throw "Cannot find `'$newChannelName`' after creating it"
    }

    Write-Host "Removing '$newChannelName'"
    Darc delete-channel --name `'$newChannelName`'

    # Get the channel and make sure it's no longer there
    $channels = Darc get-channels
    if ($channels.Contains($newChannelName)) {
        throw "Cannot delete '$newChannelName' after creating it"
    }

    Write-Host "Channel tests passed."
} finally {
    Teardown
}