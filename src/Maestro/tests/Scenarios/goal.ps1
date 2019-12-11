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
    Write-Host "Checking that the goal is added"
    $goal = Darc-Get-Goal -channel $newChannelName -definitionId $newDefinitionId
    if (-not $goal -match $newMinutes) {
        throw "Adding goal failed"
    } else {
        Write-Host "Add and Get goal was successful"
    }

    $newMinutes = Get-Random
    Write-Host "Updating goal" 
    Darc-Set-Goal -channel $newChannelName -definitionId $newDefinitionId -minutes $newMinutes

    # Update goal verification
    Write-Host "Checking that the goal is getting updated"
    $goal = Darc-Get-Goal -channel $newChannelName -definitionId $newDefinitionId
    if (-not $goal -match $newMinutes) {
        throw "Updating goal failed"
    } else {
        Write-Host "Update goal was successful"
    }
    Write-Host "Tests passed."
}finally{
    #Teardown only deletes the Channel that was created.
    Teardown
}
