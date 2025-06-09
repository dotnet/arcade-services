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
    [Parameter(Mandatory=$false, HelpMessage="Open browser with the generated GraphViz diagram")]
    [switch]$OpenInBrowser,
    [Parameter(Mandatory=$false, HelpMessage="Path to output the GraphViz diagram file")]
    [string]$OutputPath = "",
    [Parameter(Mandatory=$false, HelpMessage="Enable verbose output")]
    [Alias("v")]
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

# This script loads Git commits from specified repositories and generates a GraphViz diagram
# The diagram shows commits as nodes in columns with parent-child relationships and cross-repo connections
#
# The diagram will show connections between repos based on Source tag references in Version.Details.xml
# Collapsed commits are displayed as a single node showing multiple commits in a range
#
# Features:
# - Automatically extracts repository URLs from Git remotes
# - Creates clickable nodes that link to the original repository commit or compare view
# - For single commits, the link leads to: [REPO_URL]/commit/[SHA]
# - For collapsed ranges, the link leads to: [REPO_URL]/compare/[LAST_SHA]...[FIRST_SHA]
# - Renders as a GraphViz with proper node alignment and styling

# Function to create GraphViz notation from commits
function Create-GraphVizDiagram {
    param (
        [array]$vmrCommits,
        [array]$repoCommits,
        [string]$vmrName,
        [string]$repoName,
        [array]$crossRepoConnections = @(),
        [int]$collapseThreshold = 2,  # Collapse runs longer than this threshold
        [switch]$NoCollapse,          # Disable collapsing feature entirely
        [string]$vmrRepoUrl = "",     # Repository URL for VMR for clickable links
        [string]$repoUrl = "",        # Repository URL for source repo for clickable links
        [switch]$Verbose
    )

    # Start building the GraphViz diagram string

    # Add summary information to the diagram as comments
    $diagram =  "// Git Commit Diagram - Generated $(Get-Date)`n"
    $diagram += "// Repositories: vmr ($VmrPath) and repo ($RepoPath)`n"
    $diagram += "// Repository URLs: vmr ($vmrRepoUrl) and repo ($sourceRepoUrl)`n"
    $diagram += "// Total Commits: $($vmrCommits.Count) vmr commits, $($repoCommits.Count) repo commits`n"
    $diagram += "// Cross-Repository Connections: $($crossRepoConnections.Count) connections found`n"
    $diagram += "// Collapse Threshold: $CollapseThreshold (NoCollapse: $NoCollapse)`n"
    $diagram += "// Note: Commit nodes are clickable and link to the repository`n`n"    # Use a consistent prefix with the GraphViz diagram
    $diagram += "digraph G {`n"
    $diagram += "  rankdir=TB;  // top to bottom flow overall`n"
    $diagram += "  node [shape=box, style=filled, fillcolor=white, fontcolor=blue, fontname=`"Arial`", fontsize=10];`n`n"

    # First identify all commits involved in cross-repo references
    # It's important to mark both source and target commits in both repos as referenced
    # This ensures they won't be collapsed, which could break cross-repo connections
    $referencedVmrCommits = @{}
    $referencedRepoCommits = @{}

    # First pass: identify all commits involved in any type of cross-repo connection
    foreach ($connection in $crossRepoConnections) {
        $referencedVmrCommits[$connection.VMRCommitSHA] = $true  # Mark VMR commit as referenced
        $referencedRepoCommits[$connection.RepoCommitSHA] = $true # Mark repo commit as referenced

        if ($Verbose) {
            Write-Host "  Marking VMR commit $($connection.VMRCommitSHA) as referenced"
            Write-Host "  Marking repo commit $($connection.RepoCommitSHA) as referenced"
        }
    }

    # Process VMR commits - identify collapsible ranges
    $vmrCollapsibleRanges = @()
    $currentRange = @()
    $lastSHA = ""
    $rangeIndex = 0

    # Skip collapsing if NoCollapse switch is set or threshold is 0
    $skipCollapse = $NoCollapse -or $collapseThreshold -le 0

    # Find consecutive ranges without cross-repo connections
    if (-not $skipCollapse) {
        # Start counting consecutive commits that aren't referenced in cross-repo connections
        $consRange = @()

        for ($i = 0; $i -lt $vmrCommits.Count; $i++) {
            $commit = $vmrCommits[$i]
            $isReferenced = $referencedVmrCommits.ContainsKey($commit.CommitSHA)

            if ($isReferenced) {
                # This commit is referenced by another repo, so end the current range
                if ($consRange.Count -ge $collapseThreshold) {
                    if ($Verbose) {
                        Write-Host "  Found VMR collapsible range: $($consRange[0].ShortSHA)..$($consRange[-1].ShortSHA) ($($consRange.Count) commits)" -ForegroundColor Green
                    }
                    $vmrCollapsibleRanges += ,@($consRange)
                }
                $consRange = @()
            } else {
                # Not referenced, add to the current range
                $consRange += $commit
            }
        }

        # Check the last range
        if ($consRange.Count -ge $collapseThreshold) {
            if ($Verbose) {
                Write-Host "  Found VMR collapsible range: $($consRange[0].ShortSHA)..$($consRange[-1].ShortSHA) ($($consRange.Count) commits)" -ForegroundColor Green
            }
            $vmrCollapsibleRanges += ,@($consRange)
        }

        if ($Verbose) {
            Write-Host "Found $($vmrCollapsibleRanges.Count) collapsible ranges in VMR commits" -ForegroundColor Cyan
        }
    }

    # Process repo commits - identify collapsible ranges
    $repoCollapsibleRanges = @()
    $currentRange = @()
    $lastSHA = ""
    $rangeIndex = 0

    # Find consecutive ranges without cross-repo connections
    if (-not $skipCollapse) {
        # Start counting consecutive commits that aren't referenced in cross-repo connections
        $consRange = @()

        for ($i = 0; $i -lt $repoCommits.Count; $i++) {
            $commit = $repoCommits[$i]
            $isReferenced = $referencedRepoCommits.ContainsKey($commit.CommitSHA)

            if ($isReferenced) {
                # This commit is referenced by VMR repo, so end the current range
                if ($consRange.Count -ge $collapseThreshold) {
                    if ($Verbose) {
                        Write-Host "  Found repo collapsible range: $($consRange[0].ShortSHA)..$($consRange[-1].ShortSHA) ($($consRange.Count) commits)" -ForegroundColor Green
                    }
                    $repoCollapsibleRanges += ,@($consRange)
                }
                $consRange = @()
            } else {
                # Not referenced, add to the current range
                $consRange += $commit
            }
        }

        # Check the last range
        if ($consRange.Count -ge $collapseThreshold) {
            if ($Verbose) {
                Write-Host "  Found repo collapsible range: $($consRange[0].ShortSHA)..$($consRange[-1].ShortSHA) ($($consRange.Count) commits)" -ForegroundColor Green
            }
            $repoCollapsibleRanges += ,@($consRange)
        }

        if ($Verbose) {
            Write-Host "Found $($repoCollapsibleRanges.Count) collapsible ranges in repo commits" -ForegroundColor Cyan
        }
    }

    # Counter for indexing links
    $crossRepoLinkIndex = 0

    # Track collapsed nodes to know which SHAs to replace in the graph
    $collapsedNodes = @{}

    # Add repository header node
    $diagram += "  // Left column nodes for $vmrName repository with SHA labels and URLs`n"
    $diagram += "  $($vmrName -replace '/','_') [label=`"$vmrName`""
    if ($vmrRepoUrl) {
        $diagram += ", URL=`"$vmrRepoUrl`""
    }
    $diagram += "];`n"

    # First pass - create regular nodes and collapsed nodes for VMR
    $vmrNodeIds = @()
    foreach ($commit in $vmrCommits) {
        # Check if this commit is part of a collapsible range
        $isCollapsed = $false
        $collapseId = ""

        foreach ($range in $vmrCollapsibleRanges) {
            $firstCommit = $range[0]
            $lastCommit = $range[-1]
            if ($range -contains $commit) {
                # If it's the first commit in the range, create a collapsed node
                if ($commit.CommitSHA -eq $firstCommit.CommitSHA) {
                    $rangeIdx = [array]::IndexOf($vmrCollapsibleRanges, $range)
                    $collapseId = "vmr_${($firstCommit.ShortSHA)}_${($lastCommit.ShortSHA)}_$rangeIdx"
                    # Create node for collapsed range with proper label showing the commit range
                    $label = "$($firstCommit.ShortSHA) ... $($lastCommit.ShortSHA)\n[$($range.Count) commits]"
                    if ($range.Count -eq 2) {
                        $label = "$($firstCommit.ShortSHA) .. $($lastCommit.ShortSHA)"
                    }

                    # Properly escape label for GraphViz format
                    $diagram += "  $collapseId [label=`"$label`""

                    # For ranges, use the compare URL pattern if repo URL is provided
                    if ($vmrRepoUrl) {
                        $compareUrl = "$vmrRepoUrl/compare/$($lastCommit.CommitSHA)...$($firstCommit.CommitSHA)"
                        $diagram += ", URL=`"$compareUrl`", target=`"_blank`", fontcolor=`"blue`", style=`"filled, bold`""
                    }
                    $diagram += "];`n"
                    $vmrNodeIds += $collapseId

                    # Save the collapsed node mapping for all commits in the range
                    foreach ($rangeCommit in $range) {
                        $collapsedNodes[$rangeCommit.CommitSHA] = $collapseId
                    }
                }
                $isCollapsed = $true
                break
            }
        }

        # If not collapsed, create a regular node
        if (-not $isCollapsed) {
            $shortSHA = $commit.ShortSHA
            $nodeId = "vmr___$shortSHA"  # Prefix with vmr___ to ensure valid GraphViz identifier
            $vmrNodeIds += $nodeId

            $diagram += "  $nodeId [label=`"$shortSHA`""

            # Create single commit link if repo URL is provided
            if ($vmrRepoUrl) {
                $commitUrl = "$vmrRepoUrl/commit/$($commit.CommitSHA)"
                $diagram += ", URL=`"$commitUrl`", target=`"_blank`", fontcolor=`"blue`", style=`"filled, bold`""
            }
            $diagram += "];`n"
        }
    }

    # Connect VMR nodes in a vertical chain
    $diagram += "`n  // Connect VMR nodes in a vertical chain`n"
    if ($vmrNodeIds.Count -gt 0) {
        for ($i = ($vmrNodeIds.Count - 1); $i -gt 0; $i--) {
            $diagram += "  $($vmrNodeIds[$i-1]) -> $($vmrNodeIds[$i]) [arrowhead=none, color=black];`n"
        }

        $diagram += "  $($vmrName -replace '/','_') -> $($vmrNodeIds[0]) [arrowhead=none, color=black];`n"
    }

    # Clear collapsed nodes for repo commits
    $collapsedNodes = @{}

    # Add repo header node
    $diagram += "`n  // Right column nodes for $repoName repository with SHA labels and URLs`n"
    $diagram += "  $($repoName -replace '/','_') [label=`"$repoName`""
    if ($repoUrl) {
        $diagram += ", URL=`"$repoUrl`""
    }
    $diagram += "];`n"

    # First pass - create regular nodes and collapsed nodes for repo
    $repoNodeIds = @()
    foreach ($commit in $repoCommits) {
        # Check if this commit is part of a collapsible range
        $isCollapsed = $false
        $collapseId = ""

        foreach ($range in $repoCollapsibleRanges) {
            $firstCommit = $range[0]
            $lastCommit = $range[-1]
            if ($range -contains $commit) {
                # If it's the first commit in the range, create a collapsed node
                if ($commit.CommitSHA -eq $firstCommit.CommitSHA) {
                    $rangeIdx = [array]::IndexOf($repoCollapsibleRanges, $range)
                    $collapseId = "repo_${($firstCommit.ShortSHA)}_${($lastCommit.ShortSHA)}_$rangeIdx"
                    # Create node for collapsed range with proper label showing the commit range
                    $label = "$($firstCommit.ShortSHA) ... $($lastCommit.ShortSHA)\n[$($range.Count) commits]"
                    if ($range.Count -eq 2) {
                        $label = "$($firstCommit.ShortSHA) .. $($lastCommit.ShortSHA)"
                    }

                    # Properly escape label for GraphViz format
                    $diagram += "  $collapseId [label=`"$label`""

                    # For ranges, use the compare URL pattern if repo URL is provided
                    if ($repoUrl) {
                        $compareUrl = "$repoUrl/compare/$($lastCommit.CommitSHA)...$($firstCommit.CommitSHA)"
                        $diagram += ", URL=`"$compareUrl`", target=`"_blank`", fontcolor=`"blue`", style=`"filled, bold`""
                    }
                    $diagram += "];`n"
                    $repoNodeIds += $collapseId

                    # Save the collapsed node mapping for all commits in the range
                    foreach ($rangeCommit in $range) {
                        $collapsedNodes[$rangeCommit.CommitSHA] = $collapseId
                    }
                }
                $isCollapsed = $true
                break
            }
        }

        # If not collapsed, create a regular node
        if (-not $isCollapsed) {
            $shortSHA = $commit.ShortSHA
            $nodeId = "repo___$shortSHA"  # Prefix with repo___ to ensure valid GraphViz identifier
            $repoNodeIds += $nodeId

            $diagram += "  $nodeId [label=`"$shortSHA`""

            # Create single commit link if repo URL is provided
            if ($repoUrl) {
                $commitUrl = "$repoUrl/commit/$($commit.CommitSHA)"
                $diagram += ", URL=`"$commitUrl`", target=`"_blank`", fontcolor=`"blue`", style=`"filled, bold`""
            }
            $diagram += "];`n"
        }
    }

    # Connect repo nodes in a vertical chain
    $diagram += "`n  // Connect repo nodes in a vertical chain`n"
    if ($repoNodeIds.Count -gt 0) {
        for ($i = ($repoNodeIds.Count - 1); $i -gt 0; $i--) {
            $diagram += "  $($repoNodeIds[$i-1]) -> $($repoNodeIds[$i]) [arrowhead=none, color=black];`n"
        }

        $diagram += "  $($repoName -replace '/','_') -> $($repoNodeIds[0]) [arrowhead=none, color=black];`n"
    }

    # Add cross-repository connections
    if ($crossRepoConnections -and $crossRepoConnections.Count -gt 0) {
        $diagram += "`n  // Cross-repository connections with clickable URLs and colored arrows`n"

        # Keep track of which connections we've already created to avoid duplicates
        $processedConnections = @{}

        foreach ($connection in $crossRepoConnections) {
            $isBackflow = $connection.ConnectionType -eq "BackFlow"
            $linkColor = if ($isBackflow) { "red" } else { "green" }

            # Get the node IDs accounting for collapsed nodes
            $sourceNodeId = ""
            $targetNodeId = ""
            if ($isBackflow) {
                # VMR to repo connection
                $sourceId = $connection.VMRCommitSHA
                $targetId = $connection.RepoCommitSHA
                  # Check if source node (from VMR) is part of a collapsed range
                if ($connection.CommitSHA) {
                    # First find the exact commit in our commits array
                    $vmrNode = $vmrCommits | Where-Object { $_.CommitSHA -eq $sourceId } | Select-Object -First 1
                    if ($vmrNode) {
                        $shortSHA = $sourceId.Substring(0, 7)
                        $sourceId = "vmr___$shortSHA"  # Use consistent naming format with vmr___ prefix

                        # Then check if it belongs to any collapsed range
                        foreach ($range in $vmrCollapsibleRanges) {
                            if ($range | Where-Object { $_.CommitSHA -eq $vmrNode.CommitSHA }) {
                                $rangeIdx = [array]::IndexOf($vmrCollapsibleRanges, $range)
                                $sourceId = "vmr_$($range[0].ShortSHA)_$($range[-1].ShortSHA)_$rangeIdx"
                                if ($Verbose) {
                                    Write-Host "  Found collapsed node for VMR commit $shortSHA -> $sourceId" -ForegroundColor Magenta
                                }
                                break
                            }
                        }
                    }
                }

                # Check if target node (from repo) is part of a collapsed range
                if ($connection.RepoCommitSHA) {
                    # First find the exact commit in our commits array
                    $repoNode = $repoCommits | Where-Object { $_.CommitSHA -eq $connection.RepoCommitSHA } | Select-Object -First 1
                    if ($repoNode) {
                        $shortSHA = $repoNode.CommitSHA.Substring(0, 7)
                        $targetId = "repo___$shortSHA"  # Use consistent naming format with repo___ prefix

                        # Then check if it belongs to any collapsed range
                        foreach ($range in $repoCollapsibleRanges) {
                            if ($range | Where-Object { $_.CommitSHA -eq $repoNode.CommitSHA }) {
                                $rangeIdx = [array]::IndexOf($repoCollapsibleRanges, $range)
                                $targetId = "repo_$($range[0].ShortSHA)_$($range[-1].ShortSHA)_$rangeIdx"
                                if ($Verbose) {
                                    Write-Host "  Found collapsed node for repo commit $shortSHA -> $targetId" -ForegroundColor Magenta
                                }
                                break
                            }
                        }
                    }
                }

                # Only create the connection if both nodes exist in our graph
                if ($vmrNodeIds -contains $sourceId -and $repoNodeIds -contains $targetId) {
                    $linkId = "link_back_${sourceId}_to_${targetId}"

                    # Check if we've already processed this connection
                    if (-not $processedConnections.ContainsKey($linkId)) {
                        $processedConnections[$linkId] = $true

                        $linkUrl = ""
                        if ($vmrRepoUrl) {
                            $linkUrl = "$vmrRepoUrl/commit/${connection.VMRCommitSHA}"
                        }

                        $diagram += "  $sourceId -> $targetId [penwidth=3, constraint=false, color=$linkColor"
                        if ($linkUrl) {
                            $diagram += ", URL=`"$linkUrl`", target=`"_blank`""
                        }
                        $diagram += "];`n"
                    }
                }
            } else {
                # Repo to VMR connection
                $sourceId = $connection.RepoCommitSHA
                $targetId = $connection.VMRCommitSHA

                # Check if source node (from repo) is part of a collapsed range
                # First find the exact commit in our commits array
                $repoNode = $repoCommits | Where-Object { $_.CommitSHA -eq $sourceId } | Select-Object -First 1
                if ($repoNode) {
                    $shortSHA = $repoNode.ShortSHA
                    $sourceId = "repo___$shortSHA"  # Use consistent naming format with repo___ prefix

                    # Then check if it belongs to any collapsed range
                    foreach ($range in $repoCollapsibleRanges) {
                        if ($range | Where-Object { $_.CommitSHA -eq $repoNode.CommitSHA }) {
                            $rangeIdx = [array]::IndexOf($repoCollapsibleRanges, $range)
                            $sourceId = "repo_$($range[0].ShortSHA)_$($range[-1].ShortSHA)_$rangeIdx"
                            if ($Verbose) {
                                Write-Host "  Found collapsed node for repo commit $shortSHA -> $sourceId" -ForegroundColor Cyan
                            }
                            break
                        }
                    }
                }

                # First find the exact commit in our commits array
                $vmrNode = $vmrCommits | Where-Object { $_.CommitSHA -eq $connection.VMRCommitSHA } | Select-Object -First 1
                if ($vmrNode) {
                    $shortSHA = $vmrNode.ShortSHA
                    $targetId = "vmr___$shortSHA"  # Use consistent naming format with vmr___ prefix

                    # Then check if it belongs to any collapsed range
                    foreach ($range in $vmrCollapsibleRanges) {
                        if ($range | Where-Object { $_.CommitSHA -eq $vmrNode.CommitSHA }) {
                            $rangeIdx = [array]::IndexOf($vmrCollapsibleRanges, $range)
                            $targetId = "vmr_$($range[0].ShortSHA)_$($range[-1].ShortSHA)_$rangeIdx"
                            if ($Verbose) {
                                Write-Host "  Found collapsed node for VMR commit $shortSHA -> $targetId" -ForegroundColor Cyan
                            }
                            break
                        }
                    }
                }

                # Only create the connection if both nodes exist in our graph
                if ($repoNodeIds -contains $sourceId -and $vmrNodeIds -contains $targetId) {
                    $linkId = "link_forward_${sourceId}_to_${targetId}"

                    # Check if we've already processed this connection
                    if (-not $processedConnections.ContainsKey($linkId)) {
                        $processedConnections[$linkId] = $true

                        $linkUrl = ""
                        if ($repoUrl) {
                            $linkUrl = "$repoUrl/commit/${connection.RepoCommitSHA}"
                        }

                        $diagram += "  $sourceId -> $targetId [penwidth=3, constraint=false, color=$linkColor"
                        if ($linkUrl) {
                            $diagram += ", URL=`"$linkUrl`", target=`"_blank`""
                        }
                        $diagram += "];`n"
                    }
                }
            }

            $crossRepoLinkIndex++
        }
    }

    # Close the diagram
    $diagram += "}"

    return $diagram
}

try {
    Write-Host "Generating Git commit GraphViz diagram..." -ForegroundColor Green

    $vmrCommits = @()
    $repoCommits = @()

    # Create a parameter hashtable for verbose if needed
    $verboseParam = @{}
    if ($VerboseScript) {
        $verboseParam.Verbose = $true
        Write-Verbose "Verbose mode enabled"
    }

    Write-Host "Loading commits from repo ($RepoPath)..." -ForegroundColor Yellow
    $repoCommits = Get-GitCommits -repoPath $RepoPath -count $Depth @verboseParam
    Write-Host "Loaded $($repoCommits.Count) commits from source repository" -ForegroundColor Green

    # Determine the optimal VMR depth by analyzing the loaded repo commits for referenced commits
    $vmrDepth = Get-MaxVmrDepth -repoPath $RepoPath -vmrPath $VmrPath -repoCommits $repoCommits -defaultDepth $Depth -minDepth 10 -maxDepth 500 @verboseParam

    Write-Host "Loading commits from VMR ($VmrPath)..." -ForegroundColor Yellow
    $vmrCommits = Get-GitCommits -repoPath $VmrPath -count $vmrDepth @verboseParam
    Write-Host "Loaded $($vmrCommits.Count) commits from VMR repository" -ForegroundColor Green


    # Initialize cross-repository connections array
    $crossRepoConnections = @()

    # Create a parameter hashtable for verbose if needed
    $verboseParam = @{}
    if ($VerboseScript) {
        $verboseParam.Verbose = $true
        Write-Verbose "Verbose mode enabled for flow detection."
    }

    $mappingName = Get-SourceTagMappingFromVersionDetails -repoPath $RepoPath @verboseParam

    Write-Host "Finding forward flow connections (repo to VMR)..." -ForegroundColor Yellow
    # Assumes Find-ForwardFlows takes repoPath, vmrPath, repoCommits, vmrCommits
    # and returns objects with CommitSHA, SourceSHA, ShortSHA, ShortSourceSHA
    $forwardFlows = Find-ForwardFlows -vmrPath $VmrPath -repoMapping $mappingName -depth $vmrDepth @verboseParam
    Write-Host "Found $($forwardFlows.Count) forward flow connections." -ForegroundColor Green

    foreach ($flow in $forwardFlows) {
        $flow | Add-Member -NotePropertyName "ConnectionType" -NotePropertyValue "ForwardFlow" -Force
        $crossRepoConnections += $flow
        if ($VerboseScript) {
            Write-Verbose "  + Added forward flow: repo $($flow.ShortSHA) -> vmr $($flow.ShortSourceSHA)"
        }
    }

    Write-Host "Finding backflow connections (VMR to repo)..." -ForegroundColor Yellow
    # Assumes Find-Backflows takes repoPath, vmrPath, repoCommits, vmrCommits
    # and returns objects with CommitSHA, SourceSHA, ShortSHA, ShortSourceSHA
    $backflows = Find-Backflows -repoPath $RepoPath -depth $Depth @verboseParam
    Write-Host "Found $($backflows.Count) backflow connections." -ForegroundColor Green

    foreach ($flow in $backflows) {
        $flow | Add-Member -NotePropertyName "ConnectionType" -NotePropertyValue "BackFlow" -Force
        $crossRepoConnections += $flow
        if ($VerboseScript) {
            Write-Verbose "  + Added backflow: vmr $($flow.ShortSHA) -> repo $($flow.ShortSourceSHA)"
        }
    }

    Write-Host "Found $($crossRepoConnections.Count) total cross-repository connections ($($forwardFlows.Count) forward, $($backflows.Count) backflow)." -ForegroundColor Green

    # Get repository URLs for clickable links
    $vmrRepoUrl = ""
    $sourceRepoUrl = ""

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

    $diagramText = Create-GraphVizDiagram @diagramParams
    
    if ($OpenInBrowser) {
        # URL encode the diagram text for inclusion in the URL
        Add-Type -AssemblyName System.Web
        # Use UrlEncoding but replace '+' with '%20' for better compatibility
        $encodedDiagram = [System.Web.HttpUtility]::UrlEncode($diagramText).Replace("+", "%20")

        # Generate the edotor.net URL
        $edotorUrl = "https://edotor.net/?engine=dot#$encodedDiagram"
        Start-Process $edotorUrl
        Write-Host "Opening diagram in browser..." -ForegroundColor Green
        exit 0
    }

    # Determine output file path
    if (-not $OutputPath) {
        Write-Host "GraphViz diagram: " -ForegroundColor Cyan
        Write-Host $diagramText -ForegroundColor White
        exit 0
    }

    # Save the diagram to a file with explanation
    Set-Content -Path $OutputPath -Value $diagramText
    Write-Host "GraphViz diagram saved to: $OutputPath" -ForegroundColor Green
}
catch {
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
}

