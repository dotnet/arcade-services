[CmdletBinding()]
param(
    [Parameter(HelpMessage="Number of commits to retrieve from history")]
    [int]$Depth = 20,
    [Parameter(Mandatory=$false, HelpMessage="Path to the repository")]
    [string]$RepoPath = "D:\tmp\aspnetcore",
    [Parameter(Mandatory=$false, HelpMessage="Path to the VMR")]
    [string]$VmrPath = "D:\tmp\dotnet",
    [Parameter(Mandatory=$false, HelpMessage="Number of consecutive commits without cross-references to collapse")]
    [int]$CollapseThreshold = 2,
    [Parameter(Mandatory=$false, HelpMessage="Disable collapsing commits regardless of threshold")]
    [switch]$NoCollapse,
    [Parameter(Mandatory=$false, HelpMessage="Path to output the Mermaid diagram file")]
    [string]$OutputPath = "",
    [switch]$VerboseScript
)

# Import our GitFlowFunctions module
$moduleDir = Join-Path -Path $PSScriptRoot -ChildPath "modules"
$modulePath = Join-Path -Path $moduleDir -ChildPath "GitFlowFunctions.psm1"

# Check if module exists before importing
if (-not (Test-Path $modulePath)) {
    Write-Error "Required module not found: $modulePath"
    exit 1
}

# Import the module
Import-Module $modulePath -Force

# This script loads Git commits from specified repositories and generates a Mermaid TD (top-down) diagram
# The diagram shows commits as nodes in subgroups with parent-child relationships and cross-repo connections
#
# The diagram will show connections between repos based on Source tag references in Version.Details.xml
# Collapsed commits are displayed as a single node showing the first and last commit in the range
#
# Features:
# - Automatically extracts repository URLs from Git remotes
# - Creates clickable nodes that link to the original repository commit or compare view
# - For single commits, the link leads to: [REPO_URL]/commit/[SHA]
# - For collapsed ranges, the link leads to: [REPO_URL]/compare/[LAST_SHA]...[FIRST_SHA]

# Main script execution

try {
    Write-Host "Generating Git commit Mermaid diagram..." -ForegroundColor Green

    $vmrCommits = @()
    $repoCommits = @()

    # Create a parameter hashtable for verbose if needed
    $verboseParam = @{}
    if ($VerboseScript) {
        $verboseParam.Verbose = $true
        Write-Verbose "Verbose mode enabled"
    }    Write-Host "Loading commits from repo ($RepoPath)..." -ForegroundColor Yellow
    $repoCommits = Get-GitCommits -repoPath $RepoPath -count $Depth @verboseParam
    Write-Host "Loaded $($repoCommits.Count) commits from source repository" -ForegroundColor Green

    # Determine the optimal VMR depth by analyzing repo history for referenced commits
    $vmrDepth = Get-OptimalVmrDepth -repoPath $RepoPath -vmrPath $VmrPath -defaultDepth $Depth -minDepth 10 -maxDepth 500 @verboseParam

    Write-Host "Loading commits from VMR ($VmrPath)..." -ForegroundColor Yellow
    $vmrCommits = Get-GitCommits -repoPath $VmrPath -count $vmrDepth @verboseParam
    Write-Host "Loaded $($vmrCommits.Count) commits from VMR repository" -ForegroundColor Green

    # Determine if we're using real repositories
    $useRealRepositories = $true
    if (-not (Test-Path -Path $VmrPath) -or -not (Test-Path -Path $RepoPath)) {
        $useRealRepositories = $false
        Write-Host "One or both repositories not found. Using sample data." -ForegroundColor Yellow
    }

    if ($VerboseScript) {
        Write-Verbose "Using real repositories: $useRealRepositories"
    }

    function Test-CommitInDiagram {
        param (
            [string]$commitSHA,
            [array]$commits,
            [switch]$Verbose
        )

        foreach ($commit in $commits) {
            if ($commit.CommitSHA -eq $commitSHA) {
                if ($Verbose) {
                    Write-Verbose "Found commit $commitSHA in diagram"
                }
                return $true
            }
        }

        if ($Verbose) {
            Write-Verbose "Commit $commitSHA not found in diagram"
        }
        return $false
    }    # Find commits in repo that reference vmr commits via Source tags (forward flow)
    # and VMR commits that reference repo commits via source-manifest.json (backflow)
    $crossRepoConnections = @()
    $backflowConnections = @()

    if ($useRealRepositories) {
        Write-Host "Finding Source tag changes in eng/Version.Details.xml (forward flow)..." -ForegroundColor Yellow
        # Increased count to find more source tag changes across a wider history

        # Check if Verbose is being used and pass it through
        $verboseParam = @{}
        if ($VerboseScript) {
            $verboseParam.Verbose = $true
        }

        $sourceTagChanges = Find-SourceTagChanges -repoPath $RepoPath -filePath "eng/Version.Details.xml" -count $Depth @verboseParam
        Write-Host "Found $($sourceTagChanges.Count) forward flow commits where Source tag actually changed" -ForegroundColor Green

        # Filter connections to only include commits that are in our diagram
        Write-Host "Checking which source tag changes match commits in our diagram..." -ForegroundColor Yellow

        # Keep track of external commits we need to add
        $externalVmrCommits = @{}

        foreach ($change in $sourceTagChanges) {
            $vmrCommitExists = Test-CommitInDiagram -commitSHA $change.SourceSHA -commits $vmrCommits @verboseParam
            $repoCommitExists = Test-CommitInDiagram -commitSHA $change.CommitSHA -commits $repoCommits @verboseParam

            if ($vmrCommitExists -and $repoCommitExists) {
                # Add a type property to identify forward flow connections
                $change | Add-Member -NotePropertyName "ConnectionType" -NotePropertyValue "ForwardFlow" -Force
                $crossRepoConnections += $change
                Write-Host "  + Added forward flow $($change.ShortSHA) -> $($change.ShortSourceSHA)" -ForegroundColor Green
            } else {
                # Record external vmr commits for later inclusion
                if (-not $vmrCommitExists) {
                    $externalVmrCommits[$change.SourceSHA] = $change
                }

                # Always add the connection if repo commit exists
                if ($repoCommitExists) {
                    # Add a type property to identify forward flow connections
                    $change | Add-Member -NotePropertyName "ConnectionType" -NotePropertyValue "ForwardFlow" -Force
                    $crossRepoConnections += $change
                    Write-Host "  + Added: repo $($change.ShortSHA) references external vmr $($change.ShortSourceSHA)" -ForegroundColor Cyan
                }
            }
        }

        # Now find backflow connections from VMR to repo using source-manifest.json
        Write-Host "`nFinding backflow connections in source-manifest.json..." -ForegroundColor Yellow

        # Get the repository mapping from Version.Details.xml
        $repoMapping = Get-SourceTagMappingFromVersionDetails -repoPath $RepoPath @verboseParam
        Write-Host "Using repository mapping from Version.Details.xml: $repoMapping" -ForegroundColor Cyan

        if ($repoMapping) {
            # Find changes in source-manifest.json for this repository
            $manifestChanges = Find-SourceManifestChanges -vmrPath $VmrPath -repoMapping $repoMapping -count $Depth @verboseParam
            Write-Host "Found $($manifestChanges.Count) backflow commits where source-manifest.json changed for $repoMapping" -ForegroundColor Green

            # Filter connections to only include commits that are in our diagram
            Write-Host "Checking which backflow changes match commits in our diagram..." -ForegroundColor Yellow

            # Keep track of external repo commits we need to add
            $externalRepoCommits = @{}

            foreach ($change in $manifestChanges) {
                $vmrCommitExists = Test-CommitInDiagram -commitSHA $change.CommitSHA -commits $vmrCommits @verboseParam
                $repoCommitExists = Test-CommitInDiagram -commitSHA $change.SourceSHA -commits $repoCommits @verboseParam

                if ($vmrCommitExists -and $repoCommitExists) {
                    # Add a type property to identify backflow connections
                    $change | Add-Member -NotePropertyName "ConnectionType" -NotePropertyValue "BackFlow" -Force
                    $backflowConnections += $change
                    Write-Host "  + Added backflow $($change.ShortSHA) -> $($change.ShortSourceSHA)" -ForegroundColor Green
                } else {
                    # Record external repo commits for later inclusion
                    if (-not $repoCommitExists) {
                        $externalRepoCommits[$change.SourceSHA] = $change
                    }
                    if (-not $vmrCommitExists) {
                        Write-Host "  - Skipped: vmr commit $($change.ShortSHA) not in loaded history" -ForegroundColor DarkYellow
                    }

                    # Always add the connection if VMR commit exists
                    if ($vmrCommitExists) {
                        # Add a type property to identify backflow connections
                        $change | Add-Member -NotePropertyName "ConnectionType" -NotePropertyValue "BackFlow" -Force
                        $backflowConnections += $change
                        Write-Host "  + Added: vmr $($change.ShortSHA) references external repo $($change.ShortSourceSHA)" -ForegroundColor Magenta
                    }
                }
            }

            # Add the backflow connections to the cross-repo connections
            $crossRepoConnections += $backflowConnections
        } else {
            Write-Host "Could not determine repository mapping from Version.Details.xml, skipping backflow connections" -ForegroundColor Yellow
        }

        Write-Host "Added $($crossRepoConnections.Count) total cross-repository connections ($($sourceTagChanges.Count) forward, $($backflowConnections.Count) backflow)" -ForegroundColor Green    } else {
        # In sample mode, generate more realistic fake connections
        Write-Host "Generating sample cross-repository connections..." -ForegroundColor Yellow

        # Create sample connections every few commits to simulate periodic dependency updates
        # This creates a more realistic pattern of dependency updates
        $vmrStep = [Math]::Max(1, [Math]::Floor($vmrCommits.Count / 5))
        $repoStep = [Math]::Max(1, [Math]::Floor($repoCommits.Count / 4))        # Generate forward flow connections (repo to VMR)
        for ($i = 0; $i -lt 3; $i++) {
            $vmrIndex = [Math]::Min($vmrStep * $i + 1, $vmrCommits.Count - 1)
            $repoIndex = [Math]::Min($repoStep * $i, $repoCommits.Count - 1)

            $crossRepoConnections += [PSCustomObject]@{
                # For forward flow: repo commit references VMR commit
                CommitSHA = $repoCommits[$repoIndex].CommitSHA  # Repo commit (the requester)
                ShortSHA = $repoCommits[$repoIndex].ShortSHA
                SourceSHA = $vmrCommits[$vmrIndex].CommitSHA    # VMR commit (the target)
                ShortSourceSHA = $vmrCommits[$vmrIndex].ShortSHA
                ConnectionType = "ForwardFlow"                  # Direction: repo -> VMR
            }

            Write-Host "  - Added sample forward flow connection: repo $($repoCommits[$repoIndex].ShortSHA) references vmr $($vmrCommits[$vmrIndex].ShortSHA)" -ForegroundColor Cyan
        }

        # Generate backflow connections (VMR to repo)
        for ($i = 0; $i -lt 3; $i++) {
            $vmrIndex = [Math]::Min($vmrStep * ($i + 2), $vmrCommits.Count - 1)
            $repoIndex = [Math]::Min($repoStep * ($i + 1), $repoCommits.Count - 1)

            $crossRepoConnections += [PSCustomObject]@{
                # For backflow: VMR commit references repo commit
                CommitSHA = $vmrCommits[$vmrIndex].CommitSHA    # VMR commit (the requester)
                ShortSHA = $vmrCommits[$vmrIndex].ShortSHA
                SourceSHA = $repoCommits[$repoIndex].CommitSHA  # Repo commit (the target)
                ShortSourceSHA = $repoCommits[$repoIndex].ShortSHA
                ConnectionType = "BackFlow"                     # Direction: VMR -> repo
            }

            Write-Host "  - Added sample backflow connection: vmr $($vmrCommits[$vmrIndex].ShortSHA) references repo $($repoCommits[$repoIndex].ShortSHA)" -ForegroundColor Magenta
        }

        Write-Host "Added 6 sample cross-repository connections (3 forward flow, 3 backflow)" -ForegroundColor Green
    }    # Get repository URLs for clickable links
    $vmrRepoUrl = ""
    $sourceRepoUrl = ""

    if ($useRealRepositories) {
        Write-Host "Getting repository URLs for clickable links..." -ForegroundColor Yellow

        # Create parameter hashtable for verbose if needed
        $verboseParam = @{}
        if ($VerboseScript) {
            $verboseParam.Verbose = $true
        }

        $vmrRepoUrl = Get-GitRepositoryUrl -repoPath $VmrPath @verboseParam
        $sourceRepoUrl = Get-GitRepositoryUrl -repoPath $RepoPath @verboseParam

        if ($vmrRepoUrl) {
            Write-Host "VMR repository URL: $vmrRepoUrl" -ForegroundColor Green
        } else {
            Write-Host "Could not determine VMR repository URL" -ForegroundColor Yellow
        }

        if ($sourceRepoUrl) {
            Write-Host "Source repository URL: $sourceRepoUrl" -ForegroundColor Green
        } else {
            Write-Host "Could not determine source repository URL" -ForegroundColor Yellow
        }
    } else {
        # In sample mode, use placeholder URLs
        $vmrRepoUrl = "https://github.com/dotnet/dotnet"
        $sourceRepoUrl = "https://github.com/dotnet/aspnetcore"
        Write-Host "Using sample repository URLs for clickable links" -ForegroundColor Yellow
    }

    # Create Mermaid diagram with cross-repository connections
    $diagramParams = @{
        vmrCommits = $vmrCommits
        repoCommits = $repoCommits
        vmrName = "VMR"
        repoName = (Split-Path -Path $RepoPath -Leaf)
        crossRepoConnections = $crossRepoConnections
        collapseThreshold = $CollapseThreshold
        vmrRepoUrl = $vmrRepoUrl
        repoUrl = $sourceRepoUrl
    }

    # Add the NoCollapse parameter if it was provided
    if ($NoCollapse) {
        $diagramParams.NoCollapse = $true
    }

    # Add the Verbose parameter if it was provided
    if ($VerboseScript) {
        $diagramParams.Verbose = $true
    }

    $diagramText = Create-MermaidDiagram @diagramParams

    # Determine output file path
    if (-not $OutputPath) {
        $outputPath = Join-Path -Path $PSScriptRoot -ChildPath "git-commit-diagram.mmd"
    } else {
        $outputPath = $OutputPath
    }    # Add summary information to the diagram
    $diagramSummary = "%%% Git Commit Diagram - Generated $(Get-Date)`n"
    $diagramSummary += "%%% Repositories: vmr ($VmrPath) and repo ($RepoPath)`n"
    $diagramSummary += "%%% Repository URLs: vmr ($vmrRepoUrl) and repo ($sourceRepoUrl)`n"
    $diagramSummary += "%%% Total Commits: $($vmrCommits.Count) vmr commits, $($repoCommits.Count) repo commits`n"
    $diagramSummary += "%%% Cross-Repository Connections: $($crossRepoConnections.Count) connections found`n"
    $diagramSummary += "%%% Collapse Threshold: $CollapseThreshold (NoCollapse: $NoCollapse)`n"
    $diagramSummary += "%%% Note: Commit nodes are clickable and link to the repository`n"

    # Use a consistent prefix and add the flowchart TD immediately after the summary
    $completeText = $diagramSummary + $diagramText

    # Save the diagram to a file with explanation
    Set-Content -Path $outputPath -Value $completeText
    Write-Host "Mermaid diagram saved to: $outputPath" -ForegroundColor Green
}
catch {
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
}
