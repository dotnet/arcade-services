param(
    [string]$maestroInstallation,
    [string]$darcVersion,
    [string]$maestroBearerToken,
    [string]$githubPAT,
    [string]$azdoPAT
)

$source1RepoName = "maestro-test1"
$source2RepoName = "maestro-test3"
$targetRepoName = "maestro-test2"
$testChannelName = Get-Random
$targetBranch = Get-Random
$targetCommit = Get-Random
$target1BuildNumber = Get-Random
$target2BuildNumber = Get-Random
$sourceBuildNumber = Get-Random
$sourceCommit = Get-Random
$sourceBranch = "master"
$source1Assets = @(
    @{
        name = "Foo"
        version = "1.1.0"
    },
    @{
        name = "Bar"
        version = "2.1.0"
    }
)
$source2Assets = @(
    @{
        name = "Pizza"
        version = "3.1.0"
    },
    @{
        name = "Hamburger"
        version = "4.1.0"
    }
)

$targetAssets = @(
    @{
        name = "Source1"
        version = "3.1.0"
    },
    @{
        name = "Source2"
        version = "4.1.0"
    }
)
try {
    Write-Host
    Write-Host "Github Dependency Flow, batched"
    Write-Host

    # Import common tooling and prep for tests
    . $PSScriptRoot/common.ps1

    Write-Host "Running tests..."
    Write-Host

    $source1RepoUri = Get-Github-RepoUri $source1RepoName
    $source2RepoUri = Get-Github-RepoUri $source2RepoName
    $targetRepoUri = Get-Github-RepoUri $targetRepoName

    Write-Host "Creating a test channel '$testChannelName'"
    Darc-Add-Channel -channelName $testChannelName -classification "test"

    Write-Host "Set up build1 for intake into target repository"
    # Create a build for the first source repo
    $build1Id = New-Build -repository $source1RepoUri -branch $sourceBranch -commit $sourceCommit -buildNumber $sourceBuildNumber -assets $source1Assets
    # Add the build to the target channel
    Add-Build-To-Channel $build1Id $testChannelName

    Write-Host "Set up build2 for intake into target repository"
    # Create a build for the second  source repo
    $build2Id = New-Build -repository $source2RepoUri -branch $sourceBranch -commit $sourceCommit -buildNumber $sourceBuildNumber -assets $source2Assets
    # Add the build to the target channel
    Add-Build-To-Channel $build2Id $testChannelName

    $dependencies = @(
        @{
            buildId = $build1Id
            isProduct = $true
        },
        @{
            buildId = $build2Id
            isProduct = $true
        }
    )

    # Add the target build once, should populate the BuildDependencies table and calculate TimeToInclusion
    $targetBuild = New-Build -repository $targetRepoUri -branch $targetBranch -commit $targetCommit -buildNumber $target1BuildNumber -assets $targetAssets -dependencies $dependencies
    Add-Build-To-Channel $targetBuild $testChannelName

    # Add the target build a second time, should populate the BuildDependencies table and use the previous TimeToInclusion
    $targetBuild = New-Build -repository $targetRepoUri -branch $targetBranch -commit $targetCommit -buildNumber $target2BuildNumber -assets $targetAssets -dependencies $dependencies
    Add-Build-To-Channel $targetBuild $testChannelName
    
} finally {
    Teardown
}
