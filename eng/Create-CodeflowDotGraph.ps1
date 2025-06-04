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
    [Parameter(Mandatory=$false, HelpMessage="Path to output the DOT graph diagram file")]
    [string]$OutputPath = ""
)

# This script loads Git commits from specified repositories and generates a DOT graph diagram
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
# - Renders as a DOT graph with proper node alignment and styling

# Function to get last N commits from a Git repository
function Get-GitCommits {
    param (
        [string]$repoPath,
        [int]$count = 30,
        [switch]$Verbose
    )

    # Get the commit history in format: <sha> <parent-sha>
    # This gives us both the commit SHA and its parent commit SHA
    # We use the %P format to get all parent SHAs (important for merge commits)
    $commits = git -C $repoPath log -n $count --format="%H %P" main

    # Convert the raw git log output into structured objects
    $commitObjects = @()
    foreach ($commit in $commits) {
        $parts = $commit.Split(' ')
        if ($parts.Count -ge 2) {
            # Store all parent SHAs in an array
            $parentShas = $parts[1..($parts.Count-1)]

            $commitObjects += [PSCustomObject]@{
                CommitSHA = $parts[0]
                ParentSHA = $parts[1]               # First parent (for backward compatibility)
                ParentSHAs = $parentShas           # All parents (for proper branch visualization)
                ShortSHA = $parts[0].Substring(0, 7)  # Truncate to 7 chars
                ShortParentSHA = $parts[1].Substring(0, 7)  # Truncate to 7 chars
            }
        }
    }

    # Note: Git log returns commits in reverse chronological order (newest first)
    # Our collapsing algorithm accounts for this

    return $commitObjects
}

# Function to create DOT graph notation from commits
function Create-DotDiagram {
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

    # Start building the DOT diagram string
    $diagram = "digraph G {`n"
    $diagram += "  rankdir=TB;  // top to bottom flow overall`n"
    $diagram += "  node [shape=box, style=filled, fillcolor=white, fontcolor=blue, fontname=`"Arial`", fontsize=10];`n`n"

    # First identify all commits involved in cross-repo references
    # It's important to mark both source and target commits in both repos as referenced
    # This ensures they won't be collapsed, which could break cross-repo connections
    $referencedVmrCommits = @{}
    $referencedRepoCommits = @{}

    # First pass: identify all commits involved in any type of cross-repo connection
    foreach ($connection in $crossRepoConnections) {
        $isBackflow = $connection.ConnectionType -eq "BackFlow"

        if ($isBackflow) {
            # For backflow: VMR commit (source) references repo commit (target)
            $referencedVmrCommits[$connection.CommitSHA] = $true  # Mark VMR commit as referenced
            $referencedRepoCommits[$connection.SourceSHA] = $true # Mark repo commit as referenced

            if ($Verbose) {
                Write-Host "  Marking VMR commit $($connection.ShortSHA) as referenced (backflow source)" -ForegroundColor Magenta
                Write-Host "  Marking repo commit $($connection.ShortSourceSHA) as referenced (backflow target)" -ForegroundColor Magenta
            }
        } else {
            # For forward flow: repo commit (source) references VMR commit (target)
            $referencedVmrCommits[$connection.SourceSHA] = $true  # Mark VMR commit as referenced
            $referencedRepoCommits[$connection.CommitSHA] = $true # Mark repo commit as referenced

            if ($Verbose) {
                Write-Host "  Marking VMR commit $($connection.ShortSourceSHA) as referenced (forward flow target)" -ForegroundColor Green
                Write-Host "  Marking repo commit $($connection.ShortSHA) as referenced (forward flow source)" -ForegroundColor Green
            }
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
                    
                    # Properly escape label for DOT format
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
            $nodeId = "vmr___$shortSHA"  # Prefix with vmr___ to ensure valid DOT identifier
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
        $diagram += "  $($vmrNodeIds[0]) -> $($vmrName -replace '/','_') [arrowhead=none, color=black];`n"
        
        for ($i = 0; $i -lt ($vmrNodeIds.Count - 1); $i++) {
            $diagram += "  $($vmrNodeIds[$i+1]) -> $($vmrNodeIds[$i]) [arrowhead=none, color=black];`n"
        }
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
                    
                    # Properly escape label for DOT format
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
            $nodeId = "repo___$shortSHA"  # Prefix with repo___ to ensure valid DOT identifier
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
        $diagram += "  $($repoNodeIds[0]) -> $($repoName -replace '/','_') [arrowhead=none, color=black];`n"
        
        for ($i = 0; $i -lt ($repoNodeIds.Count - 1); $i++) {
            $diagram += "  $($repoNodeIds[$i+1]) -> $($repoNodeIds[$i]) [arrowhead=none, color=black];`n"
        }
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
                $sourceId = $connection.ShortSHA
                $targetId = $connection.ShortSourceSHA
                  # Check if source node (from VMR) is part of a collapsed range
                if ($connection.CommitSHA) {
                    # First find the exact commit in our commits array
                    $vmrNode = $vmrCommits | Where-Object { $_.CommitSHA -eq $connection.CommitSHA } | Select-Object -First 1
                    if ($vmrNode) {
                        $shortSHA = $vmrNode.ShortSHA
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
                }                # Check if target node (from repo) is part of a collapsed range
                if ($connection.SourceSHA) {
                    # First find the exact commit in our commits array
                    $repoNode = $repoCommits | Where-Object { $_.CommitSHA -eq $connection.SourceSHA } | Select-Object -First 1
                    if ($repoNode) {
                        $shortSHA = $repoNode.ShortSHA
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
                            $linkUrl = "$vmrRepoUrl/commit/$($connection.CommitSHA)"
                        }
                        
                        $diagram += "  $targetId -> $sourceId [penwidth=3, constraint=false, color=$linkColor"
                        if ($linkUrl) {
                            $diagram += ", URL=`"$linkUrl`", target=`"_blank`""
                        }
                        $diagram += "];`n"
                    }
                }
            } else {
                # Repo to VMR connection
                $sourceId = $connection.ShortSHA
                $targetId = $connection.ShortSourceSHA                # Check if source node (from repo) is part of a collapsed range
                if ($connection.CommitSHA) {
                    # First find the exact commit in our commits array
                    $repoNode = $repoCommits | Where-Object { $_.CommitSHA -eq $connection.CommitSHA } | Select-Object -First 1
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
                }                # Check if target node (from VMR) is part of a collapsed range
                if ($connection.SourceSHA) {
                    # First find the exact commit in our commits array
                    $vmrNode = $vmrCommits | Where-Object { $_.CommitSHA -eq $connection.SourceSHA } | Select-Object -First 1
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
                }
                
                # Only create the connection if both nodes exist in our graph
                if ($repoNodeIds -contains $sourceId -and $vmrNodeIds -contains $targetId) {
                    $linkId = "link_forward_${sourceId}_to_${targetId}"
                    
                    # Check if we've already processed this connection
                    if (-not $processedConnections.ContainsKey($linkId)) {
                        $processedConnections[$linkId] = $true
                        
                        $linkUrl = ""
                        if ($repoUrl) {
                            $linkUrl = "$repoUrl/commit/$($connection.CommitSHA)"
                        }
                        
                        $diagram += "  $targetId -> $sourceId [penwidth=3, constraint=false, color=$linkColor"
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

# Function to get the Git repository URL
function Get-GitRepositoryUrl {
    param (
        [string]$repoPath,
        [switch]$Verbose
    )

    try {
        # Try to get the origin remote URL
        $remoteUrl = git -C $repoPath config --get remote.origin.url 2>$null

        # If origin doesn't exist, try to get any remote URL
        if (-not $remoteUrl) {
            $remotes = git -C $repoPath remote 2>$null
            if ($remotes) {
                $firstRemote = $remotes[0]
                $remoteUrl = git -C $repoPath config --get remote.$firstRemote.url 2>$null
            }
        }

        if ($remoteUrl) {
            # Format the URL for browser access (handle both SSH and HTTPS formats)
            if ($remoteUrl -match "git@github\.com:(.*?)\.git$") {
                $normalizedUrl = "https://github.com/$($matches[1])"
            } elseif ($remoteUrl -match "https://github\.com/(.*?)\.git$") {
                $normalizedUrl = "https://github.com/$($matches[1])"
            } elseif ($remoteUrl -match "git@dev\.azure\.com:(.*)") {
                # Azure DevOps SSH format
                $normalizedUrl = "https://dev.azure.com/$($matches[1].Replace('/', '/_git/'))"
                $normalizedUrl = $normalizedUrl -replace '\.git$', ''
            } elseif ($remoteUrl -match "https://.*?@dev\.azure\.com/(.*)") {
                # Azure DevOps HTTPS format
                $normalizedUrl = "https://dev.azure.com/$($matches[1])"
                $normalizedUrl = $normalizedUrl -replace '\.git$', ''
            } else {
                # Just remove trailing .git for other URLs
                $normalizedUrl = $remoteUrl -replace '\.git$', ''
            }

            if ($Verbose) {
                Write-Host "Extracted repository URL: $normalizedUrl from $remoteUrl" -ForegroundColor Green
            }

            return $normalizedUrl
        } else {
            if ($Verbose) {
                Write-Host "No remote URL found for repository at $repoPath" -ForegroundColor Yellow
            }
            return ""
        }
    } catch {
        if ($Verbose) {
            Write-Host "Error getting repository URL: $_" -ForegroundColor Red
        }
        return ""
    }
}

# Function to get commits that changed a specific file
function Get-CommitsThatChangedFile {
    param (
        [string]$repoPath,
        [string]$filePath,
        [int]$count = 50,
        [switch]$Verbose
    )

    $commits = git -C $repoPath log -n $count --format="%H" -- $filePath
    return $commits
}

# Function to get repository information from source-manifest.json
function Get-SourceManifestRepositoryInfo {
    param (
        [string]$vmrPath,
        [string]$commitSHA,
        [string]$repoMapping,
        [string]$filePath = "src/source-manifest.json",
        [switch]$Verbose
    )

    # Get the object ID (blob) for the file in this commit
    $blobId = git -C $vmrPath rev-parse "$commitSHA`:$filePath" 2>$null

    # Check if the file exists in this commit
    if (-not $blobId) {
        if ($Verbose) {
            Write-Host "File $filePath doesn't exist in commit $commitSHA" -ForegroundColor DarkYellow
        }
        return $null
    }

    # Use git cat-file to get the content directly without creating a temp file
    $fileContent = git -C $vmrPath cat-file -p $blobId

    $result = $null

    # Try to parse the JSON
    try {
        $manifest = $fileContent | ConvertFrom-Json

        # Look for the repository with the matching path
        foreach ($repo in $manifest.repositories) {
            if ($repo.path -eq $repoMapping) {
                if ($Verbose) {
                    Write-Host "  Found repository with path '$repoMapping' in manifest: commit $($repo.commitSha)" -ForegroundColor Green
                }

                $result = [PSCustomObject]@{
                    CommitSHA = $commitSHA           # VMR commit SHA
                    ShortSHA = $commitSHA.Substring(0, 7)  # Truncated VMR SHA
                    SourceSHA = $repo.commitSha      # Referenced repo commit
                    ShortSourceSHA = $repo.commitSha.Substring(0, 7) # Truncated repo SHA
                    RepoPath = $repo.path            # Repository path in the VMR
                    RemoteUri = $repo.remoteUri      # Repository remote URI
                }
                break
            }
        }

        if ($result -eq $null -and $Verbose) {
            Write-Host "  No repository with path '$repoMapping' found in manifest" -ForegroundColor DarkYellow
        }
    }
    catch {
        if ($Verbose) {
            Write-Host "Error parsing source-manifest.json: $_" -ForegroundColor Red
        }
        return $null
    }

    return $result
}

# Function to extract the Source tag mapping from Version.Details.xml
function Get-SourceTagMappingFromVersionDetails {
    param (
        [string]$repoPath,
        [string]$filePath = "eng/Version.Details.xml",
        [switch]$Verbose
    )

    try {
        # Get the latest content of Version.Details.xml
        $fileContent = Get-Content -Path (Join-Path -Path $repoPath -ChildPath $filePath) -Raw -ErrorAction Stop

        # Try to parse the XML
        $xml = New-Object System.Xml.XmlDocument
        $xml.LoadXml($fileContent)

        # Look for the Source tag with Mapping attribute
        $sourceWithMapping = $xml.SelectSingleNode("//*[local-name()='Source' and @Mapping]")
        if ($sourceWithMapping) {
            $mapping = $sourceWithMapping.GetAttribute("Mapping")
            if ($Verbose) {
                Write-Host "Found Source tag with Mapping attribute: $mapping" -ForegroundColor Green
            }
            return $mapping
        }

        # If not found with direct attribute, try to find it in any format
        $source = $xml.SelectSingleNode("//*[local-name()='Source']")
        if ($source -and $source.HasAttribute("Mapping")) {
            $mapping = $source.GetAttribute("Mapping")
            if ($Verbose) {
                Write-Host "Found Source tag with Mapping attribute: $mapping" -ForegroundColor Green
            }
            return $mapping
        }

        # Try regex as fallback
        if ($fileContent -match '<Source[^>]*Mapping="([^"]+)"') {
            $mapping = $matches[1]
            if ($Verbose) {
                Write-Host "Found Source tag with Mapping using regex: $mapping" -ForegroundColor Green
            }
            return $mapping
        }

        if ($Verbose) {
            Write-Host "No Source tag with Mapping attribute found in $filePath" -ForegroundColor Yellow
        }

        # Return a default mapping as fallback
        return "default"
    }
    catch {
        if ($Verbose) {
            Write-Host "Error extracting mapping from $filePath`: $_" -ForegroundColor Red
        }
        # Return a default mapping as fallback
        return "default"
    }
}

# Function to extract the Source tag SHA from Version.Details.xml for a specific commit
function Get-SourceTagShaFromCommit {
    param (
        [string]$repoPath,
        [string]$commitSHA,
        [string]$filePath = "eng/Version.Details.xml",
        [switch]$Verbose
    )

    # Get the object ID (blob) for the file in this commit
    $blobId = git -C $repoPath rev-parse "$commitSHA`:$filePath" 2>$null

    # Check if the file exists in this commit
    if (-not $blobId) {
        if ($Verbose) {
            Write-Host "File $filePath doesn't exist in commit $commitSHA" -ForegroundColor DarkYellow
        }
        return $null
    }

    # Use git cat-file to get the content directly without creating a temp file
    $fileContent = git -C $repoPath cat-file -p $blobId

    $result = $null

    # Try to parse the XML - if it's valid XML, we can extract Source tag more reliably
    try {
        # Load the XML document directly from the content string
        $xml = New-Object System.Xml.XmlDocument

        # Load the XML content directly
        $xml.LoadXml($fileContent)

        # Try various XPath queries to find Source and Sha elements
        # Only return the first matching Source tag

        # 1. Try to find Source elements with Sha attribute
        $source = $xml.SelectSingleNode("//*[local-name()='Source' and @Sha]")
        if ($source) {
            $sourceSHA = $source.Sha
            if ($sourceSHA -match '^[0-9a-f]+$') {
                if ($Verbose) {
                    Write-Host "  Found Source with Sha attribute: $sourceSHA" -ForegroundColor Green
                }
                $result = [PSCustomObject]@{
                    CommitSHA = $commitSHA
                    ShortSHA = $commitSHA.Substring(0, 7)
                    SourceSHA = $sourceSHA
                    ShortSourceSHA = $sourceSHA.Substring(0, 7)
                }
            }
        }

        # If we didn't find a Source tag with Sha attribute, try other methods
        if ($result -eq $null) {
            # 2. Try to find Sha elements inside or near Source elements
            $sha = $xml.SelectSingleNode("//*[local-name()='Dependency']/*[local-name()='Source']/parent::*/*[local-name()='Sha']")
            if ($sha) {
                $sourceSHA = $sha.InnerText
                if ($sourceSHA -match '^[0-9a-f]+$') {
                    if ($Verbose) {
                        Write-Host "  Found Source with child Sha element: $sourceSHA" -ForegroundColor Green
                    }
                    $result = [PSCustomObject]@{
                        CommitSHA = $commitSHA
                        ShortSHA = $commitSHA.Substring(0, 7)
                        SourceSHA = $sourceSHA
                        ShortSourceSHA = $sourceSHA.Substring(0, 7)
                    }
                }
            }
        }

        # 3. Try a more general approach if still not found
        if ($result -eq $null) {
            $dependency = $xml.SelectSingleNode("//*[local-name()='Dependency']")
            if ($dependency) {
                $sourceNode = $dependency.SelectSingleNode("*[local-name()='Source']")
                $shaNode = $dependency.SelectSingleNode("*[local-name()='Sha']")

                if ($sourceNode -and $shaNode) {
                    $sourceSHA = $shaNode.InnerText
                    if ($sourceSHA -match '^[0-9a-f]+$') {
                        if ($Verbose) {
                            Write-Host "  Found Dependency with Source and Sha elements: $sourceSHA" -ForegroundColor Green
                        }
                        $result = [PSCustomObject]@{
                            CommitSHA = $commitSHA
                            ShortSHA = $commitSHA.Substring(0, 7)
                            SourceSHA = $sourceSHA
                            ShortSourceSHA = $sourceSHA.Substring(0, 7)
                        }
                    }
                }
            }
        }

        # 4. Try to find any Dependency with vmr repository if still not found
        if ($result -eq $null) {
            $dep = $xml.SelectSingleNode("//*[local-name()='Dependency' and contains(@Repository, 'dotnet')]")
            if ($dep) {
                $shaNode = $dep.SelectSingleNode("*[local-name()='Sha']")
                if ($shaNode) {
                    $sourceSHA = $shaNode.InnerText
                    if ($sourceSHA -match '^[0-9a-f]+$') {
                        if ($Verbose) {
                            Write-Host "  Found vmr Dependency with Sha: $sourceSHA" -ForegroundColor Green
                        }
                        $result = [PSCustomObject]@{
                            CommitSHA = $commitSHA
                            ShortSHA = $commitSHA.Substring(0, 7)
                            SourceSHA = $sourceSHA
                            ShortSourceSHA = $sourceSHA.Substring(0, 7)
                        }
                    }
                }
            }
        }
    }
    catch {
        if ($Verbose) {
            Write-Host "XML parsing failed: $_" -ForegroundColor Red
        }
        # If XML parsing fails, fall back to regex parsing
    }

    # If XML parsing didn't find anything or failed, use regex as fallback
    if ($result -eq $null) {
        if ($Verbose) {
            Write-Host "  Using regex fallback parsing" -ForegroundColor Yellow
        }

        $lines = $fileContent -split "`n"

        foreach ($line in $lines) {
            # Different versions of the XML might have different formats, so we need to match various patterns
            if ($line -match '<Source.*Sha="([0-9a-f]+)".*>' -or
                $line -match 'Sha="([0-9a-f]+)".*<Source' -or
                $line -match '<Source[^>]*>[^<]*<Sha>([0-9a-f]+)</Sha>' -or
                $line -match '<Sha>([0-9a-f]+)</Sha>.*<Source' -or
                $line -match '<Sha>([0-9a-f]+)</Sha>') {

                $sourceSHA = $matches[1]
                if ($Verbose) {
                    Write-Host "  Found SHA using regex: $sourceSHA" -ForegroundColor Green
                }
                $result = [PSCustomObject]@{
                    CommitSHA = $commitSHA
                    ShortSHA = $commitSHA.Substring(0, 7)
                    SourceSHA = $sourceSHA
                    ShortSourceSHA = $sourceSHA.Substring(0, 7)
                }
                # Break after finding the first match
                break
            }
        }
    }

    if ($result -eq $null -and $Verbose) {
        Write-Host "  No Source tag with SHA found in commit $($commitSHA.Substring(0, 7))" -ForegroundColor DarkYellow
    }

    return $result
}

# Function to find changes to a specific repository in source-manifest.json
function Find-SourceManifestChanges {
    param (
        [string]$vmrPath,
        [string]$repoMapping,
        [string]$filePath = "src/source-manifest.json",
        [int]$count = 200,
        [switch]$Verbose
    )

    # Get commits that changed the file
    try {
        $commitSHAs = Get-CommitsThatChangedFile -repoPath $vmrPath -filePath $filePath -count $count

        if (-not $commitSHAs -or $commitSHAs.Count -eq 0) {
            Write-Host "No commits found that changed file: $filePath" -ForegroundColor Yellow
            return @()
        }

        $manifestChanges = @()
        $previousCommitSha = $null

        # For each commit, extract repository info
        foreach ($commitSHA in $commitSHAs) {
            try {
                # Pass the Verbose switch if it was provided
                $verboseParam = @{}
                if ($Verbose) {
                    $verboseParam.Verbose = $true
                }

                $repoInfo = Get-SourceManifestRepositoryInfo -vmrPath $vmrPath -commitSHA $commitSHA -repoMapping $repoMapping -filePath $filePath @verboseParam

                if ($repoInfo) {
                    # Only include commits where the commit SHA changed from the previous commit
                    if ($repoInfo.SourceSHA -ne $previousCommitSha) {
                        if ($Verbose) {
                            Write-Host "  Source manifest changed in commit $($repoInfo.ShortSHA): $previousCommitSha -> $($repoInfo.SourceSHA)" -ForegroundColor Green
                        }
                        $manifestChanges += $repoInfo
                        $previousCommitSha = $repoInfo.SourceSHA
                    } else {
                        if ($Verbose) {
                            Write-Host "  Skipping commit $($repoInfo.ShortSHA): Repository commit unchanged" -ForegroundColor DarkYellow
                        }
                    }
                }
            }
            catch {
                Write-Host "Error processing commit $commitSHA`: $_" -ForegroundColor Red
                continue
            }
        }

        return $manifestChanges
    }
    catch {
        Write-Host "Error finding commits that changed file $filePath`: $_" -ForegroundColor Red
        return @()
    }
}

# Function to find Source tag changes in commits
function Find-SourceTagChanges {
    param (
        [string]$repoPath,
        [string]$filePath = "eng/Version.Details.xml",
        [int]$count = 200,  # Increased to find more historic changes
        [switch]$Verbose
    )

    # Get commits that changed the file
    try {
        $commitSHAs = Get-CommitsThatChangedFile -repoPath $repoPath -filePath $filePath -count $count

        if (-not $commitSHAs -or $commitSHAs.Count -eq 0) {
            Write-Host "No commits found that changed file: $filePath" -ForegroundColor Yellow
            return @()
        }

        $sourceTagChanges = @()
        $previousSourceSHA = $null

        # For each commit, extract Source tag SHA
        foreach ($commitSHA in $commitSHAs) {
            try {
                # Pass the Verbose switch if it was provided
                $verboseParam = @{}
                if ($Verbose) {
                    $verboseParam.Verbose = $true
                }

                $change = Get-SourceTagShaFromCommit -repoPath $repoPath -commitSHA $commitSHA -filePath $filePath @verboseParam
                if ($change) {
                    # Only include commits where the Source tag SHA changed from the previous commit
                    if ($change.SourceSHA -ne $previousSourceSHA) {
                        if ($Verbose) {
                            Write-Host "  Source tag changed in commit $($change.ShortSHA): $previousSourceSHA -> $($change.SourceSHA)" -ForegroundColor Green
                        }
                        $sourceTagChanges += $change
                        $previousSourceSHA = $change.SourceSHA
                    }
                    else {
                        if ($Verbose) {
                            Write-Host "  Skipping commit $($change.ShortSHA): Source tag unchanged" -ForegroundColor DarkYellow
                        }
                    }
                }
            }
            catch {
                Write-Host "Error processing commit $commitSHA`: $_" -ForegroundColor Red
                continue
            }
        }

        return $sourceTagChanges
    }
    catch {
        Write-Host "Error finding commits that changed file $filePath`: $_" -ForegroundColor Red
        return @()
    }
}

# Debug function to show the content of Version.Details.xml for a given commit
function Show-VersionDetailsContent {
    param (
        [string]$repoPath,
        [string]$commitSHA,
        [string]$filePath = "eng/Version.Details.xml"
    )

    Write-Host "Examining XML in commit $commitSHA..." -ForegroundColor Yellow
    # Get the content of the file at the specific commit
    $fileContent = git -C $repoPath show "$commitSHA`:$filePath" 2>$null

    # Check if the file exists in this commit
    if (-not $fileContent) {
        Write-Host "File $filePath doesn't exist in commit $commitSHA" -ForegroundColor Red
        return
    }

    # Display parts of the XML that might contain Source tags
    Write-Host "XML snippets with potential Source tags:" -ForegroundColor Cyan
    $lines = $fileContent -split "`n"
    foreach ($line in $lines) {
        if ($line -match '<Source|<Dependency|<ProductDependencies|Sha=|<Sha>') {
            Write-Host "  $line" -ForegroundColor White
        }
    }
}

# Function to determine the optimal VMR depth based on referenced commits in repo history
function Get-OptimalVmrDepth {
    param (
        [string]$repoPath,
        [string]$vmrPath,
        [int]$defaultDepth = 50,
        [int]$minDepth = 10,
        [int]$maxDepth = 500,
        [switch]$Verbose
    )

    $verboseParam = @{}
    if ($Verbose) {
        $verboseParam.Verbose = $true
        Write-Verbose "Determining optimal VMR depth..."
    }

    # Try to find referenced commits in Version.Details.xml first
    Write-Host "Finding Source tags in repo history to determine optimal VMR depth..." -ForegroundColor Yellow
    $sourceTagChanges = Find-SourceTagChanges -repoPath $RepoPath -filePath "eng/Version.Details.xml" -count $defaultDepth @verboseParam

    if ($sourceTagChanges -and $sourceTagChanges.Count -gt 0) {
        # Find the oldest VMR commit referenced by examining each Source tag
        $oldestVmrSha = $null
        $oldestCommitAge = 0

        foreach ($change in $sourceTagChanges) {
            # Use git rev-list to find how far back this commit is in VMR history
            try {
                # First check if the commit exists in VMR history
                $commitExists = git -C $vmrPath cat-file -e $change.SourceSHA 2>$null
                if ($? -eq $false) {
                    if ($Verbose) {
                        Write-Host "  VMR commit $($change.ShortSourceSHA) not found in VMR history" -ForegroundColor Yellow
                    }
                    continue
                }

                # Find how many commits back this is from HEAD
                $commitAge = git -C $vmrPath rev-list --count "$($change.SourceSHA)..HEAD" 2>$null
                if ($commitAge -and ([int]$commitAge -gt $oldestCommitAge)) {
                    $oldestCommitAge = [int]$commitAge
                    $oldestVmrSha = $change.SourceSHA
                    if ($Verbose) {
                        Write-Host "  VMR commit $($change.ShortSourceSHA) is $commitAge commits old" -ForegroundColor Cyan
                    }
                }
            } catch {
                # Silently continue if we can't find this SHA
                if ($Verbose) {
                    Write-Host "  Could not analyze VMR commit $($change.ShortSourceSHA): $_" -ForegroundColor Yellow
                }
            }
        }

        return $oldestCommitAge
    }

    # Try to determine depth from source-manifest.json if Version.Details.xml didn't provide results
    Write-Host "  Trying to determine optimal VMR depth from source-manifest.json..." -ForegroundColor Yellow

    $repoMapping = Get-SourceTagMappingFromVersionDetails -repoPath $repoPath @verboseParam
    if ($repoMapping) {
        # Look at source-manifest.json changes to find any references to repo commits
        $manifestChanges = Find-SourceManifestChanges -vmrPath $VmrPath -repoMapping $repoMapping -count $defaultDepth @verboseParam

        if ($manifestChanges -and $manifestChanges.Count -gt 0) {
            # Find the oldest VMR commit that references a repo commit
            $oldestVmrSha = $null
            $oldestCommitAge = 0

            foreach ($change in $manifestChanges) {
                try {
                    # Get the commit age from HEAD
                    $commitAge = git -C $vmrPath rev-list --count "$($change.CommitSHA)..HEAD" 2>$null
                    if ($commitAge -and ([int]$commitAge -gt $oldestCommitAge)) {
                        $oldestCommitAge = [int]$commitAge
                        $oldestVmrSha = $change.CommitSHA
                        if ($Verbose) {
                            Write-Host "  VMR commit $($change.ShortSHA) is $commitAge commits old" -ForegroundColor Cyan
                        }
                    }
                } catch {
                    # Silently continue if we can't process this commit
                    if ($Verbose) {
                        Write-Host "  Could not analyze VMR commit $($change.ShortSHA): $_" -ForegroundColor Yellow
                    }
                }
            }

            if ($oldestCommitAge -gt 0) {
                # Calculate optimal depth with buffer
                $calculatedDepth = [Math]::Ceiling($oldestCommitAge * 1.2) + 10
                $optimalDepth = [Math]::Min($maxDepth, $calculatedDepth)
                $optimalDepth = [Math]::Max($minDepth, $optimalDepth)

                Write-Host "  Setting VMR depth to $optimalDepth based on oldest VMR commit that references repo ($($oldestVmrSha.Substring(0,7)), $oldestCommitAge commits old)" -ForegroundColor Green
                return $optimalDepth
            }
        }
    }

    # If we couldn't determine an optimal depth, use a default value (2x the repo depth)
    $defaultVmrDepth = $defaultDepth * 2
    Write-Host "  Could not determine VMR history depth from repository references, using default of $defaultVmrDepth" -ForegroundColor Yellow
    return $defaultVmrDepth
}
# Main script execution

try {
    Write-Host "Generating Git commit DOT graph diagram..." -ForegroundColor Green

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

    $diagramText = Create-DotDiagram @diagramParams

    # Determine output file path
    if (-not $OutputPath) {
        $outputPath = Join-Path -Path $PSScriptRoot -ChildPath "git-commit-diagram.dot"
    } else {
        $outputPath = $OutputPath
    }    
    
    # Add summary information to the diagram as comments
    $diagramSummary = "// Git Commit Diagram - Generated $(Get-Date)`n"
    $diagramSummary += "// Repositories: vmr ($VmrPath) and repo ($RepoPath)`n"
    $diagramSummary += "// Repository URLs: vmr ($vmrRepoUrl) and repo ($sourceRepoUrl)`n"
    $diagramSummary += "// Total Commits: $($vmrCommits.Count) vmr commits, $($repoCommits.Count) repo commits`n"
    $diagramSummary += "// Cross-Repository Connections: $($crossRepoConnections.Count) connections found`n"
    $diagramSummary += "// Collapse Threshold: $CollapseThreshold (NoCollapse: $NoCollapse)`n"
    $diagramSummary += "// Note: Commit nodes are clickable and link to the repository`n`n"

    # Use a consistent prefix with the DOT diagram
    $completeText = $diagramSummary + $diagramText

    # Save the diagram to a file with explanation
    Set-Content -Path $outputPath -Value $completeText
    Write-Host "DOT graph diagram saved to: $outputPath" -ForegroundColor Green
}
catch {
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
}
