param(
    [string]$maestroInstallation,
    [string]$darcVersion,
    [string]$maestroBearerToken,
    [string]$githubPAT,
    [string]$azdoPAT,
    [string]$darcPackageSource
)

$testScripts = (
    'arcade-update.ps1',
    'azdoflow-batched.ps1',
    'azdoflow-nonbatched.ps1',
    # Disabled until https://github.com/dotnet/arcade/issues/3242 is fixed
    # 'azdoflow-nonbatched-all-checks-successful.ps1',
    'channels.ps1',
    'clone.ps1',
    'default-channels.ps1',
    'githubflow-batched.ps1',
    'githubflow-nonbatched.ps1',
    'githubflow-nonbatched-all-checks-successful.ps1',
    'githubflow-nonbatched-with-coherency.ps1',
    'githubflow-release-pipeline-nonbatched.ps1',
    'repo-policies.ps1'
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