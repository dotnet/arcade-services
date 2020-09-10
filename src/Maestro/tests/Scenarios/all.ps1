param(
    [string]$maestroInstallation,
    [string]$darcVersion,
    [string]$maestroBearerToken,
    [string]$githubPAT,
    [string]$azdoPAT,
    [string]$darcPackageSource
)

$testScripts = (
    'azdoflow-feedflow.ps1',
    'clone.ps1',
    'promote-build.ps1',
    'goal.ps1'
)

$failed = 0
$passed = 0
$stopwatch = [system.diagnostics.stopwatch]::StartNew()
foreach ($testScript in $testScripts) {
    try {
        Write-Host "Running $testScript"
        & $PSScriptRoot\$testScript -maestroInstallation $maestroInstallation -darcVersion $darcVersion -maestroBearerToken $maestroBearerToken -githubPAT $githubPAT -azdoPAT $azdoPAT
        $passed++
    }
    catch {
        $failed++
        Write-Host $_
        Write-Host "Failed ${testScript}"
    }
}

$stopwatch.Stop()
$totalSeconds = $stopwatch.Elapsed.TotalSeconds
Write-Host
Write-Host "Tests ran in $totalSeconds seconds"
Write-Host

Write-Host "$passed/$($failed+$passed) tests passed"

if ($failed -ne 0){
    throw "Test failures"
}
