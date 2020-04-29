param(
    $AccessToken
)

$Commit = $env:Build.SourceVersion
$Version = $env:VersionPrefix

$uri ="$($env:System_TeamFoundationCollectionUri)/$($env:System_TeamProject)/_apis/git/repositories/$($env:Build_Repository_Name)/annotatedTags?api-version=5.0"
$tag = "v$Version"

if ($Commit -and $Version) {
    Write-Output "Tagging $Commit with $tag"
    $headers = New-Object 'System.Collections.Generic.Dictionary[[String],[String]]'
    $base64authinfo = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":${AccessToken}"))
    $headers = @{"Authorization"="Basic $base64authinfo"}

    $body = @{
        message = "Version $Version";
        name = "$tag";
        taggedObject = @{
            objectId = "$Commit";
        }
    } | ConvertTo-Json
    Invoke-WebRequest -Method Post $uri -Headers $headers -Body $body -ContentType 'application/json'
} else {
    exit -1
}