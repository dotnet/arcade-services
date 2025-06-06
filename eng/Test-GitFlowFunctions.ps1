# Test script for the fixed forward and backflow functions
param(
    [Parameter(Mandatory=$false, HelpMessage="Path to the repository")]
    [string]$RepoPath = "D:\tmp\aspnetcore",
    [Parameter(Mandatory=$false, HelpMessage="Path to the VMR")]
    [string]$VmrPath = "D:\tmp\dotnet",
    [Parameter(Mandatory=$false, HelpMessage="Repository mapping in the VMR")]
    [string]$RepoMapping = "",
    [Parameter(Mandatory=$false, HelpMessage="Number of flows to find")]
    [int]$Depth = 10,
    [Parameter(Mandatory=$false, HelpMessage="Enable verbose output")]
    [switch]$VerboseScript
)

# Import our fixed GitFlowFunctions module
$moduleDir = Join-Path -Path $PSScriptRoot -ChildPath "modules"
$modulePath = Join-Path -Path $moduleDir -ChildPath "GitFlowFunctions.psm1"

# Check if module exists before importing
if (-not (Test-Path $modulePath)) {
    Write-Error "Required module not found: $modulePath"
    exit 1
}

# Import the module
Import-Module $modulePath -Force

# Create a parameter hashtable for verbose if needed
$verboseParam = @{}
if ($VerboseScript) {
    $verboseParam.Verbose = $true
    Write-Host "Verbose mode enabled" -ForegroundColor Yellow
}

# If RepoMapping is not provided, try to detect it from Version.Details.xml
if (-not $RepoMapping) {
    Write-Host "Detecting repository mapping from Version.Details.xml..." -ForegroundColor Yellow
    $RepoMapping = Get-SourceTagMappingFromVersionDetails -repoPath $RepoPath @verboseParam
    
    if (-not $RepoMapping -or $RepoMapping -eq "default") {
        # Use repo folder name as fallback
        $RepoMapping = Split-Path -Path $RepoPath -Leaf
        Write-Host "Using repository folder name as mapping: $RepoMapping" -ForegroundColor Yellow
    } else {
        Write-Host "Found repository mapping: $RepoMapping" -ForegroundColor Green
    }
}

# Find forward flows: source-manifest.json changes in VMR that reference the repo
Write-Host "Finding forward flows from VMR to repository ($RepoMapping)..." -ForegroundColor Cyan
try {
    $forwardFlows = Find-ForwardFlows -vmrPath $VmrPath -repoMapping $RepoMapping -depth $Depth @verboseParam

    Write-Host "Found $($forwardFlows.Count) forward flow connections" -ForegroundColor Green
    if ($forwardFlows.Count -gt 0) {
        Write-Host "Forward Flows (VMR -> Repo):" -ForegroundColor Green
        $forwardFlows | Format-Table VMRCommitSHA, RepoCommitSHA, BlamedCommitSHA, Depth -AutoSize
    }
}
catch {
    Write-Host "Error finding forward flows: $_" -ForegroundColor Red
}

# Find backflows: Version.Details.xml changes in repo that reference VMR commits
# Write-Host "Finding backflows from repository to VMR..." -ForegroundColor Cyan
# try {
#     $backFlows = Find-BackFlows -repoPath $RepoPath -depth $Depth @verboseParam

#     Write-Host "Found $($backFlows.Count) backflow connections" -ForegroundColor Green
#     if ($backFlows.Count -gt 0) {
#         Write-Host "Back Flows (Repo -> VMR):" -ForegroundColor Green
#         $backFlows | Format-Table RepoShortSHA, VMRShortSHA, BlamedShortSHA, Depth -AutoSize
#     }
# }
# catch {
#     Write-Host "Error finding backflows: $_" -ForegroundColor Red
# }

# Create a combined list of connections
$allFlows = @()
if ($forwardFlows) { $allFlows += $forwardFlows }
if ($backFlows) { $allFlows += $backFlows }

Write-Host "Total connections found: $($allFlows.Count)" -ForegroundColor Green
