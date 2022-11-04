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
if (-not $issueHasReleaseNotes) {
	Write-Host "##vso[task.LogIssue type=error;]Linked GitHub issue does not have release notes. Check failed."
	exit 1
}

Write-Host "PR links a GitHub issue. Check passed."