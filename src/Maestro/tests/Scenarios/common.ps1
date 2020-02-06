# Globals
[string]$maestroInstallation = if (-not $maestroInstallation) { throw "Please supply the Maestro installation with -maestroInstallation"} else { $maestroInstallation }
[string]$maestroBearerToken = if (-not $maestroBearerToken) { throw "Please supply the Maestro bearer token with -maestroBearerToken"} else { $maestroBearerToken }
[string]$githubPAT = if (-not $githubPAT) { throw "Please supply the github PAT with -githubPAT"} else { $githubPAT }
[string]$githubUser = if (-not $githubUser) { "dotnet-maestro-bot" } else { $githubUser }
[string]$githubTestOrg = if (-not $githubTestOrg) { "maestro-auth-test" } else { $githubTestOrg }
[string]$azdoPAT = if (-not $azdoPAT) { throw "Please supply the azdo PAT with -azdoPAT"} else { $azdoPAT }
[string]$azdoUser = if (-not $azdoUser) { "dotnet-maestro-bot" } else { $azdoUser }
[string]$azdoAccount = if (-not $azdoAccount) { "dnceng" } else { $azdoAccount }
[string]$azdoProject = if (-not $azdoProject) { "internal" } else { $azdoProject }
[string]$azdoApiVersion = if (-not $azdoApiVersion) { "5.0-preview.1" } else { $azdoApiVersion }
[string]$darcPackageSource = if (-not $darcPackageSource) {""} else { $darcPackageSource }
[string]$barApiVersion = "2019-01-16"
$global:gitHubPRsToClose = @()
$global:githubBranchesToDelete = @()
$global:azdoPRsToClose = @()
$global:azdoBranchesToDelete = @()
$global:subscriptionsToDelete = @()
$global:channelsToDelete = @()
$global:defaultChannelsToDelete = @()
$global:pipelinesToDelete = @()
$global:channelPipelinesToDelete = @{}

# Get a temporary directory for a test root. Use the agent work folder if running under azdo, use the temp path if not.
$testRootBase = if ($env:AGENT_WORKFOLDER) { $env:AGENT_WORKFOLDER } else { $([System.IO.Path]::GetTempPath()) }
$testRoot = Join-Path -Path $testRootBase -ChildPath $([System.IO.Path]::GetRandomFileName())
New-Item -Path $testRoot -ItemType Directory | Out-Null

$darcTool = ""
$darcCommandPrefix = ""
if (Test-Path $darcVersion) {
    # Set the tool to 'dotnet', and the command prefix to the darc binary
    $darcTool = "dotnet"
    $darcCommandPrefix = $darcVersion
    Write-Host "Using local darc binary $darcCommandPrefix"
} else {
    Write-Host "Temporary testing location located at $testRoot"
    $darcInstallArguments = @( "--tool-path", $testRoot, "--version", $darcVersion, "Microsoft.DotNet.Darc" )
    if ($darcPackageSource) {
        $darcInstallArguments += @( "--add-source", "${darcPackageSource}" )
    }
    Write-Host "Installing Darc: dotnet tool install $darcInstallArguments"
    & dotnet tool install @darcInstallArguments
    $darcTool = Join-Path -Path $testRoot -ChildPath "darc"
}
Write-Host

# Set auth parameters
$darcAuthParams = @("--bar-uri", $maestroInstallation, "--github-pat", $githubPAT, "--azdev-pat", $azdoPAT, "--password", $maestroBearerToken)

# Enable TLS 1.2
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Teardown() {

    Write-Host

    Write-Host "Cleaning $($global:subscriptionsToDelete.Count) subscriptions"
    foreach ($subscriptionId in $global:subscriptionsToDelete) {
        try {
            Write-Host "Deleting $subscriptionId"
            Darc-Delete-Subscription "$subscriptionId"
        } catch {
            Write-Warning "Failed to delete subscription with id $subscriptionId"
            Write-Warning $_
        }
    }

    Write-Host "Cleaning $($global:defaultChannelsToDelete.Count) default channels"
    foreach ($defaultChannel in $global:defaultChannelsToDelete) {
        try {
            Write-Host "Deleting default channel $($defaultChannel.repo)@$($defaultChannel.branch) -> $($defaultChannel.channel)"
            Darc-Delete-Default-Channel -channelName $defaultChannel.channel -repoUri $defaultChannel.repo -branch $defaultChannel.branch
        } catch {
            Write-Warning "Failed to delete default channel $($defaultChannel.repo)@$($defaultChannel.branch) -> $($defaultChannel.channel)"
            Write-Warning $_
        }
    }

    Write-Host "Cleaning $($global:channelPipelinesToDelete.Count) channel-pipeline mappings"
    foreach ($channelId in $global:channelPipelinesToDelete.Keys) {
        $pipelineIds = $global:channelPipelinesToDelete[$channelId]
        foreach ($pipelineId in $pipelineIds) {
            try {
                Write-Host "Removing pipeline: $pipelineId from channel: $channelId"
                Remove-Pipeline-From-Channel $channelId $pipelineId
            } catch {
                Write-Warning "Failed to remove pipeline $pipelineId from channel $channelId"
                Write-Warning $_
            }
        }
    }

    Write-Host "Cleaning $($global:channelsToDelete.Count) channels"
    foreach ($channel in $global:channelsToDelete) {
        try {
            Write-Host "Deleting channel $channel"
            Darc-Command delete-channel --name "$channel"
        } catch {
            Write-Warning "Failed to delete channel $channel"
            Write-Warning $_
        }
    }

    Write-Host "Cleaning $($global:pipelinesToDelete.Count) pipelines"
    foreach ($pipeline in $global:pipelinesToDelete) {
        try {
            Write-Host "Deleting pipeline $pipeline"
            Delete-Pipeline $pipeline
        } catch {
            Write-Warning "Failed to delete pipeline $pipeline"
            Write-Warning $_
        }
    }

    Write-Host "Cleaning $($global:githubBranchesToDelete.Count) github branches"
    foreach ($branch in $global:githubBranchesToDelete) {
        try {
            Write-Host "Removing $($branch.branch) from $($branch.repo)"
            GitHub-Delete-Branch $branch.repo $branch.branch
        } catch {
            Write-Warning "Failed to remove github branch $($branch.branch) in $($branch.repo)"
            Write-Warning $_
        }
    }

    Write-Host "Cleaning $($global:gitHubPRsToClose.Count) github PRs"
    foreach ($pr in $global:gitHubPRsToClose) {
        try {
            Write-Host "Closing pull request $($pr.number) in $($pr.repo)"
            Close-GitHub-PullRequest $pr.repo $pr.number
        } catch {
            Write-Warning "Failed to close github pull request $($pr.number) in $($pr.repo)"
            Write-Warning $_
        }
    }

    Write-Host "Cleaning $($global:azdoBranchesToDelete.Count) azdo branches"
    foreach ($branch in $global:azdoBranchesToDelete) {
        try {
            Write-Host "Removing $($branch.branch) from $($branch.repo)"
            AzDO-Delete-Branch $branch.repo $branch.branch
        } catch {
            Write-Warning "Failed to remove azdo branch $($branch.branch) $($branch.repo)"
            Write-Warning $_
        }
    }

    Write-Host "Cleaning $($global:azdoPRsToClose.Count) azdo PRs"
    foreach ($pr in $global:azdoPRsToClose) {
        try {
            Write-Host "Closing pull request $($pr.number) in $($pr.repo)"
            Close-AzDO-PullRequest $pr.repo $pr.number
        } catch {
            Write-Warning "Failed to close azdo pull request $($pr.number) in $($pr.repo)"
            Write-Warning $_
        }
    }

    Write-Host "Cleaning up $testRoot"
    Remove-Item -Path $testRoot -Recurse -Force | Out-Null
}

function Darc-Command-WithPipeline([Parameter(ValueFromPipeline=$true)]$pipelineParams, [Parameter(ValueFromRemainingArguments=$true)]$darcParams) {
    $finalParams = $darcParams
    if ($darcCommandPrefix) {
        $finalParams = ,"$darcCommandPrefix" + $finalParams
    }
    Write-Host "Running 'pipelineParams | $darcTool $finalParams ***'"
    $commandOutput = $pipelineParams| & $darcTool @finalParams @darcAuthParams
    if ($LASTEXITCODE -ne 0) {
      Write-Host ${commandOutput}
      throw "Darc command exited with exit code: $LASTEXITCODE`n${commandOutput}"
    } else {
      $commandOutput
    }
}

function Darc-Command([Parameter(ValueFromRemainingArguments=$true)]$darcParams) {
    $finalParams = $darcParams
    if ($darcCommandPrefix) {
        $finalParams = ,"$darcCommandPrefix" + $finalParams
    }
    Write-Host "Running '$darcTool $finalParams ***'"
    $commandOutput = & $darcTool @finalParams @darcAuthParams
    if ($LASTEXITCODE -ne 0) {
      Write-Host ${commandOutput}
      throw "Darc command exited with exit code: $LASTEXITCODE`n${commandOutput}"
    } else {
      $commandOutput
    }
}

# Run darc set-repository-policies
function Darc-Set-Repository-Policies($repo, $branch, $policiesParams) {
    $darcParams = @( "set-repository-policies", "-q", "--repo", "$repo", "--branch", "$branch" ) + $policiesParams
    Darc-Command -darcParams $darcParams
}

# Run darc get-repository-policies
function Darc-Get-Repository-Policies($repo, $branch) {
    $darcParams = @( "get-repository-policies", "--all", "--repo", "$repo", "--branch", "$branch" )
    Darc-Command -darcParams $darcParams
}

# Run darc add-channel and record the channel for later deletion
function Darc-Add-Channel($channelName, $classification) {
    $darcParams = @("add-channel", "--name", "$channelName", "--classification", "$classification" )
    Darc-Command -darcParams $darcParams
    $global:channelsToDelete += $channelName
}

function Darc-Delete-Channel($channelName) {
    $darcParams = @( "delete-channel", "--name", "$channelName" )
    Darc-Command -darcParams $darcParams
}

# Run darc add-channel and record the channel for later deletion
function Darc-Add-Default-Channel($channelName, $repoUri, $branch) {
    $darcParams = @( "add-default-channel", "--channel", "$channelName", "--repo", "$repoUri", "--branch", "$branch", "--quiet" )
    Darc-Command -darcParams $darcParams
    # We sometimes call add-default-channel with a refs/heads/ prefix, which
    # will get stripped away by the DB.
    $global:defaultChannelsToDelete += @{ channel = $channelName; repo = $repoUri; branch = $branch.ToString().Replace("refs/heads/", "") }
}

function Darc-Delete-Default-Channel($channelName, $repoUri, $branch) {
    $darcParams = @( "delete-default-channel", "--channel", "$channelName", "--repo", "$repoUri", "--branch", "$branch" )
    Darc-Command -darcParams $darcParams
}

function Darc-Enable-Default-Channel($channelName, $repoUri, $branch) {
    $darcParams = @( "default-channel-status", "--channel", "$channelName", "--repo", "$repoUri", "--branch", "$branch", "--enable" )
    Darc-Command -darcParams $darcParams
}

function Darc-Disable-Default-Channel($channelName, $repoUri, $branch) {
    $darcParams = @( "default-channel-status", "--channel", "$channelName", "--repo", "$repoUri", "--branch", "$branch", "--disable" )
    Darc-Command -darcParams $darcParams
}

function Darc-Get-Default-Channel-From-Api($repoUri, $branch) {
    $headers = Get-Bar-Headers 'text/plain'
    $uri = "$maestroInstallation/api/default-channels?repository=$repoUri&branch=$branch&api-version=${barApiVersion}"
    $response = Invoke-WebRequest -Uri $uri -Headers $headers -Method Get
    $jsonResponse = $response | ConvertFrom-Json
    return $jsonResponse
}

function Darc-Delete-Subscription($subscriptionId) {
    Darc-Command delete-subscriptions --id $subscriptionId --quiet
}

function Darc-Get-Subscription($subscriptionId) {
    Darc-Command get-subscriptions --ids $subscriptionId
}

function Darc-Get-Subscription-Enabled($subscriptionId) {
    return $(Darc-Command get-subscriptions --ids $subscriptionId) -match "- Enabled: True"
}

function Darc-Add-Subscription-Process-Output($output) {
    $match = $output -match "Successfully created new subscription with id '([a-f0-9-]+)'"

    # Batched subscriptions return a warning that non-batched subscriptions don't,
    # the behavior of -match changes depending on whether the input is an array or a scalar
    # so we check if the special $Matches variable has any content to determine if we should
    # try another match
    if ($match) {
        if (!$Matches) {
            $match[0] -match "'([a-f0-9-]+)'" | Out-Null
        }
        $subscriptionId = $Matches[1].replace("'", "")
        if (-not $subscriptionId) {
            throw "Failed to extract subscription id`n${output}"
        }
        $global:subscriptionsToDelete += $subscriptionId
        $subscriptionId
    } else {
        throw "Failed to create subscription or parse subscription id`n${output}"
    }
}

# Run darc add-subscription with the specified parameters, extract out the subscription id,
# and record it for teardown later. Implicitly passes -q and --no-trigger
function Darc-Add-Subscription([Parameter(ValueFromRemainingArguments=$true)]$darcParams) {
    $darcParams = @( "add-subscription" ) + $darcParams + @( "-q", "--no-trigger" )
    $output = Darc-Command -darcParams $darcParams
    Darc-Add-Subscription-Process-Output $output
}

function Darc-Add-Subscription-And-Trigger([Parameter(ValueFromRemainingArguments=$true)]$darcParams) {
    $darcParams = @( "add-subscription" ) + $darcParams + @( "-q", "--trigger" )
    $output = Darc-Command -darcParams $darcParams
    Darc-Add-Subscription-Process-Output $output
}

function Darc-Add-Subscription-From-Yaml($yamlText) {
    $darcParams = @( "add-subscription", "--read-stdin", "--no-trigger" )
    $output = $yamlText | Darc-Command-WithPipeline -darcParams $darcParams
    Darc-Add-Subscription-Process-Output $output
}

function Trigger-Subscription($subscriptionId) {
    $headers = Get-Bar-Headers 'text/plain'
    Write-Host "Triggering subscription $subscriptionId"

    $uri = "$maestroInstallation/api/subscriptions/$subscriptionId/trigger?api-version=$barApiVersion"

    Invoke-WebRequest -Uri $uri -Headers $headers -Method Post
}

function Get-ChannelId($channelName) {
    Write-Host "Looking up id of channel '${channelName}'"
    $headers = Get-Bar-Headers 'text/plain' $barToken
    $getChannelsEndpoint = "$maestroInstallation/api/channels?api-version=${barApiVersion}"
    $channels = Invoke-WebRequest -Uri $getChannelsEndpoint -Headers $headers | ConvertFrom-Json
    $channelId = $($channels | Where-Object -Property "name" -Value "${channelName}" -EQ | Select-Object -Property id).id
    if (!$channelId) {
        throw "Channel ${channelName} not found"
    }
    $channelId
}

function Add-Build-To-Channel ($buildId, $channelName) {
    # Look up the channel id
    $channelId = Get-ChannelId $channelName

    Write-Host "Adding build ${buildId} to channel ${channelId}"
    $darcParams = @("add-build-to-channel", "--id", "$buildId", "--channel", "$channelName", "--skip-assets-publishing" )
    Darc-Command -darcParams $darcParams
}

function Remove-Build-From-Channel ($buildId, $channelName) {
    # Look up the channel id
    $channelId = Get-ChannelId $channelName

    Write-Host "Removing build ${buildId} from channel ${channelId}"
    $darcParams = @("delete-build-from-channel", "--id", "$buildId", "--channel", "$channelName" )
    Darc-Command -darcParams $darcParams
}

function New-Build($repository, $branch, $commit, $buildNumber, $assets, $publishUsingPipelines, $dependencies) {
    if (!$publishUsingPipelines) {
        $publishUsingPipelines = "false"
    }

    $headers = Get-Bar-Headers 'text/plain'
    $body = @{
        gitHubRepository = $repository;
        azureDevOpsRepository = $repository;
        gitHubBranch = $branch;
        azureDevOpsBranch = $branch;
        azureDevOpsAccount = "dnceng";
        azureDevOpsProject = "internal";
        azureDevOpsBuildNumber = $buildNumber;
        azureDevOpsBuildId = 144618;
        azureDevOpsBuildDefinitionId = 6;
        commit = $commit;
        assets = $assets;
        dependencies = $dependencies;
        publishUsingPipelines = $publishUsingPipelines;
    }
    $bodyJson = ConvertTo-Json $body -Depth 4
    Write-Host "Creating Build:"
    Write-Host $bodyJson

    $uri = "$maestroInstallation/api/builds?api-version=$barApiVersion"

    Write-Host "Creating a new build in the Build Asset Registry..."

    $response = Invoke-WebRequest -Uri $uri -Headers $headers -Method Post -Body $bodyJson -ContentType 'application/json' | ConvertFrom-Json

    $newBuildId = $response.Id
    Write-Host "Successfully created build with id $newBuildId"

    $newBuildId
}

function Get-Build($id) {
    $headers = Get-Bar-Headers 'application/json'
    Write-Host "Getting Build $id"

    $uri = "$maestroInstallation/api/builds/${id}?api-version=$barApiVersion"

    $response = Invoke-WebRequest -Uri $uri -Headers $headers -Method Get | ConvertFrom-Json
    $response
}

function Darc-Get-Build($id) {
    $darcParams = @( "get-build", "--id", "$id" )
    Darc-Command -darcParams $darcParams
}
function Darc-Update-Build($id, $updateParams) {
    $darcParams = @( "update-build", "--id", "$id" ) + $updateParams
    Darc-Command -darcParams $darcParams
}

function Get-Bar-Headers([string]$accept) {
    $headers = New-Object 'System.Collections.Generic.Dictionary[[String],[String]]'
    $headers.Add('Accept', $accept)
    $headers.Add('Authorization',"Bearer $maestroBearerToken")
    return $headers
}

function Get-Repo-Location($repoName) {
    "$testRoot\$repoName"
}

function Git-Command($repoName) {
    Push-Location -Path $(Get-Repo-Location($repoName))
    try {
        $gitParams = $args
        if ($gitParams.GetType().Name -ne "Object[]") {
            $gitParams = $gitParams.ToString().Split(" ")
        }
        Write-Host "Running 'git $gitParams' from $(Get-Location)"
        $commandOutput = & git @gitParams; if ($LASTEXITCODE -ne 0) { throw "Git exited with exit code: $LASTEXITCODE" } else { $commandOutput }
        $commandOutput
    }
    finally {
        Pop-Location
    }
}

function Create-Pipeline($releasePipelineId) {
    $headers = Get-Bar-Headers 'text/plain'

    $uri = "$maestroInstallation/api/pipelines?pipelineIdentifier=$releasePipelineId&organization=dnceng&project=internal&api-version=$barApiVersion"

    Write-Host "Creating a new pipeline in the Build Asset Registry..."

    $response = Invoke-WebRequest -Uri $uri -Headers $headers -Method Post | ConvertFrom-Json
    write-host $response
    $pipelineId = $response.id
    Write-Host "Created Pipeline with id $pipelineId"
    $global:pipelinesToDelete += $pipelineId
    return $pipelineId
}

function Delete-Pipeline($barPipelineId) {
    $headers = Get-Bar-Headers 'text/plain'

    $uri = "$maestroInstallation/api/pipelines/${barPipelineId}?api-version=$barApiVersion"

    Write-Host "Deleting Pipeline $barPipelineId from the Build Asset Registry..."

    Invoke-WebRequest -Uri $uri -Headers $headers -Method Delete | Out-Null
}

function Add-Pipeline-To-Channel($channelName, $pipelineId) {
    $channelId = Get-ChannelId $channelName

    $headers = Get-Bar-Headers 'text/plain'

    $uri = "$maestroInstallation/api/channels/${channelId}/pipelines/${pipelineId}?api-version=$barApiVersion"

    Write-Host "Adding pipeline ${pipelineId} to channel ${channelId} in the Build Asset Registry..."

    Invoke-WebRequest -Uri $uri -Headers $headers -Method Post

    $global:channelPipelinesToDelete[$channelName] += $pipelineId
}

function Remove-Pipeline-From-Channel($channelName, $pipelineId) {
    $channelId = Get-ChannelId $channelName

    $headers = Get-Bar-Headers 'text/plain'

    $uri = "$maestroInstallation/api/channels/${channelId}/pipelines/${pipelineId}?api-version=$barApiVersion"

    Write-Host "Removing pipeline ${pipelineId} from channel ${channelId} in the Build Asset Registry..."

    Invoke-WebRequest -Uri $uri -Headers $headers -Method Delete | Out-Null
}

#
# Azure DevOps specific functionality
#

function Get-AzDO-RepoAuthUri($repoName) {
    return "https://${azdoUser}:${azdoPAT}@dev.azure.com/${azdoAccount}/${azdoProject}/_git/${repoName}"
}

function Get-AzDO-RepoUri($repoName) {
    return "https://dev.azure.com/${azdoAccount}/${azdoProject}/_git/${repoName}"
}

function AzDO-Clone($repoName) {
    $authUri = Get-AzDO-RepoAuthUri $repoName
    & git clone $authUri $(Get-Repo-Location $repoName)
    Push-Location -Path $(Get-Repo-Location $repoName)
    & git config user.email $azdoUser@test.com
    & git config user.name $azdoUser
    Pop-Location
}

function AzDO-Delete-Branch($repoName, $branchName) {
    $uri = "$(Get-AzDO-RepoApiUri($repoName))/refs?api-version=${azdoApiVersion}"
    $body = ConvertTo-Json @(@{
        name="refs/heads/${branchName}"
        newObjectId="0000000000000000000000000000000000000000"
        oldObjectId="0000000000000000000000000000000000000000"
    })
    Invoke-WebRequest -Uri $uri -Headers $(Get-AzDO-Headers) -Method Post -Body $body -ContentType 'application/json' | Out-Null
}

function Close-AzDO-PullRequest($targetRepoName, $prId) {
    $uri = "$(Get-AzDO-RepoApiUri($targetRepoName))/pullrequests/${prId}?api-version=${azdoApiVersion}"
    $body = @{
        status="abandoned"
    } | ConvertTo-Json
    Invoke-WebRequest -Uri $uri -Headers $(Get-AzDO-Headers) -Method Patch -Body $body -ContentType 'application/json' | Out-Null
}

function Get-AzDO-PullRequests($targetRepoName, $targetBranch) {
    $uri = "$(Get-AzDO-RepoApiUri($targetRepoName))/pullrequests?searchCriteria.status=active&searchCriteria.targetRefName=refs/heads/${targetBranch}&api-version=${azdoApiVersion}"
    Invoke-WebRequest -Uri $uri -Headers $(Get-AzDO-Headers) -Method Get | ConvertFrom-Json
}

function Get-AzDO-PullRequest($targetRepoName, $prId) {
    $uri = "$(Get-AzDO-RepoApiUri($targetRepoName))/pullrequests/${prId}?api-version=${azdoApiVersion}"
    Invoke-WebRequest -Uri $uri -Headers $(Get-AzDO-Headers) -Method Get | ConvertFrom-Json
}

function Check-AzDO-PullRequest-Completed($targetRepoName, $pullRequestNumber) {
    $uri = "$(Get-AzDO-RepoApiUri($targetRepoName))/pullrequests/$pullRequestNumber"
    $tries = 7
    while ($tries-- -gt 0) {
        $pullrequest = Invoke-WebRequest -Uri $uri -Headers $(Get-AzDO-Headers) -Method Get | ConvertFrom-Json
        if ($pullRequest.status -eq "completed") {
            return $true
        }
        Write-Host "Pull request has not been completed. $tries tries remaining."
        Start-Sleep 60
    }
    $global:azdoPRsToClose += @{ number = $pullRequestNumber; repo = $targetRepoName }
    throw "Expected PR to be completed."
}

function Check-AzDO-PullRequest-Created($targetRepoName, $targetBranch) {
    # Check that the PR was created properly. poll azdo
    $tries = 10
    while ($tries-- -gt 0) {
        Start-Sleep 60
        Write-Host "Checking for PRs, ${tries} tries remaining"
        $pullRequests = Get-AzDO-PullRequests $targetRepoName $targetBranch
        if ($pullRequests.count -gt 0) {
            # Find and verify PR info
            if ($pullRequests.count -ne 1) {
                throw "Unexpected number of pull requests open $($pullRequests.count)."
            }
            $pullRequest = $pullRequests.value[0]
            $pullRequestBaseBranch = $pullRequest.sourceRefName.Replace('refs/heads/','')
            $global:azdoBranchesToDelete += @{ branch = $pullRequestBaseBranch; repo = $targetRepoName}
            $global:azdoPRsToClose += @{ number = $pullRequest.pullRequestId; repo = $targetRepoName }
            return $pullRequest
        }
    }
}

function Compare-Array-Output($expected, $actual) {
    if ($expected.Count -ne $actual.Count) {
        Write-Host "Expected $($expected.Count) lines, got $($actual.Count) lines."
        return $false
    }
    for ($i = 0; $i -lt $expected.Count; $i++) {
        if ($actual[$i] -notmatch $expected[$i]) {
            Write-Host "Line $i not matched`nExpected '$($expected[$i])'`nActual   '$($actual[$i])'"
            return $false
        }
    }
    return $true
}

function Validate-AzDO-PullRequest-Contents($pullRequest, $expectedPRTitle, $targetRepoName, $targetBranch, $expectedDependencies) {
    $pullRequestBaseBranch = $pullRequest.sourceRefName.Replace('refs/heads/','')

    # Depending on how quickly each dependency update comes through,
    # we might have to wait for the title to be updated correctly for batched Subscriptions.
    $tries = 5
    $validTitle = $false;
    while ($tries-- -gt 0 -and (-not $validTitle)) {
        Write-Host "Validating PR title. $tries tries remaining..."
        $pullRequest = Get-AzDO-PullRequest $targetRepoName $pullRequest.pullRequestId
        if ($pullRequest.title -eq $expectedPRTitle) {
            $validTitle = $true
            break
        }
        Start-Sleep 30
    }

    if (-not $validTitle) {
        throw "Expected PR title to be $expectedPRTitle, was $($pullrequest.title)"
    }

    Validate-PullRequest-Dependencies $targetRepoName $pullRequestBaseBranch $expectedDependencies 1
}

function Validate-Feeds-NugetConfig($targetRepoName, $expectedFeeds, $notExpectedFeeds) {
    try {
        # there's a good chance we're already there.
        Push-Location -Path $(Get-Repo-Location $targetRepoName) -ErrorAction SilentlyContinue
        [xml]$nugetConfig = Get-Content "NuGet.config"
        $currentFeeds = $nugetConfig.Configuration.PackageSources.Add.Value
        $missingFeeds = $expectedFeeds.Where{$_ -notin $currentFeeds}
        if ($missingFeeds) {
            Write-Error "Missing feeds! `n Expected: $expectedFeeds `n Found: $currentFeeds"
            throw "PR did not have expected feeds"
        }
        $wrongFeeds = $currentFeeds.Where{$_ -in $notExpectedFeeds}
        if ($wrongFeeds) {
            Write-Error "Incorrect feeds present! `n Did not expect $wrongFeeds to be part of the PR"
            throw "PR had extra unexpected feeds"
        }
        Write-Host "Finished validating feeds"
        return $true
    } finally {
        Pop-Location
    }
}

function Check-NonBatched-AzDO-PullRequest($sourceRepoName, $targetRepoName, $targetBranch, $expectedDependencies, $complete = $false, $expectedFeeds = @(), $notExpectedFeeds = @()) {
    $expectedPRTitle = "[$targetBranch] Update dependencies from $azdoAccount/$azdoProject/$sourceRepoName"
    return Check-AzDO-PullRequest `
        -expectedPRTitle $expectedPRTitle `
        -targetRepoName $targetRepoName `
        -targetBranch $targetBranch `
        -expectedDependencies $expectedDependencies `
        -complete $complete `
        -expectedFeeds $expectedFeeds `
        -notExpectedFeeds $notExpectedFeeds
}

function Check-Batched-AzDO-PullRequest($sourceRepoCount, $targetRepoName, $targetBranch, $expectedDependencies, $expectedFeeds = @(), $notExpectedFeeds = @()) {
    $expectedPRTitle = "[$targetBranch] Update dependencies from $sourceRepoCount repositories"
    return Check-AzDO-PullRequest `
        -expectedPRTitle $expectedPRTitle `
        -targetRepoName $targetRepoName `
        -targetBranch $targetBranch `
        -exptectedDependencies $expectedDependencies `
        -complete $false `
        -expectedFeeds $expectedFeeds `
        -notExpectedFeeds $notExpectedFeeds
}

function Check-AzDO-PullRequest($expectedPRTitle, $targetRepoName, $targetBranch, $exptectedDependencies, $complete, $expectedFeeds, $notExpectedFeeds)
{
    Write-Host "Checking Opened PR in $targetBranch $targetRepoName ..."
    $pullRequest = Check-AzDO-PullRequest-Created $targetRepoName $targetBranch
    if (!$pullRequest) {
        return $false
    }
    Validate-AzDO-PullRequest-Contents -pullRequest $pullRequest -expectedPRTitle $expectedPRTitle -targetRepoName $targetRepoName -targetBranch $targetBranch -expectedDependencies $expectedDependencies
    if ($expectedFeeds.count -gt 0) {
        Write-Host "Validating Nuget feeds in PR branch"
        Write-Host "Expected feeds: $expectedFeeds"
        Validate-Feeds-NugetConfig $targetRepoName $expectedFeeds $notExpectedFeeds
    }
    if ($complete) {
        Check-AzDO-PullRequest-Completed $targetRepoName $pullRequest.pullRequestId
    }
    return $true
}

function Get-AzDO-RepoApiUri($repoName) {
    "https://dev.azure.com/${azdoAccount}/${azdoProject}/_apis/git/repositories/${repoName}"
}

function Get-AzDO-Headers() {
    $headers = New-Object 'System.Collections.Generic.Dictionary[[String],[String]]'
    $base64authinfo = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":${azdoPAT}"))
    $headers = @{"Authorization"="Basic $base64authinfo"}
    return $headers
}

# Release API only works with Oauth2 Authentication. For now we can only run these functions inside an AzDo environment,
# so that the SYSTEM_ACCESSTOKEN variable is available.
function Get-AzDO-Releases($releaseDefinitionId, $count) {
    if (-not $env:SYSTEM_ACCESSTOKEN) {
        throw "env:SYSTEM_ACCESSTOKEN is not set. Is the script running within Azure DevOps?"
    }
    $uri = "https://vsrm.dev.azure.com/${azdoAccount}/${azdoProject}/_apis/release/releases?definitionId=${releaseDefinitionId}&api-version=${azdoApiVersion}&`$top=${count}"
    $headers = @{"Authorization"="Bearer $env:SYSTEM_ACCESSTOKEN"}
    $response = Invoke-WebRequest -Uri $uri -Headers $headers -Method Get
    $jsonResponse = ($response | ConvertFrom-Json).Value
    return $jsonResponse
}

function Get-AzDO-Release($releaseId) {
    if (-not $env:SYSTEM_ACCESSTOKEN) {
        throw "env:SYSTEM_ACCESSTOKEN is not set. Is the script running within Azure DevOps?"
    }
    $uri = "https://vsrm.dev.azure.com/${azdoAccount}/${azdoProject}/_apis/release/releases/${releaseId}?api-version=${azdoApiVersion}"
    $headers = @{"Authorization"="Bearer $env:SYSTEM_ACCESSTOKEN"}
    Invoke-WebRequest -Uri $uri -Headers $headers -Method Get | ConvertFrom-Json
}

function Find-BuildId-In-AzDO-Release($releaseDefinitionId, $barBuildId)
{
    write-host "attempting to find a release with BarBuildId: $barBuildId in release pipeline $releaseDefinitionId"
    $found = $False
    $releases = Get-AzDO-Releases $releaseDefinitionId 5
    foreach ($release in $releases) {
        $release = Get-AzDO-Release $release.Id
        write-host $release
        $releaseBarBuildId = $release.Variables.BarBuildId.value
        if ($releaseBarBuildId -and ($releaseBarBuildId -eq $barBuildId)) {
            $found = $True
            break
        }
    }
    return $found
}

#
# Github specific functionality
#

function Get-Github-Headers() {
    $headers = New-Object 'System.Collections.Generic.Dictionary[[String],[String]]'
    $headers.Add('Authorization',"token $githubPAT")
    return $headers
}

function Get-Github-RepoAuthUri($repoName) {
    "https://${githubUser}:${githubPAT}@github.com/${githubTestOrg}/${repoName}"
}

function Get-Github-RepoUri($repoName) {
    "https://github.com/${githubTestOrg}/${repoName}"
}

function Get-Github-RepoApiUri($repoName) {
    "https://api.github.com/repos/${githubTestOrg}/${repoName}"
}

function GitHub-Clone($repoName) {
    $authUri = Get-Github-RepoAuthUri $repoName
    & git clone $authUri $(Get-Repo-Location $repoName)
    Push-Location -Path $(Get-Repo-Location $repoName)
    & git config user.email "${githubUser}@test.com"
    & git config user.name $githubUser
    Pop-Location
}

function GitHub-Delete-Branch($repoName, $branchName) {
    $uri = "$(Get-Github-RepoApiUri($repoName))/git/refs/heads/${branchName}"
    Invoke-WebRequest -Uri $uri -Headers $(Get-Github-Headers) -Method Delete | Out-Null
}

function Close-GitHub-PullRequest($targetRepoName, $prId) {
    $uri = "$(Get-Github-RepoApiUri($targetRepoName))/pulls/${prId}"
    $body = @{
        state="closed"
    } | ConvertTo-Json
    Invoke-WebRequest -Uri $uri -Headers $(Get-Github-Headers) -Method Patch -Body $body | Out-Null
}

function Validate-Subscription-Info($subscriptionId, $expectedInfo){

    $subscriptionInfo = Darc-Get-Subscription $subscriptionId

    if (-not $(Compare-Array-Output $expectedInfo $subscriptionInfo)) {
        throw "Subscription did not have expected info"
    }
    Write-Host "Finished validating subscription info"
}

function Get-GitHub-PullRequests($targetRepoName, $targetBranch) {
    $uri = "$(Get-Github-RepoApiUri($targetRepoName))/pulls?state=open&base=$targetBranch"
    Invoke-WebRequest -Uri $uri -Headers $(Get-Github-Headers) -Method Get | ConvertFrom-Json
}

function Get-GitHub-PullRequest($targetRepoName, $prId) {
    $uri = "$(Get-Github-RepoApiUri($targetRepoName))/pulls/$prId"
    Invoke-WebRequest -Uri $uri -Headers $(Get-Github-Headers) -Method Get | ConvertFrom-Json
}

function Get-Github-File-Contents($targetRepoName, $path, $ref) {
    $uri = "$(Get-Github-RepoApiUri($targetRepoName))/contents/${path}?ref=$ref"
    Invoke-WebRequest -Uri $uri -Headers $(Get-Github-Headers) -Method Get | ConvertFrom-Json
}

function Check-Github-PullRequest-Completed($targetRepoName, $pullRequestNumber) {
    $uri = "$(Get-Github-RepoApiUri($targetRepoName))/pulls/$pullRequestNumber/merge"
    Write-Host "Checking $uri until it reports a completed merge"
    # Maestro checks PRs every 5 minutes, give it a couple extra minutes just in case.
    $tries = 7
    while ($tries-- -gt 0) {
        # Github API returns 404 if the PR has not been merged, so check for exception and keep trying
        try {
            Invoke-WebRequest -Uri $uri -Headers $(Get-Github-Headers) -Method Get
            return
        }
        catch {
            Write-Host "Pull request has not been completed. $tries tries remaining."
            Start-Sleep 60
        }
    }
    throw "Expected PR to be completed."
}

function Check-Github-PullRequest-Created($targetRepoName, $targetBranch) {
    # Check that the PR was created properly. poll github
    $tries = 10
    while ($tries-- -gt 0) {
        Start-Sleep 60
        Write-Host "Checking for PRs, ${tries} tries remaining"
        $pullRequests = Get-GitHub-PullRequests $targetRepoName $targetBranch

        if ($pullRequests) {
            # Find and verify PR info
            if ($pullRequests.Count -ne 1) {
                throw "Unexpected number of pull requests opened."
            }
            $pullRequest = $pullRequests[0]
            $pullRequestBaseBranch = $pullRequest.head.ref
            $global:gitHubPRsToClose += @{ number = $pullRequest.number; repo = $targetRepoName }
            $global:githubBranchesToDelete += @{ branch = $pullRequestBaseBranch; repo = $targetRepoName}
            return $pullRequest[0]
        }
    }
    throw "Could not find open pull request in $targetRepoName for branch $targetBranch"
}

function Validate-Github-PullRequest-Contents($pullRequest, $expectedPRTitle, $targetRepoName, $targetBranch, $expectedDependencies) {
    # Depending on how quickly each dependency update comes through,
    # we might have to wait for the title to be updated correctly for batched Subscriptions
    $tries = 5
    $validTitle = $false;
    while ($tries-- -gt 0) {
        Write-Host "Validating PR title. $tries tries remaining..."
        $pullRequest = Get-GitHub-PullRequest $targetRepoName $pullRequest.number
        if ($pullRequest.title -eq $expectedPRTitle) {
            $validTitle = $true
            break
        }
        Start-Sleep 30
    }

    if (-not $validTitle) {
        throw "Expected PR title to be $expectedPRTitle, was $($pullrequest.title)"
    }

    Validate-PullRequest-Dependencies $targetRepoName $pullRequest.head.ref $expectedDependencies 1
}

function Validate-PullRequest-Dependencies($targetRepoName, $pullRequestBaseBranch, $expectedDependencies, $tries) {
    $triesRemaining = $tries
    while ($triesRemaining-- -gt 0) {
        # Check out the merge commit sha, then use darc to get and verify the
        # dependencies
        Git-Command $targetRepoName fetch
        Git-Command $targetRepoName checkout $pullRequestBaseBranch
        Git-Command $targetRepoName pull

        try {
            Push-Location -Path $(Get-Repo-Location $targetRepoName)
            $dependencies = Darc-Command get-dependencies
            if ($(Compare-Array-Output $expectedDependencies $dependencies)) {
                Write-Host "Finished validating PR contents"
                return $true
            }
        } finally {
            Pop-Location
        }

        Start-Sleep 30
    }

    throw "PR did not have expected dependency updates."
}

function Check-NonBatched-Github-PullRequest($sourceRepoName, $targetRepoName, $targetBranch, $expectedDependencies, $complete = $false) {
    $expectedPRTitle = "[$targetBranch] Update dependencies from $githubTestOrg/$sourceRepoName"
    return Check-Github-PullRequest $expectedPRTitle $targetRepoName $targetBranch $expectedDependencies $complete
}

function Check-Batched-Github-PullRequest($sourceRepoCount, $targetRepoName, $targetBranch, $expectedDependencies) {
    $expectedPRTitle = "[$targetBranch] Update dependencies from $sourceRepoCount repositories"
    return Check-Github-PullRequest $expectedPRTitle $targetRepoName $targetBranch $expectedDependencies $false
}

function Check-Github-PullRequest($expectedPRTitle, $targetRepoName, $targetBranch, $exptectedDependencies, $complete)
{
    Write-Host "Checking Opened PR in $targetBranch $targetRepoName ..."
    $pullRequest = Check-Github-PullRequest-Created $targetRepoName $targetBranch
    if (!$pullRequest) {
        return $false
    }
    Validate-Github-PullRequest-Contents $pullRequest $expectedPRTitle $targetRepoName $targetBranch $expectedDependencies
    if ($complete) {
        Check-Github-PullRequest-Completed $targetRepoName $pullRequest.number
    }
    return $true
}

function Validate-Arcade-PullRequest-Contents($pullRequest, $expectedPRTitle, $targetRepoName, $targetBranch, $expectedDependencies) {
    Validate-Github-PullRequest-Contents $pullRequest $expectedPRTitle $targetRepoName $targetBranch $expectedDependencies
    Write-Host "Validating dependency update PR changes specific to arcade..."
    Write-Host "Checking for eng\common directory..."
    $engCommon = Get-Github-File-Contents $targetRepoName "eng/common" $pullRequest.merge_commit_sha

    if (!$engCommon -or $engCommon.Length -eq 0) {
        throw "Could not find update to eng/common files in the pull request."
    }
    $globaljson = Get-Github-File-Contents $targetRepoName "global.json" $pullRequest.merge_commit_sha
    if (!$globaljson) {
        throw "Could not find global.json in the pull request."
    }
}

function Get-ArcadeRepoUri
{
    "https://github.com/dotnet/arcade"
}

# Run darc add-goal and record the channel for later deletion
function Darc-Set-Goal($channel, $definitionId, $minutes) {
    $darcParams = @("set-goal", "--channel", "$channel", "--definition-id", "$definitionId", "--minutes" , "$minutes")
    Darc-Command -darcParams $darcParams
}

function Darc-Get-Goal($channel, $definitionId) {
    $darcParams = @("get-goal", "--channel", "$channel", "--definition-id", "$definitionId")
    Darc-Command -darcParams $darcParams
}
