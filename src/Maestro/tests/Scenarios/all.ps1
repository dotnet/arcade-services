param(
    [string]$maestroInstallation,
    [string]$darcVersion,
    [string]$maestroBearerToken,
    [string]$githubPAT,
    [string]$azdoPAT
)

$testScripts = (
    'channels.ps1',
    'default-channels.ps1',
    'githubflow-nonbatched.ps1',
    'githubflow-nonbatched-with-coherency.ps1',
    'azdoflow-nonbatched.ps1',
    'githubflow-release-pipeline-nonbatched.ps1'
)

$failed = 0
$passed = 0
$stopwatch = [system.diagnostics.stopwatch]::StartNew()
foreach ($testScript in $testScripts) {
    try {
        Write-Host "Running $testScript"
        Invoke-Expression "& $PSScriptRoot\$testScript -maestroInstallation $maestroInstallation -darcVersion $darcVersion -maestroBearerToken $maestroBearerToken -githubPAT $githubPAT -azdoPAT $azdoPAT"
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