param(
    [string]$maestroInstallation,
    [string]$darcVersion,
    [string]$maestroBearerToken,
    [string]$githubPAT,
    [string]$githubUser,
    [string]$azdoPAT
)

$githubTestOrg = "dotnet"

function Check-GitDir {
param([string]$gitDirPath)

    ("hooks", "info", "logs", "objects", "refs") | % {
        $folderPath = Join-Path $gitDirFolder $_
        if (-Not (Test-Path $folderPath -PathType Container)) {
            throw "'$gitDirPath' does not appear to be a valid .gitdir: missing folder '$_'"
        }
    }
    ("config", "description", "FETCH_HEAD", "HEAD", "index") | % {
        $filePath = Join-Path $gitDirFolder $_
        if (-Not (Test-Path $filePath -PathType Leaf)) {
            throw "'$gitDirPath' does not appear to be a valid .gitdir: missing file '$_'"
        }
    }
}

function Check-Expected {
param(
    [Tuple[string,string][]]$repos,
    [string[]]$masterRepos,
    [string[]]$gitDirs
)
    foreach ($rName in $repos.Keys) {
        $rPath = Join-Path $reposFolder $rName
        if (-Not (Test-Path $rPath -PathType Container)) {
            throw "Expected cloned repo '$rName' but not found at '$rPath'"
        }
        $versionDetailsPath = Join-Path (Join-Path $rPath "eng") "Version.Details.xml"
        if (Test-Path $versionDetailsPath -PathType Leaf) {
            $h = Get-FileHash -Algorithm SHA256 $versionDetailsPath
            if ($h.Hash -ine $repos[$rName]) {
                throw "Expected $versionDetailsPath to have hash '$($repos[$rName])', actual hash '$($h.Hash)'"
            }
        }
        else {
            if ($repos[$rName] -ne "") {
                throw "Expected a '$versionDetailsPath' but it is missing"
            }
        }
    }

    foreach ($r in $masterRepos) {
        $rPath = Join-Path $reposFolder $r
        if (-Not (Test-Path $rPath -PathType Container)) {
            throw "Expected cloned master repo '$r' but not found at '$rPath'"
        }
        $gitRedirectPath = Join-Path $rPath ".git"
        $expectedGitDir = Join-Path $gitDirFolder $r
        $expectedRedirect = "gitdir: $expectedGitDir.git"
        $actualRedirect = Get-Content $gitRedirectPath
        if ($actualRedirect -ine $expectedRedirect) {
            throw "Expected '$rPath' to have a .gitdir redirect of '$expectedRedirect', actual '$actualRedirect'"
        }
    }

    $actualRepos = Get-ChildItem $reposFolder -Directory | ? { $_.Name.Contains(".") }
    $actualMasterRepos = Get-ChildItem $reposFolder -Directory | ? { -Not $_.Name.Contains(".") }

    foreach ($r in $actualRepos) {
        $found = $false
        $repos | % {
            if ($_.Item1 -ieq $r) {
                $found = $true
            }
        }
        if (-Not $found) {
            throw "Found unexpected repo folder '$($r.FullName)'"
        }
    }

    foreach ($r in $actualMasterRepos) {
        $found = $false
        $masterRepos | % {
            if ($_ -ieq $r.Name) {
                $found = $true
            }
        }
        if (-Not $found) {
            throw "Found unexpected master repo folder '$($r.FullName)'"
        }
    }

    foreach ($gd in $gitDirs) {
        $gdPath = Join-Path $gitDirFolder $gd
        if (-Not (Test-Path $gdPath -PathType Container)) {
            throw "Expected a .gitdir for '$gd' but not found at '$gdPath'"
        }
    }

    $actualGitDirs = Get-ChildItem $gitDirFolder -Directory

    foreach ($gd in $actualGitDirs) {
        if (-Not $gitDirs.Contains($gd.Name)) {
            throw "Found unexpected .gitdir '$($gd.FullName)'"
        }
        Check-GitDir $gd.FullName
    }
}

try {
    Write-Host
    Write-Host "Uberclone"
    Write-Host

    # Import common tooling and prep for tests
    . $PSScriptRoot/common.ps1

    $sourceRepoName = "core-sdk"
    $sourceRepoVersion = "v3.0.100-preview4-011223"
    $reposFolder = Join-Path $testRoot "cloned-repos"
    $gitDirFolder = Join-Path $testRoot "git-dirs"
    # these repos are not currently clonable for us due to auth
    $reposToIgnore = "https://dev.azure.com/dnceng/internal/_git/dotnet-optimization;https://dev.azure.com/devdiv/DevDiv/_git/DotNet-Trusted;https://devdiv.visualstudio.com/DevDiv/_git/DotNet-Trusted"
    # these repos have file names that are too long on Windows for the temp folder
    $reposToIgnore += ";https://github.com/aspnet/AspNetCore;https://github.com/aspnet/AspNetCore-Tooling;https://github.com/dotnet/core-setup;https://github.com/dotnet/templating;https://github.com/dotnet/sdk;https://github.com/Microsoft/visualfsharp;https://github.com/dotnet/roslyn;https://github.com/NuGet/NuGet.Client;https://github.com/dotnet/corefx"

    Write-Host "parameters: sourceRepoName=$sourceRepoName, sourceRepoVersion=$sourceRepoVersion, reposFolder=$reposFolder, gitDirFolder=$gitDirFolder, reposToIgnore='$reposToIgnore'"

    Write-Host "Running tests..."
    Write-Host

    $sourceRepoUri = Get-Github-RepoUri $sourceRepoName

    Write-Host "Cloning repo $sourceRepoUri at $sourceRepoVersion with depth 2 and include-toolset=false"
    try { Darc-Command "clone" "--repo" "$sourceRepoUri" "--version" "$sourceRepoVersion" "--git-dir-folder" "$gitDirFolder" "--ignore-repos" "$reposToIgnore" "--repos-folder" "$reposFolder" "--depth" "2" } catch {}

    $expectedRepos = @(
        [Tuple]::Create("cli.204f425b6f061d0b8a01faf46f762ecf71436f68",                     "E9C194F021B0E91C9494B16FB6EAA52CC443E865BE5D33EFB6303232A0D80005"),
        [Tuple]::Create("cliCommandLineParser.0e89c2116ad28e404ba56c14d1c3f938caa25a01",    "B0FC3EAC54CFDAB2152FCFF78D922AE39F3F1BC8D0985DDCAFC4A2AB86725F93"),
        [Tuple]::Create("core-sdk.v3.0.100-preview4-011223",                                "6171BB338C8BF8F5C9CE2E84585F102599B407F632FDCFC5D5FD273920E4E0B4"),
        [Tuple]::Create("msbuild.d004974104fde202e633b3c97e0ece3287aa62f9",                 "CA5E91071DFF9F288C3FBA198AFD4594C020941602E0F4340B835FD40918FE45"),
        [Tuple]::Create("roslyn.01f3eb103049e2c93e0516c7d50908031deaca74",                  "44EE060FD57300CDB28EBC1C1098CB282162742DEA0E94D07E18130C93BA3B67"),
        [Tuple]::Create("sdk.814b7898f9908a88f62706331cf56f1ecc9745eb",                     "535F2D732EDBE41846985CB192C70220CE2871B55D21C8B2EEDC09D558F5BD12"),
        [Tuple]::Create("standard.8ef5ada20b5343b0cb9e7fd577341426dab76cd8",                "13DA5AFE54625E18EF6F5A8A4A4155F8237EE5FDD2AEC554D38819C74BFE5397"),
        [Tuple]::Create("toolset.3165b2579582b6b44aef768c01c3cc9ff4f0bc17",                 "105E3D6CDCDFAA8FD4C1FBFBE0CAA49308EDCD4E45BD982900C53CAE3E29D79C"),
        [Tuple]::Create("websdk.b55d4f4cf22bee7ec9a2ca5f49d54ebf6ee67e83",                  "68DF74EB53B1B1465FD48D0B16AF40DBD23E0E024BED967543C3B011EF0CA883"),
        [Tuple]::Create("winforms.b1ee29b8b8e14c1200adff02847391dde471d0d2",                "B081A500DAB904994907F9A213E05B7B0765F87A02607D138023548EE67AECF9"),
        [Tuple]::Create("wpf.d378b1ec6b8555c52b7da1c40ffc0784cb0f5cad",                     "C934F80C5FA37ECB54B555C76571BF0A7A49587F3DA5E8AD9A6E09A0A4740DB7")
    )

    $expectedMasterRepos = @(
        "cli",
        "cliCommandLineParser",
        "core-sdk",
        "msbuild"
        "standard",
        "toolset",
        "websdk",
        "winforms",
        "wpf"
    )

    $expectedGitDirs = @(
        "cli.git",
        "cliCommandLineParser.git",
        "core-sdk.git",
        "msbuild.git",
        "standard.git",
        "toolset.git",
        "websdk.git",
        "winforms.git",
        "wpf.git"
    )

    Check-Expected -repos $expectedRepos -masterRepos $expectedMasterRepos -gitDirs $expectedGitDirs

    # more repos with file names that are too long
    $reposToIgnore += ";https://github.com/dotnet/arcade"

    Write-Host "Cloning repo $sourceRepoUri at $sourceRepoVersion with depth 4 and include-toolset=true"
    try { Darc-Command "clone" "--repo" "$sourceRepoUri" "--version" "$sourceRepoVersion" "--git-dir-folder" "$gitDirFolder" "--ignore-repos" "$reposToIgnore" "--repos-folder" "$reposFolder" "--depth" "4" "--include-toolset"  } catch {}

    $expectedRepos = @(
        [Tuple]::Create("cli.204f425b6f061d0b8a01faf46f762ecf71436f68",                     "E9C194F021B0E91C9494B16FB6EAA52CC443E865BE5D33EFB6303232A0D80005"),
        [Tuple]::Create("cliCommandLineParser.0e89c2116ad28e404ba56c14d1c3f938caa25a01",    "B0FC3EAC54CFDAB2152FCFF78D922AE39F3F1BC8D0985DDCAFC4A2AB86725F93"),
        [Tuple]::Create("core-sdk.v3.0.100-preview4-011223",                                "6171BB338C8BF8F5C9CE2E84585F102599B407F632FDCFC5D5FD273920E4E0B4"),
        [Tuple]::Create("coreclr.9562c551f391635ce81869aabc84f894c2846be8",                 "2D0D4BE57A2EBAB8FADA391C1EA90ED71EC5CFD6441B3AF34B0A43FCC3CB0979"),
        [Tuple]::Create("coreclr.991817d90827b206ab25e74e7a5bd326a7e86ad4",                 "7101B4BEDC4783B7DD4F3449E12130FAF43CB65A49809ECE970CD8D2978A63C0"),
        [Tuple]::Create("coreclr.aea7846fc71591739e47c65c0632007bff1cc4a4",                 "C5B777C8948A439375FBB0C216919592BBB036CA4A9BFFEFB428739904172F69"),
        [Tuple]::Create("coreclr.d833cacabd67150fe3a2405845429a0ba1b72c12",                 "2D0D4BE57A2EBAB8FADA391C1EA90ED71EC5CFD6441B3AF34B0A43FCC3CB0979"),
        [Tuple]::Create("msbuild.d004974104fde202e633b3c97e0ece3287aa62f9",                 "CA5E91071DFF9F288C3FBA198AFD4594C020941602E0F4340B835FD40918FE45"),
        [Tuple]::Create("sdk.814b7898f9908a88f62706331cf56f1ecc9745eb",                     "535F2D732EDBE41846985CB192C70220CE2871B55D21C8B2EEDC09D558F5BD12"),
        [Tuple]::Create("standard.31a38c14c8a4d06ea59c67706fe4399c1f14368f",                "3A79469090374A4386754F5B331255775ADA567751585798ECB64B1B5687493B"),
        [Tuple]::Create("standard.8ef5ada20b5343b0cb9e7fd577341426dab76cd8",                "13DA5AFE54625E18EF6F5A8A4A4155F8237EE5FDD2AEC554D38819C74BFE5397"),
        [Tuple]::Create("standard.a652c72dd650656e8284eb8cfb95cb9965a2e75e",                "8A9BCA3A645635931CAB5DC2EEB246F7029BF1D4DDECFF29E5DD6E6E7B4F62A7"),
        [Tuple]::Create("toolset.3165b2579582b6b44aef768c01c3cc9ff4f0bc17",                 "105E3D6CDCDFAA8FD4C1FBFBE0CAA49308EDCD4E45BD982900C53CAE3E29D79C"),
        [Tuple]::Create("websdk.b55d4f4cf22bee7ec9a2ca5f49d54ebf6ee67e83",                  "68DF74EB53B1B1465FD48D0B16AF40DBD23E0E024BED967543C3B011EF0CA883"),
        [Tuple]::Create("winforms.b1ee29b8b8e14c1200adff02847391dde471d0d2",                "B081A500DAB904994907F9A213E05B7B0765F87A02607D138023548EE67AECF9"),
        [Tuple]::Create("wpf.d378b1ec6b8555c52b7da1c40ffc0784cb0f5cad",                     "C934F80C5FA37ECB54B555C76571BF0A7A49587F3DA5E8AD9A6E09A0A4740DB7")
    )

    $expectedMasterRepos = @(
        "cli",
        "cliCommandLineParser",
        "core-sdk",
        "coreclr",
        "msbuild",
        "standard",
        "toolset",
        "websdk",
        "winforms",
        "wpf"
    )

    $expectedGitDirs = @(
        "cli.git",
        "cliCommandLineParser.git",
        "core-sdk.git",
        "coreclr.git",
        "msbuild.git",
        "standard.git",
        "toolset.git",
        "websdk.git",
        "winforms.git",
        "wpf.git"
    )

    Check-Expected -repos $expectedRepos -masterRepos $expectedMasterRepos -gitDirs $expectedGitDirs

    Write-Host "Test passed"

} finally {
    Teardown
}
