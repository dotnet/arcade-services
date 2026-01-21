### Automates the rollout PR preparation process for arcade-services.
### This script:
###   - Pulls the latest changes from main
###   - Creates a rollout branch from a specified commit (or HEAD)
###   - Pushes the branch to origin
###   - Creates a rollout issue with the appropriate labels and project assignments
###   - Creates a PR from the rollout branch to production, referencing the issue
###
### Parameters:
###   -d, --date <string> Date for the rollout in YYYY-MM-DD format (default: today)
###
### Example: .\prepare-rollout.ps1
### Example: .\prepare-rollout.ps1 -d 2025-12-15

[CmdletBinding(PositionalBinding=$false)]
Param(
    [Alias('d')]
    [Parameter(Mandatory=$false)]
    [string]
    $Date
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$Repo = "dotnet/arcade-services"

# Use today's date if not specified
if (-not $Date) {
    $Date = Get-Date -Format 'yyyy-MM-dd'
}

# Validate date format
if ($Date -notmatch '^\d{4}-\d{2}-\d{2}$') {
    Write-Error "Date must be in YYYY-MM-DD format. Got: $Date"
    exit 1
}

$branchName = "rollout/$Date"
$issueTitle = "Rollout $Date"
$prTitle = "[Rollout] Production rollout $Date"

Write-Host "=== Rollout Preparation for $Date ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Determine the remote name based on the repository URI
Write-Host "Step 1: Determining remote name for repository $Repo..." -ForegroundColor Yellow
$remotes = git remote -v | Out-String
$remoteName = $null

# Check for common remote URL patterns for the repo
foreach ($line in $remotes -split "`n") {
    if ($line -match "^(\S+)\s+.*github\.com[:/]$Repo") {
        $remoteName = $matches[1]
        break
    }
}

if (-not $remoteName) {
    Write-Error "Could not find a remote for repository $Repo. Available remotes:`n$remotes"
    exit 1
}

Write-Host "Using remote: $remoteName" -ForegroundColor Green
Write-Host ""

# Step 2: Pull latest changes from main
Write-Host "Step 2: Pulling latest changes from main..." -ForegroundColor Yellow
git fetch $remoteName main

# Step 3: Determine the commit to rollout
Write-Host "Step 3: Using HEAD of main" -ForegroundColor Yellow
$commitSha = git rev-parse $remoteName/main

Write-Host "Commit SHA: $commitSha" -ForegroundColor Green
Write-Host ""

# Step 4: Create the rollout branch
Write-Host "Step 4: Creating branch '$branchName' from commit $commitSha..." -ForegroundColor Yellow
git checkout -b $branchName $commitSha

# Step 5: Determine fork remote (if exists) or use main remote
Write-Host "Step 5: Determining fork remote for pushing..." -ForegroundColor Yellow
$currentUser = gh api user --jq '.login'
$forkRemote = $null

# Check if there's a remote pointing to the user's fork
foreach ($line in $remotes -split "`n") {
    if ($line -match "^(\S+)\s+.*github\.com[:/]$currentUser/") {
        $forkRemote = $matches[1]
        break
    }
}

if ($forkRemote) {
    Write-Host "Found fork remote: $forkRemote" -ForegroundColor Green
    $pushRemote = $forkRemote
    $prHead = "$currentUser`:$branchName"
} else {
    Write-Host "No fork remote found, using $remoteName (will push directly)" -ForegroundColor Yellow
    $pushRemote = $remoteName
    $prHead = $branchName
}
Write-Host ""

# Step 6: Push the branch to the determined remote
Write-Host "Step 6: Pushing branch '$branchName' to $pushRemote..." -ForegroundColor Yellow
git push -u $pushRemote $branchName
Write-Host ""

# Step 7: Read the rollout issue template
Write-Host "Step 7: Reading rollout issue template..." -ForegroundColor Yellow
$templatePath = Join-Path $PSScriptRoot ".." ".github" "ISSUE_TEMPLATE" "rollout-issue.md"
$issueBody = Get-Content $templatePath -Raw

# Remove the frontmatter from the template
$issueBody = $issueBody -replace '(?s)^---.*?---\s*', ''
Write-Host ""

# Step 8: Create the rollout issue with Rollout label and assign to current user
Write-Host "Step 8: Creating rollout issue..." -ForegroundColor Yellow
$issueUrl = gh issue create --title "$issueTitle" --body "$issueBody" --label "Rollout" --assignee "$currentUser" --repo $Repo
Write-Host "Created issue: $issueUrl (assigned to $currentUser)" -ForegroundColor Green

# Get issue details
$issue = gh issue view $issueUrl --json id,number,url | ConvertFrom-Json
Write-Host ""

# Step 9: Assign issue to PCS project and FR area
Write-Host "Step 9: Assigning issue to PCS project and FR area..." -ForegroundColor Yellow

$pcsProjectId = 276
$areaName = 'First Responder / Ops / Debt'

function Set-ProjectProperty($projectId, $issue, $property, $value) {
    $project = gh project view --owner dotnet --format json $projectId | ConvertFrom-Json
    $projectItem = gh project item-add $projectId --owner dotnet --url $issue.url --format json | ConvertFrom-Json
    $field = gh project field-list $projectId --format json --owner dotnet --jq ".fields[] | select(.name == `"$property`")" | ConvertFrom-Json
    $option = $field.options | Where-Object { $_.name -eq $value }
    gh project item-edit --id $projectItem.id --project-id $project.id --field-id $field.id --single-select-option-id $option.id
}

try {
    Set-ProjectProperty $pcsProjectId $issue 'Area' $areaName
    Write-Host "Assigned to PCS project with FR area" -ForegroundColor Green
} catch {
    Write-Warning "Failed to assign to project: $_"
    Write-Warning "You may need to manually assign the issue to the PCS project and FR area"
}

# Note: Sprint assignment requires knowing the current sprint, which may vary
# Users should manually assign to the current sprint as mentioned in the checklist
Write-Host "Note: Please manually assign the issue to the current sprint" -ForegroundColor Yellow
Write-Host ""

# Step 10: Create the PR
Write-Host "Step 10: Creating PR from '$branchName' to 'production'..." -ForegroundColor Yellow

$prBody = @"
#$($issue.number)

## Checklist
- [ ] Verify this PR contains the expected changes
- [ ] Follow the rollout process in issue #$($issue.number)
- [ ] ⚠️ **DO NOT SQUASH** when merging - use merge commit
"@

$prUrl = gh pr create --base production --head $prHead --title "$prTitle" --body "$prBody" --repo $Repo
Write-Host "Created PR: $prUrl" -ForegroundColor Green

# Get PR number for auto-merge
$prNumber = gh pr view $prUrl --json number --jq '.number'

# Step 11: Enable auto-merge with merge commit strategy
Write-Host "Step 11: Enabling auto-merge (merge commit) on PR..." -ForegroundColor Yellow
try {
    gh pr merge $prNumber --auto --merge --repo $Repo
    Write-Host "Auto-merge enabled (merge commit strategy)" -ForegroundColor Green
} catch {
    Write-Warning "Failed to enable auto-merge: $_"
    Write-Warning "You may need to manually enable auto-merge on the PR"
}
Write-Host ""

# Step 12: Summary
Write-Host "=== Rollout Preparation Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Summary:" -ForegroundColor White
Write-Host "  Date:        $Date" -ForegroundColor White
Write-Host "  Branch:      $branchName" -ForegroundColor White
Write-Host "  Pushed to:   $pushRemote" -ForegroundColor White
Write-Host "  Commit:      $commitSha" -ForegroundColor White
Write-Host "  Issue:       $issueUrl" -ForegroundColor White
Write-Host "  PR:          $prUrl" -ForegroundColor White
Write-Host "  Auto-merge:  Enabled (merge commit)" -ForegroundColor White
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Assign the issue to the current sprint (if not already done)" -ForegroundColor White
Write-Host "  2. Review the PR to ensure it contains the expected changes" -ForegroundColor White
Write-Host "  3. Follow the rollout checklist in the issue" -ForegroundColor White
Write-Host ""

# Open the issue in the browser
Write-Host "Opening issue in browser..." -ForegroundColor Yellow
start $issue.url
