param(
    [string]$maestroInstallation,
    [string]$darcVersion,
    [string]$maestroBearerToken,
    [string]$githubPAT,
    [string]$azdoPAT
)

$testScripts = (
    'channels.ps1',
    'githubflow-nonbatched.ps1',
    'azdoflow-nonbatched.ps1'
)

$failed = 0
$passed = 0
foreach ($testScript in $testScripts) {
    try {
        Write-Host "Running $testScript"
        Invoke-Expression "& $PSScriptRoot\channels.ps1 -maestroInstallation $maestroInstallation -darcVersion $darcVersion -maestroBearerToken $maestroBearerToken -githubPAT $githubPAT -azdoPAT $azdoPAT"
        $passed++
    }
    catch {
        $failed++
        Write-Host "Failed $testScript"
    }
}

Write-Host "$passed/$($failed+$passed) tests passed"

if ($failed -ne 0){
    throw "Test failures"
}