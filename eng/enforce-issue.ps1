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

# Look for https://github.com/dotnet/arcade-services/issues/3625
$hasIssue = $prDetail.body -Match "github\.com/dotnet/(.+)/issues/(\d+)"
if (-not $hasIssue) {
	# Or for https://dev.azure.com/dnceng/internal/_workitems/edit/45126
	$hasIssue = $prDetail.body -Match "dev\.azure\.com/(.+)/(.+)/_workitems"
	if (-not $hasIssue) {
		Write-Host "##vso[task.LogIssue type=error;]Link to the corresponding GitHub/AzDO issue is missing in the PR description. Check failed."
		exit 1
	}
}

exit 0
