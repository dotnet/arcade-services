param(
    [string]$maestroInstallation,
    [string]$darcVersion,
    [string]$maestroBearerToken,
    [string]$githubPAT,
    [string]$azdoPAT
)

try {
    Write-Host
    Write-Host "Darc/Maestro Goal tests"
    Write-Host

    # Import common tooling and prep for tests
    . $PSScriptRoot/common.ps1

    # Test channel name
    $newChannelName = Get-Random
    $newDefinitionId = Get-Random
    $newMinutes = Get-Random

    # Add a new channel
    Write-Host "Creating channel '$newChannelName'"
    Darc-Add-Channel -channelName $newChannelName -classification 'test'

    # Add a new Goal for a Definition in a Channel 
    Write-Host "Creating goal" 
    Darc-Set-Goal -channel $newChannelName -definitionId $newDefinitionId -minutes $newMinutes

    # Get the goal and make sure it's there
    Write-Host "Checking that goal $newMinutes minutes for '$newDefinitionId' on channel '$newChannelName' exists"
    $goal = Darc-Get-Goal -channel $newChannelName -definitionId $newDefinitionId
    if (-not $goal -match $newMinutes) {
        throw "Cannot find goal $newMinutes for definition '$newDefinitionId' on channel '$newChannelName'"
    } else {
        Write-Host "Add and Get goal was successful"
    }

    $newMinutes = Get-Random
    Write-Host "Updating goal" 
    Darc-Set-Goal -channel $newChannelName -definitionId $newDefinitionId -minutes $newMinutes

    # Update goal verification
    Write-Host "Checking if the goal is updated to $newMinutes minutes for '$newDefinitionId' on channel '$newChannelName'"
    $goal = Darc-Get-Goal -channel $newChannelName -definitionId $newDefinitionId
    if (-not $goal -match $newMinutes) {
        throw "Cannot update goal $newMinutes for definition '$newDefinitionId' on channel '$newChannelName'"
    } else {
        Write-Host "Update goal was successful"
    }
    Write-Host "Tests passed."
} finally {
    Teardown
}
