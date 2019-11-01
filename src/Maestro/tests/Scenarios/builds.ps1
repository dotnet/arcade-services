param(
    [string]$maestroInstallation,
    [string]$darcVersion,
    [string]$maestroBearerToken,
    [string]$githubPAT,
    [string]$azdoPAT
)

$sourceRepoName = "maestro-test1"
$sourceBuildNumber = Get-Random
$sourceCommit = Get-Random
$sourceBranch = "master"
$sourceAssets = @(
    @{
        name = "Foo"
        version = "1.1.0"
        locations = @(
            @{
                location = "https://pkgs.dev.azure.com/dnceng/public/_packaging/NotARealFeed/nuget/v3/index.json"
                type = "NugetFeed"
            }
        )
    },
    @{
        name = "Bar"
        version = "2.1.0"
        locations = @(
            @{
                location = "https://pkgs.dev.azure.com/dnceng/public/_packaging/NotARealFeed/nuget/v3/index.json"
                type = "NugetFeed"
            }
        )
    }
)

try {
    Write-Host
    Write-Host "Darc/Maestro build handling tests"
    Write-Host

    # Import common tooling and prep for tests
    . $PSScriptRoot/common.ps1

    Write-Host "Running tests..."
    Write-Host

    $sourceRepoUri = Get-Github-RepoUri $sourceRepoName

    # Create a new build and check some of the metadata. Then mark as released and check again
    Write-Host "Set up build for intake into target repository"
    # Create a build for the source repo
    $buildId = New-Build -repository $sourceRepoUri -branch $sourceBranch -commit $sourceCommit -buildNumber $sourceBuildNumber -assets $sourceAssets

    $output = Darc-Get-Build $buildId
    if (-not $output -match "Released: +False") {
        throw "Build should be marked unreleased"
    }

    # Release the build
    $output = Darc-Update-Build -id $buildId -updateParams @( "--released" )
    if (-not $output -match "Released: +True") {
        throw "Build should be marked released"
    }

    # Gather a drop with released included
    $gatherWithReleasedDir = Join-Path -Path $testRoot -ChildPath "gather-with-released"
    $darcParams = @( "gather-drop", "--id", "$buildId", "--dry-run", "--output-dir", $gatherWithReleasedDir )
    $gatherDropOutput = Darc-Command -darcParams $darcParams
    if ((-not $gatherDropOutput -match "Gathering drop for build $sourceBuildNumber")) {
        throw "Build should download build $sourceBuildNumber"
    }
    if ((-not $gatherDropOutput -match "Downloading asset Bar@2.1.0") -or
        (-not $gatherDropOutput -match "Downloading asset Foo@1.1.0")) {
        throw "Build should download both Foo and Bar"
    }

    # Gather with release excluded
    $gatherWithNoReleasedDir = Join-Path -Path $testRoot -ChildPath "gather-no-released"
    $darcParams = @( "gather-drop", "--id", "$buildId", "--dry-run", "--skip-released", "--output-dir", $gatherWithNoReleasedDir )
    $gatherDropOutput = Darc-Command -darcParams $darcParams
    if ((-not $gatherDropOutput -match "Skipping download of released build $sourceBuildNumber of $sourceRepoUri @ $sourceCommit")) {
        throw "Build should not download build $sourceBuildNumber"
    }
    if (($gatherDropOutput -match "Downloading asset Bar@2.1.0") -or
        ($gatherDropOutput -match "Downloading asset Foo@1.1.0")) {
        throw "Build should not download either Foo and Bar"
    }
    Write-Host $gatherDropOutput

    # Unrelease the build
    # $output = Darc-Update-Build -id $buildId -updateParams @( "--not-released" )
    if (-not $output -match "Released: +False") {
        throw "Build should be marked unreleased"
    }

    # Gather with release excluded
    $gatherWithNoReleased2Dir = Join-Path -Path $testRoot -ChildPath "gather-no-released-2"
    $darcParams = @( "gather-drop", "--id", "$buildId", "--dry-run", "--skip-released", "--output-dir", $gatherWithNoReleased2Dir )
    $gatherDropOutput = Darc-Command -darcParams $darcParams
    if ((-not $gatherDropOutput -match "Gathering drop for build $sourceBuildNumber")) {
        throw "Build should download build $sourceBuildNumber"
    }
    if ((-not $gatherDropOutput -match "Downloading asset Bar@2.1.0") -or
        (-not $gatherDropOutput -match "Downloading asset Foo@1.1.0")) {
        throw "Build should download both Foo and Bar"
    }

    Write-Host "Tests passed."
} finally {
    Teardown
}