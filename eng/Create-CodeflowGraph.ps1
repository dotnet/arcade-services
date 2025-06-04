[CmdletBinding()]
param(
    [Parameter(HelpMessage="Number of commits to retrieve from history")]
    [int]$Depth = 20,
    [Parameter(Mandatory=$true, HelpMessage="Path to the repository")]
    [string]$RepoPath,
    [Parameter(Mandatory=$true, HelpMessage="Path to the VMR")]
    [string]$VmrPath,
    [Parameter(Mandatory=$false, HelpMessage="Number of consecutive commits without cross-references to collapse")]
    [int]$CollapseThreshold = 2,
    [Parameter(Mandatory=$false, HelpMessage="Disable collapsing commits regardless of threshold")]
    [switch]$NoCollapse,
    [Parameter(Mandatory=$false, HelpMessage="Path to output the Mermaid diagram file")]
    [string]$OutputPath = "",
    [switch]$VerboseScript
)

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

# Function to create Mermaid diagram text from commits
function Create-MermaidDiagram {
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

    # Start building the Mermaid diagram string
    $diagram = "flowchart TD`n"    # First identify all commits involved in cross-repo references
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

    # Track collapsed nodes to know which SHAs to replace in the graph
    $collapsedNodes = @{}

    # Add VMR repository subgraph
    $diagram += "    subgraph $vmrName`n"

    # First pass - create regular nodes and collapsed nodes
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

                    # Create link for collapsed range if repo URL is provided
                    if ($vmrRepoUrl) {
                        # For ranges, use the compare URL pattern
                        $compareUrl = "$vmrRepoUrl/compare/$($lastCommit.CommitSHA)...$($firstCommit.CommitSHA)"
                        $label = "`"$($firstCommit.ShortSHA)..$($lastCommit.ShortSHA)<br>(+$($range.Count-1) commits)`""
                        $diagram += "        $collapseId[$label]:::clickable`n"
                        $diagram += "        click $collapseId `"$compareUrl`" _blank`n"
                    } else {
                        $label = "`"$($firstCommit.ShortSHA)..$($lastCommit.ShortSHA)<br>(+$($range.Count-1) commits)`""
                        $diagram += "        $collapseId[$label]`n"
                    }

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
            if ($vmrRepoUrl) {
                # Create single commit link
                $commitUrl = "$vmrRepoUrl/commit/$($commit.CommitSHA)"
                $diagram += "        $($commit.ShortSHA)[$($commit.ShortSHA)]:::clickable`n"
                $diagram += "        click $($commit.ShortSHA) `"$commitUrl`" _blank`n"
            } else {
                $diagram += "        $($commit.ShortSHA)[$($commit.ShortSHA)]`n"
            }
        }
    }

    # Second pass - create a map of all commit SHAs for fast lookup
    $vmrCommitMap = @{}
    foreach ($commit in $vmrCommits) {
        $vmrCommitMap[$commit.CommitSHA] = $commit
    }

    # Create a map for visible nodes (non-collapsed or first commit of collapsed range)
    $visibleNodes = @()
    $nodeToIndexMap = @{}
    $nodeIndex = 0

    # First identify all visible nodes in chronological order (newest to oldest as in Git log)
    foreach ($commit in $vmrCommits) {
        $isFirstInCollapsedRange = $false
        $nodeId = ""

        # Check if this commit is inside a collapsed range
        if ($collapsedNodes.ContainsKey($commit.CommitSHA)) {
            # Check if it's the first commit in a collapsed range
            foreach ($range in $vmrCollapsibleRanges) {
                if ($range.Count -gt 0 -and $range[0].CommitSHA -eq $commit.CommitSHA) {
                    $firstCommit = $range[0]
                    $lastCommit = $range[-1]
                    $rangeIdx = [array]::IndexOf($vmrCollapsibleRanges, $range)
                    $nodeId = "vmr_${($firstCommit.ShortSHA)}_${($lastCommit.ShortSHA)}_$rangeIdx"
                    $isFirstInCollapsedRange = $true
                    break
                }
            }
        }

        # If it's either not collapsed or the first in a collapsed range, add to visible nodes
        if (-not $collapsedNodes.ContainsKey($commit.CommitSHA) -or $isFirstInCollapsedRange) {
            if (-not $nodeId) {
                $nodeId = $commit.ShortSHA
            }

            $visibleNodes += $nodeId
            $nodeToIndexMap[$nodeId] = $nodeIndex
            $nodeIndex++
        }
    }

    # Now create edges to form a linear chain of history
    $processedEdges = @{}
    for ($i = 0; $i -lt ($visibleNodes.Count - 1); $i++) {
        $sourceNodeId = $visibleNodes[$i]
        $targetNodeId = $visibleNodes[$i + 1]

        # Create a unique edge identifier
        $edgeKey = "$sourceNodeId-->$targetNodeId"

        # Only add each edge once
        if (-not $processedEdges.ContainsKey($edgeKey)) {
            $diagram += "        $sourceNodeId-->$targetNodeId`n"
            $processedEdges[$edgeKey] = $true
        }
    }

    # Additionally add parent-child relationships for merge commits
    foreach ($commit in $vmrCommits) {
        # Skip if commit is inside a collapsed range but isn't the first one
        $isInnerCollapsed = $collapsedNodes.ContainsKey($commit.CommitSHA) -and
                           (-not $vmrCollapsibleRanges.Where({ $_.Count -gt 0 -and $_[0].CommitSHA -eq $commit.CommitSHA }))

        if ($isInnerCollapsed) {
            continue
        }

        # Only process merge commits (more than one parent)
        if ($commit.ParentSHAs -and $commit.ParentSHAs.Count -gt 1) {
            # Get the node ID for this commit (might be a collapsed node)
            $childNodeId = if ($collapsedNodes.ContainsKey($commit.CommitSHA)) {
                $collapsedNodes[$commit.CommitSHA]
            } else {
                $commit.ShortSHA
            }

            # Process all secondary parents (skip the first one as it's already in the linear chain)
            for ($i = 1; $i -lt $commit.ParentSHAs.Count; $i++) {
                $parentSHA = $commit.ParentSHAs[$i]

                # Check if parent is in our loaded commits
                if ($vmrCommitMap.ContainsKey($parentSHA)) {
                    $parentCommit = $vmrCommitMap[$parentSHA]

                    # Skip if parent is inside a collapsed range but isn't the first one
                    $parentIsInnerCollapsed = $collapsedNodes.ContainsKey($parentSHA) -and
                                             (-not $vmrCollapsibleRanges.Where({ $_.Count -gt 0 -and $_[0].CommitSHA -eq $parentSHA }))

                    if ($parentIsInnerCollapsed) {
                        continue
                    }

                    # Get the node ID for the parent (might be a collapsed node)
                    $parentNodeId = if ($collapsedNodes.ContainsKey($parentSHA)) {
                        $collapsedNodes[$parentSHA]
                    } else {
                        $parentCommit.ShortSHA
                    }

                    # Create a unique edge identifier
                    $edgeKey = "$parentNodeId-->$childNodeId"

                    # Only add merge branch connections if they aren't already in the linear chain
                    if (-not $processedEdges.ContainsKey($edgeKey)) {
                        $diagram += "        $parentNodeId-.->$childNodeId`n"
                        $processedEdges[$edgeKey] = $true
                    }
                }
            }
        }
    }

    # Close VMR repository subgraph
    $diagram += "    end`n"

    # Clear collapsed nodes for repo commits
    $collapsedNodes = @{}

    # Add source repository subgraph
    $diagram += "    subgraph $repoName`n"    # First pass - create regular nodes and collapsed nodes
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

                    # Create link for collapsed range if repo URL is provided
                    if ($repoUrl) {
                        # For ranges, use the compare URL pattern
                        $compareUrl = "$repoUrl/compare/$($lastCommit.CommitSHA)...$($firstCommit.CommitSHA)"
                        $label = "`"$($firstCommit.ShortSHA)..$($lastCommit.ShortSHA)<br>(+$($range.Count-1) commits)`""
                        $diagram += "        $collapseId[$label]:::clickable`n"
                        $diagram += "        click $collapseId `"$compareUrl`" _blank`n"
                    } else {
                        $label = "`"$($firstCommit.ShortSHA)..$($lastCommit.ShortSHA)<br>(+$($range.Count-1) commits)`""
                        $diagram += "        $collapseId[$label]`n"
                    }

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
            if ($repoUrl) {
                # Create single commit link
                $commitUrl = "$repoUrl/commit/$($commit.CommitSHA)"
                $diagram += "        $($commit.ShortSHA)[$($commit.ShortSHA)]:::clickable`n"
                $diagram += "        click $($commit.ShortSHA) `"$commitUrl`" _blank`n"
            } else {
                $diagram += "        $($commit.ShortSHA)[$($commit.ShortSHA)]`n"
            }
        }
    }

    # Second pass - create a map of all commit SHAs for fast lookup
    $repoCommitMap = @{}
    foreach ($commit in $repoCommits) {
        $repoCommitMap[$commit.CommitSHA] = $commit
    }

    # Create a map for visible nodes (non-collapsed or first commit of collapsed range)
    $visibleNodes = @()
    $nodeToIndexMap = @{}
    $nodeIndex = 0

    # First identify all visible nodes in chronological order (newest to oldest as in Git log)
    foreach ($commit in $repoCommits) {
        $isFirstInCollapsedRange = $false
        $nodeId = ""

        # Check if this commit is inside a collapsed range
        if ($collapsedNodes.ContainsKey($commit.CommitSHA)) {
            # Check if it's the first commit in a collapsed range
            foreach ($range in $repoCollapsibleRanges) {
                if ($range.Count -gt 0 -and $range[0].CommitSHA -eq $commit.CommitSHA) {
                    $firstCommit = $range[0]
                    $lastCommit = $range[-1]
                    $rangeIdx = [array]::IndexOf($repoCollapsibleRanges, $range)
                    $nodeId = "repo_${($firstCommit.ShortSHA)}_${($lastCommit.ShortSHA)}_$rangeIdx"
                    $isFirstInCollapsedRange = $true
                    break
                }
            }
        }

        # If it's either not collapsed or the first in a collapsed range, add to visible nodes
        if (-not $collapsedNodes.ContainsKey($commit.CommitSHA) -or $isFirstInCollapsedRange) {
            if (-not $nodeId) {
                $nodeId = $commit.ShortSHA
            }

            $visibleNodes += $nodeId
            $nodeToIndexMap[$nodeId] = $nodeIndex
            $nodeIndex++
        }
    }

    # Now create edges to form a linear chain of history
    $processedEdges = @{}
    for ($i = 0; $i -lt ($visibleNodes.Count - 1); $i++) {
        $sourceNodeId = $visibleNodes[$i]
        $targetNodeId = $visibleNodes[$i + 1]

        # Create a unique edge identifier
        $edgeKey = "$sourceNodeId-->$targetNodeId"

        # Only add each edge once
        if (-not $processedEdges.ContainsKey($edgeKey)) {
            $diagram += "        $sourceNodeId-->$targetNodeId`n"
            $processedEdges[$edgeKey] = $true
        }
    }

    # Additionally add parent-child relationships for merge commits
    foreach ($commit in $repoCommits) {
        # Skip if commit is inside a collapsed range but isn't the first one
        $isInnerCollapsed = $collapsedNodes.ContainsKey($commit.CommitSHA) -and
                           (-not $repoCollapsibleRanges.Where({ $_.Count -gt 0 -and $_[0].CommitSHA -eq $commit.CommitSHA }))

        if ($isInnerCollapsed) {
            continue
        }

        # Only process merge commits (more than one parent)
        if ($commit.ParentSHAs -and $commit.ParentSHAs.Count -gt 1) {
            # Get the node ID for this commit (might be a collapsed node)
            $childNodeId = if ($collapsedNodes.ContainsKey($commit.CommitSHA)) {
                $collapsedNodes[$commit.CommitSHA]
            } else {
                $commit.ShortSHA
            }

            # Process all secondary parents (skip the first one as it's already in the linear chain)
            for ($i = 1; $i -lt $commit.ParentSHAs.Count; $i++) {
                $parentSHA = $commit.ParentSHAs[$i]

                # Check if parent is in our loaded commits
                if ($repoCommitMap.ContainsKey($parentSHA)) {
                    $parentCommit = $repoCommitMap[$parentSHA]

                    # Skip if parent is inside a collapsed range but isn't the first one
                    $parentIsInnerCollapsed = $collapsedNodes.ContainsKey($parentSHA) -and
                                             (-not $repoCollapsibleRanges.Where({ $_.Count -gt 0 -and $_[0].CommitSHA -eq $parentSHA }))

                    if ($parentIsInnerCollapsed) {
                        continue
                    }

                    # Get the node ID for the parent (might be a collapsed node)
                    $parentNodeId = if ($collapsedNodes.ContainsKey($parentSHA)) {
                        $collapsedNodes[$parentSHA]
                    } else {
                        $parentCommit.ShortSHA
                    }

                    # Create a unique edge identifier
                    $edgeKey = "$parentNodeId-->$childNodeId"

                    # Only add merge branch connections if they aren't already in the linear chain
                    if (-not $processedEdges.ContainsKey($edgeKey)) {
                        $diagram += "        $parentNodeId-.->$childNodeId`n"
                        $processedEdges[$edgeKey] = $true
                    }
                }
            }
        }
    }

    # Close repo subgraph
    $diagram += "    end`n"
      # Add cross-repository connections (Source tag connections and backflow connections)
    if ($crossRepoConnections -and $crossRepoConnections.Count -gt 0) {
        $diagram += "`n    %% Cross-repository connections`n"
        # Define styles for different types of connections
        $diagram += "    classDef forwardFlowSource stroke:#0c0,stroke-width:2px,color:#0c0`n"
        $diagram += "    classDef forwardFlowTarget stroke:#0c0,stroke-width:2px,color:#0c0`n"
        $diagram += "    classDef backflowSource stroke:#f00,stroke-width:2px,color:#f00`n"
        $diagram += "    classDef backflowTarget stroke:#f00,stroke-width:2px,color:#f00`n"
        $diagram += "    classDef collapsedNodes fill:#f0f0f0,stroke:#999,stroke-width:1px,color:#666`n"
        $diagram += "    classDef clickable cursor:pointer,stroke:#666,stroke-width:1px`n"

        # Track which external commits we've added
        $externalVmrCommits = @{}
        $externalRepoCommits = @{}
        $processedConnections = @{}

        foreach ($connection in $crossRepoConnections) {
            # Determine if this is a forward flow or backflow connection
            $isBackflow = $connection.ConnectionType -eq "BackFlow"

            # Create a unique identifier for this connection to avoid duplicates
            $connectionKey = "$($connection.ShortSourceSHA)_$($connection.ShortSHA)"
            if ($isBackflow) {
                # For backflow, we reverse the order to ensure uniqueness
                $connectionKey = "backflow_$($connection.ShortSHA)_$($connection.ShortSourceSHA)"
            }

            # Skip if we've already processed this connection
            if ($processedConnections.ContainsKey($connectionKey)) {
                continue
            }            # Mark this connection as processed
            $processedConnections[$connectionKey] = $true

            # Check if the commits exist in our diagrams
            $sourceInVmr = $vmrCommits.CommitSHA -contains $connection.SourceSHA
            $targetInVmr = $vmrCommits.CommitSHA -contains $connection.CommitSHA
            $sourceInRepo = $repoCommits.CommitSHA -contains $connection.SourceSHA
            $targetInRepo = $repoCommits.CommitSHA -contains $connection.CommitSHA

            if ($Verbose) {
                if ($isBackflow) {
                    Write-Host "  Processing backflow connection: VMR $($connection.ShortSHA) -> Repo $($connection.ShortSourceSHA)" -ForegroundColor Magenta
                } else {
                    Write-Host "  Processing forward flow connection: Repo $($connection.ShortSHA) -> VMR $($connection.ShortSourceSHA)" -ForegroundColor Green
                }
            }

            # Get source node ID (might be collapsed)
            $sourceNodeId = if ($isBackflow) {
                # For backflow, source is VMR commit
                if ($targetInVmr) {
                    # VMR commit is in loaded history, check if it's part of a collapsed node
                    $vmrCollapseId = $null
                    if ($collapsedNodes.ContainsKey($connection.CommitSHA)) {
                        $vmrCollapseId = $collapsedNodes[$connection.CommitSHA]
                    }

                    if ($vmrCollapseId) {
                        if ($Verbose) {
                            Write-Host "    Using collapsed VMR node: $vmrCollapseId for $($connection.ShortSHA)" -ForegroundColor Cyan
                        }
                        $vmrCollapseId
                    } else {
                        if ($Verbose) {
                            Write-Host "    Using regular VMR node: $($connection.ShortSHA)" -ForegroundColor Cyan
                        }
                        $connection.ShortSHA
                    }
                } else {
                    # External commit - add if not already added
                    if (-not $externalVmrCommits.ContainsKey($connection.ShortSHA)) {
                        $diagram += "    $($connection.ShortSHA)[$($connection.ShortSHA)]`n"
                        $externalVmrCommits[$connection.ShortSHA] = $true
                        if ($Verbose) {
                            Write-Host "    Added external VMR node: $($connection.ShortSHA)" -ForegroundColor Yellow
                        }
                    }
                    $connection.ShortSHA
                }
            } else {
                # For forward flow, source is repo commit
                if ($sourceInRepo) {
                    # Repo commit is in loaded history, check if it's part of a collapsed node
                    $repoCollapseId = $null
                    if ($collapsedNodes.ContainsKey($connection.CommitSHA)) {
                        $repoCollapseId = $collapsedNodes[$connection.CommitSHA]
                    }

                    if ($repoCollapseId) {
                        if ($Verbose) {
                            Write-Host "    Using collapsed repo node: $repoCollapseId for $($connection.ShortSHA)" -ForegroundColor Cyan
                        }
                        $repoCollapseId
                    } else {
                        if ($Verbose) {
                            Write-Host "    Using regular repo node: $($connection.ShortSHA)" -ForegroundColor Cyan
                        }
                        $connection.ShortSHA
                    }
                } else {
                    # External commit - add if not already added
                    if (-not $externalRepoCommits.ContainsKey($connection.ShortSHA)) {
                        $diagram += "    $($connection.ShortSHA)[$($connection.ShortSHA)]`n"
                        $externalRepoCommits[$connection.ShortSHA] = $true
                        if ($Verbose) {
                            Write-Host "    Added external repo node: $($connection.ShortSHA)" -ForegroundColor Yellow
                        }
                    }
                    $connection.ShortSHA
                }
            }

            # Get target node ID (might be collapsed)
            $targetNodeId = if ($isBackflow) {
                # For backflow, target is repo commit
                if ($sourceInRepo) {
                    # Repo commit is in loaded history, check if it's part of a collapsed node
                    $repoCollapseId = $null
                    if ($collapsedNodes.ContainsKey($connection.SourceSHA)) {
                        $repoCollapseId = $collapsedNodes[$connection.SourceSHA]
                    }

                    if ($repoCollapseId) {
                        if ($Verbose) {
                            Write-Host "    Using collapsed repo target node: $repoCollapseId for $($connection.ShortSourceSHA)" -ForegroundColor Cyan
                        }
                        $repoCollapseId
                    } else {
                        if ($Verbose) {
                            Write-Host "    Using regular repo target node: $($connection.ShortSourceSHA)" -ForegroundColor Cyan
                        }
                        $connection.ShortSourceSHA
                    }
                } else {
                    # External commit - add if not already added
                    if (-not $externalRepoCommits.ContainsKey($connection.ShortSourceSHA)) {
                        $diagram += "    $($connection.ShortSourceSHA)[$($connection.ShortSourceSHA)]`n"
                        $externalRepoCommits[$connection.ShortSourceSHA] = $true
                        if ($Verbose) {
                            Write-Host "    Added external repo target node: $($connection.ShortSourceSHA)" -ForegroundColor Yellow
                        }
                    }
                    $connection.ShortSourceSHA
                }
            } else {
                # For forward flow, target is VMR commit
                if ($sourceInVmr) {
                    # VMR commit is in loaded history, check if it's part of a collapsed node
                    $vmrCollapseId = $null
                    if ($collapsedNodes.ContainsKey($connection.SourceSHA)) {
                        $vmrCollapseId = $collapsedNodes[$connection.SourceSHA]
                    }

                    if ($vmrCollapseId) {
                        if ($Verbose) {
                            Write-Host "    Using collapsed VMR target node: $vmrCollapseId for $($connection.ShortSourceSHA)" -ForegroundColor Cyan
                        }
                        $vmrCollapseId
                    } else {
                        if ($Verbose) {
                            Write-Host "    Using regular VMR target node: $($connection.ShortSourceSHA)" -ForegroundColor Cyan
                        }
                        $connection.ShortSourceSHA
                    }
                } else {
                    # External commit - add if not already added
                    if (-not $externalVmrCommits.ContainsKey($connection.ShortSourceSHA)) {
                        $diagram += "    $($connection.ShortSourceSHA)[$($connection.ShortSourceSHA)]`n"
                        $externalVmrCommits[$connection.ShortSourceSHA] = $true
                        if ($Verbose) {
                            Write-Host "    Added external VMR target node: $($connection.ShortSourceSHA)" -ForegroundColor Yellow
                        }
                    }
                    $connection.ShortSourceSHA
                }
            }

            # Format connection with appropriate label and style
            if ($isBackflow) {
                $diagram += "    $sourceNodeId -..-> $targetNodeId:::backflowTarget`n"

                # Apply styling based on connection type
                if (-not $sourceNodeId.Contains("_")) {
                    $diagram += "    class $sourceNodeId backflowSource`n"
                }
                if (-not $targetNodeId.Contains("_")) {
                    $diagram += "    class $targetNodeId backflowTarget`n"
                }
            } else {
                $diagram += "    $sourceNodeId -..-> $targetNodeId:::forwardFlowTarget`n"

                # Apply styling based on connection type
                if (-not $sourceNodeId.Contains("_")) {
                    $diagram += "    class $sourceNodeId forwardFlowSource`n"
                }
                if (-not $targetNodeId.Contains("_")) {
                    $diagram += "    class $targetNodeId forwardFlowTarget`n"
                }
            }
        }

        # Apply styling to all collapsed nodes
        foreach ($range in $vmrCollapsibleRanges) {
            if ($range.Count -gt 0) {
                $firstCommit = $range[0]
                $lastCommit = $range[-1]
                $rangeIdx = [array]::IndexOf($vmrCollapsibleRanges, $range)
                $collapseId = "vmr_${($firstCommit.ShortSHA)}_${($lastCommit.ShortSHA)}_$rangeIdx"
                $diagram += "    class $collapseId collapsedNodes`n"
            }
        }

        foreach ($range in $repoCollapsibleRanges) {
            if ($range.Count -gt 0) {
                $firstCommit = $range[0]
                $lastCommit = $range[-1]
                $rangeIdx = [array]::IndexOf($repoCollapsibleRanges, $range)
                $collapseId = "repo_${($firstCommit.ShortSHA)}_${($lastCommit.ShortSHA)}_$rangeIdx"
                $diagram += "    class $collapseId collapsedNodes`n"
            }
        }
    }

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
    }

    Write-Host "Loading commits from VMR ($VmrPath)..." -ForegroundColor Yellow
    $vmrCommits = Get-GitCommits -repoPath $VmrPath -count $($Depth*8) @verboseParam
    Write-Host "Loaded $($vmrCommits.Count) commits from VMR repository" -ForegroundColor Green

    Write-Host "Loading commits from repo ($RepoPath)..." -ForegroundColor Yellow
    $repoCommits = Get-GitCommits -repoPath $RepoPath -count $Depth @verboseParam
    Write-Host "Loaded $($repoCommits.Count) commits from source repository" -ForegroundColor Green

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
                if (-not $repoCommitExists) {
                    Write-Host "  - Skipped: repo commit $($change.ShortSHA) not in loaded history" -ForegroundColor DarkYellow
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
                    Write-Host "  + Added backflow $($change.ShortSHA) -> $($change.ShortSourceSHA)" -ForegroundColor Red
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
