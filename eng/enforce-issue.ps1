[CmdletBinding()]
param (
	[Parameter(Mandatory = $True)]
	$PullRequestNumber,
	[Parameter(Mandatory = $True)]
	$RepositoryName
)

$prDetail = Invoke-WebRequest `
		-UseBasicParsing `
		-Uri "https://api.github.com/repos/$RepositoryName/pulls/$PullRequestNumber" `
	| ConvertFrom-Json

if ($prDetail.draft) {
	Write-Host "Draft PR does not have to have GitHub issue specified. Check passed."
	exit 0
}
elseif ($prDetail.title -match "\[\w+\] Update dependencies from") {
	Write-Host "Dependency update PRs don't need release notes. Check passed."
	exit 0
}
elseif ($prDetail.title -match "\[automated\]") {
	Write-Host "Automated PRs don't need release notes. Check passed."
	exit 0
}
elseif ($prDetail.title -match "Bump") {
	Write-Host "Automated PRs don't need release notes. Check passed."
	exit 0
}


$issuePatterns = @{
    "GitHub Full Link"      = "github\.com/dotnet/(.+)/issues/(\d+)" # Eg: https://github.com/dotnet/arcade-services/issues/3625
    "AzDO DevOps Link"      = "dev\.azure\.com/(.+)/(.+)/_workitems" #Eg: https://dev.azure.com/dnceng/internal/_workitems/edit/45126
    "AzDO Visual Studio Link" = "(.+)\.visualstudio\.com/(.+)/_workitems"
    "GitHub Issue Shortcut" = "(?<!\w)([\w-]+/[\w-]+)?#\d+\b" # Eg: #5374 or dotnet/arcade-services#5832
}

$hasIssue = $false

foreach ($name in $issuePatterns.Keys) {
    if ($prDetail.body -match $issuePatterns[$name]) {
        Write-Host "Found issue link matching pattern: $name"
        $hasIssue = $true
        break
    }
}

if (-not $hasIssue) {
    Write-Host "##vso[task.LogIssue type=error;]Link to the corresponding GitHub/AzDO issue is missing in the PR description. Check failed."
    exit 1
}

exit 0
