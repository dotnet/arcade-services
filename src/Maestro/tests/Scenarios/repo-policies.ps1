param(
    [string]$maestroInstallation,
    [string]$darcVersion,
    [string]$maestroBearerToken,
    [string]$githubPAT,
    [string]$githubUser,
    [string]$azdoPAT
)

$repoName = "maestro-test1"
$branchName = Get-Random

try {
    Write-Host
    Write-Host "Repository merge policy handling"
    Write-Host

    # Import common tooling and prep for tests
    . $PSScriptRoot/common.ps1

    Write-Host "Running tests..."
    Write-Host

    $repoUri = Get-Github-RepoUri $repoName
    
    Write-Host "Setting repository merge policy to empty"
    Darc-Set-Repository-Policies -repo $repoUri -branch $branchName
    $output = Darc-Get-Repository-Policies $repoUri $branchName
    $expected = @(
        "$repoUri @ $branchName",
        "- Merge Policies: []"
        )
    if (-not $(Compare-Array-Output $expected $output)) {
        throw "Repository merge policies should have been cleared, but were not"
    }
    
    Write-Host ""
    Write-Host "Setting repository merge policy to standard"
    Darc-Set-Repository-Policies -repo $repoUri -branch $branchName -policiesParams @( "--standard-automerge" )
    $output = Darc-Get-Repository-Policies $repoUri $branchName
    $expected = @(
        "$repoUri @ $branchName",
        "- Merge Policies:",
        "  Standard"
        )
    if (-not $(Compare-Array-Output $expected $output)) {
        Write-Host $output
        throw "Repository merge policies should be standard, but were not"
    }
    
    Write-Host ""
    Write-Host "Setting repository merge policy to all checks successful"
    Darc-Set-Repository-Policies -repo $repoUri -branch $branchName -policiesParams @( "--all-checks-passed", "--ignore-checks", "A,B" )
    $output = Darc-Get-Repository-Policies -repo $repoUri -branch $branchName
    $expected = @(
        "$repoUri @ $branchName",
        "- Merge Policies:",
        "  AllChecksSuccessful",
        "    ignoreChecks = ",
        "                   [",
        "                     `"A`",",
        "                     `"B`"",
        "                   ]"
        )
    if (-not $(Compare-Array-Output $expected $output)) {
        Write-Host $output
        throw "Repository merge policies should be allcheckssuccessful, but were not"
    }
    
    Write-Host "Test Passed"

} finally {
    Teardown
}
