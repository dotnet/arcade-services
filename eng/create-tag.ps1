params(
	$AccessToken
)

$Commit = $env:Build.SourceVersion
$Version = $env:VersionPrefix

$uri ="$($env:System_TeamFoundationCollectionUri)/$($env:System_TeamProject)/_apis/git/repositories/$($env:Build_Repository_Name)/annotatedTags?api-version=4.1-preview.1"
$tag = "v$Version"

if ($Commit -and $Version) {
	Write-Output "Tagging $Commit with $tag"
	$bearer = @{ "Authorization" = "Bearer $AccessToken" }
	@"
	{ "name": "$tag", "taggedObject": { "objectId": "$Commit" }, "message": "Version $Version" }
	"@ | Invoke-RestMethod -Method Post -UseBasicParsing $uri -ContentType "application/json" -Headers $bearer
} else {
	exit -1
}