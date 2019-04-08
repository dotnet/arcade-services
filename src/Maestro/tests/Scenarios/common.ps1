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
[string]$barApiVersion = "2019-01-16"
$gitHubPRsToClose = @()
$githubBranchesToDelete = @()
$azdoPRsToClose = @()
$azdoBranchesToDelete = @()
$subscriptionsToDelete = @()
$channelsToDelete = @()
$defaultChannelsToDelete = @()
$pipelinesToDelete = @()
$channelPipelinesToDelete = @{}

# Get a temporary directory for a test root
$testRoot = Join-Path -Path $([System.IO.Path]::GetTempPath()) -ChildPath $([System.IO.Path]::GetRandomFileName())
New-Item -Path $testRoot -ItemType Directory | Out-Null

$darcTool = ""
if (Test-Path $darcVersion) {
    $darcTool = "dotnet $darcVersion"
    Write-Host "Using local darc binary $darcTool"
} else {
    Write-Host "Temporary testing location located at $testRoot"
    Write-Host "Installing Darc: dotnet tool install --tool-path $testRoot --version ${darcVersion} Microsoft.DotNet.Darc"
    & dotnet tool install --tool-path $testRoot --version $darcVersion "Microsoft.DotNet.Darc"
    $darcTool = Join-Path -Path $testRoot -ChildPath "darc"
}
Write-Host

# Set auth parameters
$darcAuthParams = "--bar-uri $maestroInstallation --github-pat $githubPAT --azdev-pat $azdoPAT --password $maestroBearerToken"

# Enable TLS 1.2
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Teardown() {

    Write-Host

    Write-Host "Cleaning $($subscriptionsToDelete.Count) subscriptions"
    foreach ($subscriptionId in $subscriptionsToDelete) {
        try {
            Write-Host "Deleting $subscriptionId"
            Darc-Command delete-subscription --id $subscriptionId
        } catch {
            Write-Warning "Failed to delete subscription with id $subscriptionId"
            Write-Warning $_
        }
    }

    Write-Host "Cleaning $($defaultChannelsToDelete.Count) default channels"
    foreach ($defaultChannel in $defaultChannelsToDelete) {
        try {
            Write-Host "Deleting default channel $($defaultChannel.repo)@$($defaultChannel.branch) -> $($defaultChannel.channel)"
            Darc-Delete-Default-Channel $defaultChannel.channel $defaultChannel.repo $defaultChannel.branch
        } catch {
            Write-Warning "Failed to delete default channel $($defaultChannel.repo)@$($defaultChannel.branch) -> $($defaultChannel.channel)"
            Write-Warning $_
        }
    }

    Write-Host "Cleaning $($channelPipelinesToDelete.Count) channel-pipeline mappings"
    foreach ($channelId in $channelPipelinesToDelete.Keys) {
        $pipelineIds = $channelPipelinesToDelete[$channelId]
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

    Write-Host "Cleaning $($channelsToDelete.Count) channels"
    foreach ($channel in $channelsToDelete) {
        try {
            Write-Host "Deleting channel $channel"
            Darc-Command delete-channel --name `'$channel`'
        } catch {
            Write-Warning "Failed to delete channel $channel"
            Write-Warning $_
        }
    }

    Write-Host "Cleaning $($pipelinesToDelete.Count) pipelines"
    foreach ($pipeline in $pipelinesToDelete) {
        try {
            Write-Host "Deleting pipeline $pipeline"
            Delete-Pipeline $pipeline
        } catch {
            Write-Warning "Failed to delete pipeline $pipeline"
            Write-Warning $_
        }
    }

    Write-Host "Cleaning $($githubBranchesToDelete.Count) github branches"
    foreach ($branch in $githubBranchesToDelete) {
        try {
            Write-Host "Removing $($branch.branch) from $($branch.repo)"
            GitHub-Delete-Branch $branch.repo $branch.branch
        } catch {
            Write-Warning "Failed to remove github branch $($branch.branch) $($branch.repo)"
            Write-Warning $_
        }
    } 

    Write-Host "Cleaning $($gitHubPRsToClose.Count) github PRs"
    foreach ($pr in $gitHubPRsToClose) {
        try {
            Write-Host "Closing pull request $($pr.number) in $($pr.repo)"
            Close-GitHub-PullRequest $pr.repo $pr.number
        } catch {
            Write-Warning "Failed to close github pull request $($pr.number) in $($pr.repo)"
            Write-Warning $_
        }
    }

    Write-Host "Cleaning $($azdoBranchesToDelete.Count) azdo branches"
    foreach ($branch in $azdoBranchesToDelete) {
        try {
            Write-Host "Removing $($branch.branch) from $($branch.repo)"
            AzDO-Delete-Branch $branch.repo $branch.branch
        } catch {
            Write-Warning "Failed to remove azdo branch $($branch.branch) $($branch.repo)"
            Write-Warning $_
        }
    }

    Write-Host "Cleaning $($azdoPRsToClose.Count) azdo PRs"
    foreach ($pr in $azdoPRsToClose) {
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

function Darc-Command() {
    $darcParams = $args
    Darc-Command-Impl $darcParams
}

function Darc-Command-Impl($darcParams) {
    $baseDarcCommand = "& $darcTool $darcParams" 
    Write-Host "Running 'darc $darcParams $darcAuthParams'"
    $darcCommand = "`$commandOutput = $baseDarcCommand $darcAuthParams; if (`$LASTEXITCODE -ne 0) { Write-Host `${commandOutput};throw `"Darc command exited with exit code: `$LASTEXITCODE`" } else { `$commandOutput }"
    Invoke-Expression $darcCommand
}

# Run darc add-channel and record the channel for later deletion
function Darc-Add-Channel($channelName, $classification) {
    $darcParams = "add-channel --name '$channelName' --classification '$classification'"
    Darc-Command-Impl $darcParams
}

function Darc-Delete-Channel($channelName) {
    $darcParams = "delete-channel --name '$channelName'"
    Darc-Command-Impl $darcParams
}

# Run darc add-channel and record the channel for later deletion
function Darc-Add-Default-Channel($channelName, $repoUri, $branch) {
    $darcParams = "add-default-channel --channel '$channelName' --repo '$repoUri' --branch '$branch'"
    Darc-Command-Impl $darcParams
}

function Darc-Delete-Default-Channel($channelName, $repoUri, $branch) {
    $darcParams = "delete-default-channel --channel '$channelName' --repo '$repoUri' --branch '$branch'"
    Darc-Command-Impl $darcParams
}

# Run darc add-subscription with the specified parameters, extract out the subscription id,
# and record it for teardown later. Implicitly passes -q
function Darc-Add-Subscription() {
    $darcParams = "add-subscription $args -q"
    $output = Darc-Command-Impl $darcParams
    
    if ($output -match "Successfully created new subscription with id '([a-f0-9-]+)'") {
        $subscriptionId = $matches[1]
        if (-not $subscriptionId) {
            throw "Failed to extract subscription id"
        }
        $subscriptionId
    } else {
        throw "Failed to create subscrption or parse subscription id"
    }
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
    $headers = Get-Bar-Headers 'text/plain'
    $uri = "$maestroInstallation/api/channels/${channelId}/builds/${buildId}?api-version=${barApiVersion}"
    Invoke-WebRequest -Uri $uri -Headers $headers -Method Post
}

function New-Build($repository, $branch, $commit, $buildNumber, $assets, $publishUsingPipelines) {
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
        publishUsingPipelines = $publishUsingPipelines;
    }
    $bodyJson = ConvertTo-Json $body
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
        $baseGitCommand = "git $gitParams" 
        Write-Host "Running '$baseGitCommand' from $(Get-Location)"
        $gitCommand = "`$commandOutput = $baseGitCommand; if (`$LASTEXITCODE -ne 0) { throw 'Git exited with exit code: `$LASTEXITCODE' } else { `$commandOutput }"
        Invoke-Expression $gitCommand
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
    return $pipelineId
}

function Delete-Pipeline($barPipelineId) {
    $headers = Get-Bar-Headers 'text/plain'

    $uri = "$maestroInstallation/api/pipelines/${barPipelineId}?api-version=$barApiVersion"

    Write-Host "Deleting Pipeline $barPipelineId from the Build Asset Registry..."

    Invoke-WebRequest -Uri $uri -Headers $headers -Method Delete
}

function Add-Pipeline-To-Channel($channelName, $pipelineId) {
    $channelId = Get-ChannelId $channelName

    $headers = Get-Bar-Headers 'text/plain'

    $uri = "$maestroInstallation/api/channels/${channelId}/pipelines/${pipelineId}?api-version=$barApiVersion"

    Write-Host "Adding pipeline ${pipelineId} to channel ${channelId} in the Build Asset Registry..."

    $response = Invoke-WebRequest -Uri $uri -Headers $headers -Method Post

    Write-Host $response
}

function Remove-Pipeline-From-Channel($channelName, $pipelineId) {
    $channelId = Get-ChannelId $channelName

    $headers = Get-Bar-Headers 'text/plain'

    $uri = "$maestroInstallation/api/channels/${channelId}/pipelines/${pipelineId}?api-version=$barApiVersion"

    Write-Host "Removing pipeline ${pipelineId} from channel ${channelId} in the Build Asset Registry..."

    Invoke-WebRequest -Uri $uri -Headers $headers -Method Delete
}

#
# Azure DevOps specific functionality
#

function Get-AzDO-RepoAuthUri($repoName) {
    "https://${azdoUser}:${azdoPAT}@dev.azure.com/${azdoAccount}/${azdoProject}/_git/${repoName}"
}

function Get-AzDO-RepoUri($repoName) {
    "https://dev.azure.com/${azdoAccount}/${azdoProject}/_git/${repoName}"
}

function AzDO-Clone($repoName) {
    $authUri = Get-AzDO-RepoAuthUri $repoName
    & git clone $authUri $(Get-Repo-Location $repoName)
}

function AzDO-Delete-Branch($repoName, $branchName) {
    $uri = "$(Get-AzDO-RepoApiUri($repoName))/refs?api-version=${azdoApiVersion}"
    $body = ConvertTo-Json @(@{
        name="refs/heads/${branchName}"
        newObjectId="0000000000000000000000000000000000000000"
        oldObjectId="0000000000000000000000000000000000000000"
    })
    Invoke-WebRequest -Uri $uri -Headers $(Get-AzDO-Headers) -Method Post -Body $body -ContentType 'application/json'
}

function Close-AzDO-PullRequest($targetRepoName, $prId) {
    $uri = "$(Get-AzDO-RepoApiUri($targetRepoName))/pullrequests/${prId}?api-version=${azdoApiVersion}"
    $body = @{
        status="abandoned"
    } | ConvertTo-Json
    Invoke-WebRequest -Uri $uri -Headers $(Get-AzDO-Headers) -Method Patch -Body $body -ContentType 'application/json'
}

function Get-AzDO-PullRequests($targetRepoName, $targetBranch) {
    $uri = "$(Get-AzDO-RepoApiUri($targetRepoName))/pullrequests?searchCriteria.status=active&searchCriteria.targetRefName=refs/heads/${targetBranch}&api-version=${azdoApiVersion}"
    Invoke-WebRequest -Uri $uri -Headers $(Get-AzDO-Headers) -Method Get  | ConvertFrom-Json
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

function Get-AzDO-Release($releaseId) {
    $uri = "https://dev.azure.com/${azdoAccount$}/${azdoProject}/_apis/release/releases/${releaseId}&api-version=${azdoApiVersion}"
    Invoke-WebRequest -Uri $uri -Headers $(Get-AzDO-Headers) -Method Get | ConvertFrom-Json
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
}

function GitHub-Delete-Branch($repoName, $branchName) {
    $uri = "$(Get-Github-RepoApiUri($repoName))/git/refs/heads/${branchName}"
    Invoke-WebRequest -Uri $uri -Headers $(Get-Github-Headers) -Method Delete
}

function Close-GitHub-PullRequest($targetRepoName, $prId) {
    $uri = "$(Get-Github-RepoApiUri($targetRepoName))/pulls/${prId}"
    $body = @{
        state="closed"
    } | ConvertTo-Json
    Invoke-WebRequest -Uri $uri -Headers $(Get-Github-Headers) -Method Patch -Body $body
}

function Get-GitHub-PullRequests($targetRepoName, $targetBranch) {
    $uri = "$(Get-Github-RepoApiUri($targetRepoName))/pulls?state=open&base=$targetBranch"
    Invoke-WebRequest -Uri $uri -Headers $(Get-Github-Headers) -Method Get  | ConvertFrom-Json
}
