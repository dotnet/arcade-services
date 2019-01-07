Param(
  [Parameter(Mandatory=$true)]
  [string] $maestroEndpoint,
  [Parameter(Mandatory=$true)]
  [string] $barToken,
  [Parameter(Mandatory=$true)]
  [string] $targetChannel,
  [Parameter(Mandatory=$true)]
  [string] $assetManifestPath
)

function Get-Headers([string]$accept, [string]$barToken) {
    $headers = New-Object 'System.Collections.Generic.Dictionary[[String],[String]]'
    $headers.Add('Accept',$accept)
    $headers.Add('Authorization',"Bearer $barToken")
    return $headers
}

# Read the asset manifest xml and find the build id in the BAR.
# Because the repo uri might be mapped onto a github uri, we avoid using that parameter in the query
# and instead look up by build number and commit.
[xml]$assetManifest = Get-Content $assetManifestPath
$buildNumber = $assetManifest.Build.BuildId
$commit = $assetManifest.Build.Commit

if (!$buildNumber -or !$commit) {
    throw "Could not determine build number or commit from the asset manifest"
}

try {
    Write-Host "Looking up build by build number $buildNumber and commit $commit"

    $headers = Get-Headers 'text/plain' $barToken
    $getBuildEndpoint = "$maestroEndpoint/api/builds?commit=${commit}&buildNumber=${buildNumber}&api-version=2018-07-16"
    $builds = Invoke-WebRequest -Uri $getBuildEndpoint -Headers $headers | ConvertFrom-Json
    if ($builds.Count -ne 1) {
        Write-Error "More than one build matching the specified criteria was found."
        exit 1
    }
    $buildId = $builds[0].id
    
    Write-Host "Found build id $buildId"

    # Get the channel id
    $headers = Get-Headers 'text/plain' $barToken
    $getChannelsEndpoint = "$maestroEndpoint/api/channels?api-version=2018-07-16"
    $channels = Invoke-WebRequest -Uri $getChannelsEndpoint -Headers $headers | ConvertFrom-Json
    $channelId = $($channels | Where-Object -Property "name" -Value "${targetChannel}" -EQ | Select-Object -Property id).id
    if (!$channelId) {
        Write-Error "More than one build matching the specified criteria was found."
        exit 1
    }

    $postBuildIntoChannelApiEndpoint = "$maestroEndpoint/api/channels/$channelId/builds/${buildId}?api-version=2018-07-16"
    $headers = Get-Headers 'application/json' $barToken

    Write-Host "POSTing to $postBuildIntoChannelApiEndpoint..."
    Invoke-WebRequest -Uri $postBuildIntoChannelApiEndpoint -Headers $headers -Method Post
    Write-Host "Build '$buildId' was successfully added to channel '$channelId'"
}
catch {
    Write-Host $_
    Write-Host $_.ScriptStackTrace
}