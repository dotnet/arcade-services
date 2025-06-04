[CmdletBinding()]
param(
    [Parameter(HelpMessage="Number of commits to retrieve from history")]
    [int]$HistoryCount = 50,
    [Parameter(Mandatory=$false, HelpMessage="Path to the repository")]
    [string]$repo = "D:\tmp\aspnetcore",
    [Parameter(Mandatory=$false, HelpMessage="Path to the VMR (Virtual Mono Repository)")]
    [string]$vmr = "D:\repos\dotnet",
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
# Parameters:
# -HistoryCount: Specify how many commits to retrieve from history
# -repo: Path to the source repository (default: "D:\tmp\aspnetcore")
# -vmr: Path to the VMR (Virtual Mono Repository) (default: "D:\repos\dotnet")
# -CollapseThreshold: Number of consecutive commits without cross-references to collapse into a single node
#   Set to 0 to disable collapsing, or a higher number to collapse longer ranges of commits
# -NoCollapse: Disable commit collapsing feature entirely (overrides CollapseThreshold)
# -OutputPath: Path to output the Mermaid diagram file (default: script directory/git-commit-diagram.mmd)
# -VerboseScript: Show detailed logging during script execution
#
# The diagram will show connections between repos based on Source tag references in Version.Details.xml
# Collapsed commits are displayed as a single node showing the first and last commit in the range

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
        [switch]$Verbose
    )

    # Start building the Mermaid diagram string
    $diagram = "flowchart TD`n"

    # First identify which commits are involved in cross-repo references
    $referencedVmrCommits = @{}
    $referencedRepoCommits = @{}

    foreach ($connection in $crossRepoConnections) {
        $referencedVmrCommits[$connection.SourceSHA] = $true
        $referencedRepoCommits[$connection.CommitSHA] = $true
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
                    $label = "`"$($firstCommit.ShortSHA)..$($lastCommit.ShortSHA)<br>(+$($range.Count-1) commits)`""
                    $diagram += "        $collapseId[$label]`n"

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
            $diagram += "        $($commit.ShortSHA)[$($commit.ShortSHA)]`n"
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
    $diagram += "    subgraph $repoName`n"

    # First pass - create regular nodes and collapsed nodes
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
                    $label = "`"$($firstCommit.ShortSHA)..$($lastCommit.ShortSHA)<br>(+$($range.Count-1) commits)`""
                    $diagram += "        $collapseId[$label]`n"

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
            $diagram += "        $($commit.ShortSHA)[$($commit.ShortSHA)]`n"
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

    # Add cross-repository connections (Source tag connections)
    if ($crossRepoConnections -and $crossRepoConnections.Count -gt 0) {
        $diagram += "`n    %% Cross-repository connections from Source tag references`n"
        $diagram += "    classDef backflowSourceCommit stroke:#0c0,stroke-width:2px,color:#0c0,stroke-dasharray: 5 5`n"
        $diagram += "    classDef externalCommit fill:#f99,stroke:#f66,stroke-width:1px,color:#000,stroke-dasharray: 5 5`n"
        $diagram += "    classDef backflowTargetCommit stroke:#0c0,stroke-width:2px,color:#0c0`n"
        $diagram += "    classDef collapsedNodes fill:#f0f0f0,stroke:#999,stroke-width:1px,color:#666`n"

        # Track which external commits we've added
        $externalVmrCommits = @{}
        $processedConnections = @{
        }

        foreach ($connection in $crossRepoConnections) {
            # Create a unique identifier for this connection to avoid duplicates
            $connectionKey = "$($connection.ShortSourceSHA)_$($connection.ShortSHA)"

            # Skip if we've already processed this connection
            if ($processedConnections.ContainsKey($connectionKey)) {
                continue
            }

            # Mark this connection as processed
            $processedConnections[$connectionKey] = $true

            # Check if the source commit exists in our diagram
            $vmrCommitExists = $vmrCommits.CommitSHA -contains $connection.SourceSHA

            # Get source node ID (might be collapsed)
            $sourceNodeId = if ($vmrCommitExists -and $collapsedNodes.ContainsKey($connection.SourceSHA)) {
                # Use the collapsed node ID
                $collapsedNodes[$connection.SourceSHA]
            } elseif ($vmrCommitExists) {
                # Use the commit SHA
                $connection.ShortSourceSHA
            } else {
                # External commit - add if not already added
                if (-not $externalVmrCommits.ContainsKey($connection.ShortSourceSHA)) {
                    $diagram += "    $($connection.ShortSourceSHA)[$($connection.ShortSourceSHA)*]`n"
                    $diagram += "    class $($connection.ShortSourceSHA) externalCommit`n"
                    $externalVmrCommits[$connection.ShortSourceSHA] = $true
                }
                $connection.ShortSourceSHA
            }

            # Get target node ID (might be collapsed)
            $targetNodeId = if ($collapsedNodes.ContainsKey($connection.CommitSHA)) {
                $collapsedNodes[$connection.CommitSHA]
            } else {
                $connection.ShortSHA
            }

            # Format: vmr commit <- repo commit (shows repo commit references vmr commit)
            $diagram += "    $sourceNodeId -. forward flow .-> $targetNodeId`n"

            # Apply styling for commits (don't style collapsed nodes)
            if ($vmrCommitExists -and -not $sourceNodeId.Contains("_")) {
                $diagram += "    class $sourceNodeId backflowSourceCommit`n"
            }
            if (-not $targetNodeId.Contains("_")) {
                $diagram += "    class $targetNodeId backflowTargetCommit`n"
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

    # Repository paths from parameters
    $vmrPath = $vmr
    $repoPath = $repo

    $vmrCommits = @()
    $repoCommits = @()

    # Create a parameter hashtable for verbose if needed
    $verboseParam = @{}
    if ($VerboseScript) {
        $verboseParam.Verbose = $true
        Write-Verbose "Verbose mode enabled"
    }

    Write-Host "Loading commits from VMR ($vmrPath)..." -ForegroundColor Yellow
    $vmrCommits = Get-GitCommits -repoPath $vmrPath -count $($HistoryCount*8) @verboseParam
    Write-Host "Loaded $($vmrCommits.Count) commits from VMR repository" -ForegroundColor Green

    Write-Host "Loading commits from repo ($repoPath)..." -ForegroundColor Yellow
    $repoCommits = Get-GitCommits -repoPath $repoPath -count $HistoryCount @verboseParam
    Write-Host "Loaded $($repoCommits.Count) commits from source repository" -ForegroundColor Green

    # Determine if we're using real repositories
    $useRealRepositories = $true
    if (-not (Test-Path -Path $vmrPath) -or -not (Test-Path -Path $repoPath)) {
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
    }

    # Find commits in repo that reference vmr commits via Source tags
    $crossRepoConnections = @()

    if ($useRealRepositories) {
        Write-Host "Finding Source tag changes in eng/Version.Details.xml..." -ForegroundColor Yellow
        # Increased count to find more source tag changes across a wider history

        # Check if Verbose is being used and pass it through
        $verboseParam = @{}
        if ($VerboseScript) {
            $verboseParam.Verbose = $true
        }

        $sourceTagChanges = Find-SourceTagChanges -repoPath $repoPath -filePath "eng/Version.Details.xml" -count $HistoryCount @verboseParam
        Write-Host "Found $($sourceTagChanges.Count) commits where Source tag actually changed" -ForegroundColor Green

        # Filter connections to only include commits that are in our diagram
        Write-Host "Checking which source tag changes match commits in our diagram..." -ForegroundColor Yellow

        # Keep track of external commits we need to add
        $externalVmrCommits = @{}

        foreach ($change in $sourceTagChanges) {
            $vmrCommitExists = Test-CommitInDiagram -commitSHA $change.SourceSHA -commits $vmrCommits @verboseParam
            $repoCommitExists = Test-CommitInDiagram -commitSHA $change.CommitSHA -commits $repoCommits @verboseParam

            if ($vmrCommitExists -and $repoCommitExists) {
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
                    $crossRepoConnections += $change
                    Write-Host "  + Added: repo $($change.ShortSHA) references external vmr $($change.ShortSourceSHA)" -ForegroundColor Cyan
                }
            }
        }

        Write-Host "Added $($crossRepoConnections.Count) cross-repository connections" -ForegroundColor Green
    } else {
        # In sample mode, generate more realistic fake connections
        Write-Host "Generating sample cross-repository connections..." -ForegroundColor Yellow

        # Create sample connections every few commits to simulate periodic dependency updates
        # This creates a more realistic pattern of dependency updates
        $vmrStep = [Math]::Max(1, [Math]::Floor($vmrCommits.Count / 5))
        $repoStep = [Math]::Max(1, [Math]::Floor($repoCommits.Count / 4))

        for ($i = 0; $i -lt 4; $i++) {
            $vmrIndex = [Math]::Min($vmrStep * $i + 1, $vmrCommits.Count - 1)
            $repoIndex = [Math]::Min($repoStep * $i, $repoCommits.Count - 1)

            $crossRepoConnections += [PSCustomObject]@{
                CommitSHA = $repoCommits[$repoIndex].CommitSHA
                ShortSHA = $repoCommits[$repoIndex].ShortSHA
                SourceSHA = $vmrCommits[$vmrIndex].CommitSHA
                ShortSourceSHA = $vmrCommits[$vmrIndex].ShortSHA
            }

            Write-Host "  - Added sample connection: repo $($repoCommits[$repoIndex].ShortSHA) references vmr $($vmrCommits[$vmrIndex].ShortSHA)" -ForegroundColor Cyan
        }

        Write-Host "Added 4 sample cross-repository connections" -ForegroundColor Green
    }

    # Create Mermaid diagram with cross-repository connections
    $diagramParams = @{
        vmrCommits = $vmrCommits
        repoCommits = $repoCommits
        vmrName = "VMR"
        repoName = (Split-Path -Path $repoPath -Leaf)
        crossRepoConnections = $crossRepoConnections
        collapseThreshold = $CollapseThreshold
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
    }

    # Add summary information to the diagram
    $diagramSummary = "%%% Git Commit Diagram - Generated $(Get-Date)`n"
    $diagramSummary += "%%% Repositories: vmr ($vmrPath) and repo ($repoPath)`n"
    $diagramSummary += "%%% Total Commits: $($vmrCommits.Count) vmr commits, $($repoCommits.Count) repo commits`n"
    $diagramSummary += "%%% Cross-Repository Connections: $($crossRepoConnections.Count) connections found`n"
    $diagramSummary += "%%% Collapse Threshold: $CollapseThreshold (NoCollapse: $NoCollapse)`n"

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
