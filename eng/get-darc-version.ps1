param(
    [Parameter(Mandatory=$True)]
    [string]$maestroTestEndpoints,
    [Parameter(Mandatory=$True)]
    [string]$apiVersion
)

$maestroTestEndpoint = $maestroTestEndpoints.Split(',')[0]
$versionEndpoint = "$maestroTestEndpoint/api/assets/darc-version?api-version=$apiVersion"
$latestDarcVersion = $(Invoke-WebRequest -Uri $versionEndpoint -UseBasicParsing).Content
Write-Host "##vso[task.setvariable variable=darcVersion]$latestDarcVersion"
Write-Host "Using Darc version $latestDarcVersion to run the tests"