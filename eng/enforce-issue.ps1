[CmdletBinding()]
param (
	[Parameter(Mandatory = $True)]
	$PullRequestNumber
)

$prDetail = Invoke-WebRequest `
	-UseBasicParsing `
	-Uri "https://api.github.com/repos/dotnet/arcade-services/pulls/$PullRequestNumber" `
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

$hasIssue = $prDetail.body -Match "github\.com/dotnet/(.+)/issues/(\d+)"
if (-not $hasIssue) {
	Write-Host "##vso[task.LogIssue type=error;]Link to the corresponding GitHub issue is missing in the PR description. Check failed."
	exit 1
}

try {
	$issueDetail = Invoke-WebRequest `
		-UseBasicParsing `
		-Uri "https://api.github.com/repos/dotnet/$($matches[1])/issues/$($matches[2])" `
	| ConvertFrom-Json
}
catch {
	Write-Host "##vso[task.LogIssue type=error;]Error fetching issue dotnet/$($matches[1])#$($matches[2]) from arcade. Does it exist?"
	exit 1
}

$issueHasReleaseNotes = $issueDetail.body -Match "### Release Note Description(?:\r?\n)+([^\s]+)"
$issueIsAzdoMirror = $issueDetail.title -like "AzDO Issue*"
if (-not $issueHasReleaseNotes -and -not $issueIsAzdoMirror) {
	Write-Host "##vso[task.LogIssue type=error;]Linked GitHub issue does not have release notes. Check failed."
	Write-Host "Ensure your issue has release notes. They should be in the following format:`n`n### Release Note Description`n<Stick your notes here>`n"
	Write-Host "For more information, see https://dev.azure.com/dnceng/internal/_wiki/wikis/DNCEng%20Services%20Wiki/983/ReleaseNotesGuidance?anchor=mechanics"
	exit 1
}
elseif ($issueIsAzdoMirror) {
	Write-Host "##vso[task.LogIssue type=warning;]Linked GitHub issue is a mirrored Azure DevOps workitem. Please ensure the workitem has release notes in it."
}

Write-Host "PR links a GitHub issue. Check passed."