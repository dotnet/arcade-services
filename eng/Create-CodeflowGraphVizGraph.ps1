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
    [Parameter(Mandatory=$false, HelpMessage="Open browser with the generated GraphViz diagram")]
    [switch]$OpenInBrowser,
    [Parameter(Mandatory=$false, HelpMessage="Path to output the GraphViz diagram file")]
    [string]$OutputPath = "",
    [Parameter(Mandatory=$false, HelpMessage="Enable verbose output")]
    [Alias("v")]
    [switch]$VerboseScript,
    [Parameter(Mandatory=$false, HelpMessage="SHA(s) to force display and highlight (comma-separated or array)")]
    [string[]]$HighlightedCommits = @()
)

# This script loads Git commits from a specified repository and a VMR and generates a GraphViz diagram.
# The diagram shows forward flows and backflows between the VMR and the repository.
#
# The script either outputs the diagram to stdout, into a file or opens it in a browser.
#
# Example usage:
# .\Create-CodeflowGraphVizGraph.ps1 -RepoPath "C:\path\to\repo" -VmrPath "C:\path\to\vmr" -OpenInBrowser -Depth 50
#
# To highlight specific commits (force display and visual highlighting):
# .\Create-CodeflowGraphVizGraph.ps1 -RepoPath "C:\path\to\repo" -VmrPath "C:\path\to\vmr" -HighlightedCommits "abc1234","def5678" -OpenInBrowser

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
        [string[]]$highlightedCommits = @(), # Array of SHA(s) to force display and highlight
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
    $diagram += "// Highlighted Commits: $($highlightedCommits.Count) commits forced to display and highlighted in orange`n"
    $diagram += "// Note: Commit nodes are clickable and link to the repository`n`n"
    # Use a consistent prefix with the GraphViz diagram
    $diagram += "digraph G {`n"
    $diagram += "  rankdir=TB;  // top to bottom flow overall`n"
    $diagram += "  splines=false;  // Straight arrows`n"
    $diagram += "  outputorder=edgesfirst;  // Nodes on top (covering arrows)`n"
    $diagram += "  node [shape=box, style=filled, fillcolor=white, fontcolor=blue, fontname=`"Arial`", fontsize=10];`n`n"
    
    # First identify all commits involved in cross-repo references
    # It's important to mark both source and target commits in both repos as referenced
    # This ensures they won't be collapsed, which could break cross-repo connections
    $referencedVmrCommits = @{}
    $referencedRepoCommits = @{}

    # Create a set of highlighted commit SHAs (normalized for lookup)
    $highlightedCommitSHAs = @{}
    foreach ($sha in $highlightedCommits) {
        if ([string]::IsNullOrWhiteSpace($sha)) { continue }
        $normalizedSHA = $sha.Trim()
        $highlightedCommitSHAs[$normalizedSHA] = $true
        if ($Verbose) {
            Write-Host "  Will highlight and force display of commit: $normalizedSHA" -ForegroundColor Magenta
        }
    }    # First pass: identify all commits involved in any type of cross-repo connection
    foreach ($connection in $crossRepoConnections) {
        $referencedVmrCommits[$connection.VMRCommitSHA] = $true  # Mark VMR commit as referenced
        $referencedRepoCommits[$connection.RepoCommitSHA] = $true # Mark repo commit as referenced

        if ($Verbose) {
            Write-Host "  Marking VMR commit $($connection.VMRCommitSHA) as referenced"
            Write-Host "  Marking repo commit $($connection.RepoCommitSHA) as referenced"
        }
    }

    # Second pass: mark highlighted commits as referenced to prevent them from being collapsed
    foreach ($vmrCommit in $vmrCommits) {
        if ($highlightedCommitSHAs.ContainsKey($vmrCommit.CommitSHA) -or 
            $highlightedCommitSHAs.ContainsKey($vmrCommit.ShortSHA)) {
            $referencedVmrCommits[$vmrCommit.CommitSHA] = $true
            if ($Verbose) {
                Write-Host "  Marking highlighted VMR commit $($vmrCommit.CommitSHA) as referenced (forced display)" -ForegroundColor Magenta
            }
        }
    }

    foreach ($repoCommit in $repoCommits) {
        if ($highlightedCommitSHAs.ContainsKey($repoCommit.CommitSHA) -or 
            $highlightedCommitSHAs.ContainsKey($repoCommit.ShortSHA)) {
            $referencedRepoCommits[$repoCommit.CommitSHA] = $true
            if ($Verbose) {
                Write-Host "  Marking highlighted repo commit $($repoCommit.CommitSHA) as referenced (forced display)" -ForegroundColor Magenta
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

    # Clear collapsed nodes for repo commits
    $collapsedNodes = @{}

    # Add repo header node
    $diagram += "`n  // Right column nodes for $repoName repository with SHA labels and URLs`n"
    $repoHeaderNodeId = ($repoName -replace '/','_') -replace '\\.','_' -replace '-','_' # Ensure valid ID
    $diagram += "  $repoHeaderNodeId [label=`"$repoName`", fillcolor=lightyellow" # Added fillcolor
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
            if ($range.CommitSHA -contains $commit.CommitSHA) {
                # If it's the first commit in the range, create a collapsed node
                if ($commit.CommitSHA -eq $firstCommit.CommitSHA) {
                    if ([string]::IsNullOrEmpty($firstCommit.ShortSHA) -or [string]::IsNullOrEmpty($lastCommit.ShortSHA)) {
                        if ($Verbose) { Write-Warning "Repo Collapsed Range: Commit $($firstCommit.CommitSHA) or $($lastCommit.CommitSHA) has null/empty ShortSHA. Node ID might be invalid." }
                    }
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
                    if ($Verbose) { Write-Host "  Repo Nodes: Added collapsed node ID '$collapseId' to `$repoNodeIds." -ForegroundColor DarkMagenta }

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
            if ([string]::IsNullOrEmpty($commit.ShortSHA)) {
                if ($Verbose) { Write-Warning "Repo Individual Node: Commit $($commit.CommitSHA) has a null or empty ShortSHA. Node ID might be invalid." }
            }
            $shortSHA = $commit.ShortSHA
            $nodeId = "repo___$shortSHA"  # Prefix with repo___ to ensure valid GraphViz identifier
            $repoNodeIds += $nodeId
            if ($Verbose) { Write-Host "  Repo Nodes: Added individual node ID '$nodeId' to `$repoNodeIds." -ForegroundColor DarkMagenta }
            $diagram += "  $nodeId [label=`"$shortSHA`""

            # Check if this commit should be highlighted
            $isHighlighted = $highlightedCommitSHAs.ContainsKey($commit.CommitSHA) -or 
                           $highlightedCommitSHAs.ContainsKey($commit.ShortSHA)

            # Create single commit link if repo URL is provided
            if ($repoUrl) {
                $commitUrl = "$repoUrl/commit/$($commit.CommitSHA)"
                $diagram += ", URL=`"$commitUrl`", target=`"_blank`""
            }

            # Apply highlighting styles if this commit is highlighted
            if ($isHighlighted) {
                $diagram += ", fillcolor=`"orange`", fontcolor=`"black`", style=`"filled, bold`", penwidth=3"
                if ($Verbose) {
                    Write-Host "  Applied highlighting to repo commit $($commit.ShortSHA)" -ForegroundColor Magenta
                }
            } else {
                $diagram += ", fontcolor=`"blue`", style=`"filled, bold`""
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

        $diagram += "  $repoHeaderNodeId -> $($repoNodeIds[0]) [arrowhead=none, color=black];`n" # Use the stored header node ID
    }

    # Track collapsed nodes to know which SHAs to replace in the graph
    $collapsedNodes = @{}

    # Add repository header node
    $diagram += "  // Left column nodes for $vmrName repository with SHA labels and URLs`n"
    $vmrHeaderNodeId = ($vmrName -replace '/','_') -replace '\\.','_' # Ensure valid ID
    $diagram += "  $vmrHeaderNodeId [label=`"$vmrName`", fillcolor=lightyellow" # Added fillcolor
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
            if ($range.CommitSHA -contains $commit.CommitSHA) { # OPTIMIZATION/CLARIFICATION: Check if commit's SHA is in the list of SHAs for that range
                # If it's the first commit in the range, create a collapsed node
                if ($commit.CommitSHA -eq $range[0].CommitSHA) {
                    if ([string]::IsNullOrEmpty($range[0].ShortSHA) -or [string]::IsNullOrEmpty($range[-1].ShortSHA)) {
                        if ($Verbose) { Write-Warning "VMR Collapsed Range: Commit $($range[0].CommitSHA) or $($range[-1].CommitSHA) has a null or empty ShortSHA. Node ID might be invalid." }
                    }
                    $rangeIdx = [array]::IndexOf($vmrCollapsibleRanges, $range) # This might be fragile if ranges can have identical content but are different objects. Assuming ranges are unique.
                    $collapseId = "vmr_$($range[0].ShortSHA)_$($range[-1].ShortSHA)_$rangeIdx"
                    # Create node for collapsed range with proper label showing the commit range
                    $label = "$($firstCommit.ShortSHA) ... $($lastCommit.ShortSHA)\n[$($range.Count) commits]"

                    # Properly escape label for GraphViz format
                    $diagram += "  $collapseId [label=`"$label`""

                    # For ranges, use the compare URL pattern if repo URL is provided
                    if ($vmrRepoUrl) {
                        $compareUrl = "$vmrRepoUrl/compare/$($lastCommit.CommitSHA)...$($firstCommit.CommitSHA)"
                        $diagram += ", URL=`"$compareUrl`", target=`"_blank`", fontcolor=`"blue`", style=`"filled, bold`""
                    }
                    $diagram += "];`n"
                    $vmrNodeIds += $collapseId # Add ID of the collapsed node
                    if ($Verbose) { Write-Host "  VMR Nodes: Added collapsed node ID '$collapseId' to `$vmrNodeIds." -ForegroundColor DarkMagenta }

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
            if ([string]::IsNullOrEmpty($commit.ShortSHA)) {
                if ($Verbose) { Write-Warning "VMR Individual Node: Commit $($commit.CommitSHA) has a null or empty ShortSHA. Node ID might be invalid." }
            }
            $shortSHA = $commit.ShortSHA
            $nodeId = "vmr___$shortSHA"  # Prefix with vmr___ to ensure valid GraphViz identifier
            $vmrNodeIds += $nodeId # Add ID of the individual node
            if ($Verbose) { 
                Write-Host "  VMR Nodes: Added individual node ID '$nodeId' to `$vmrNodeIds." -ForegroundColor DarkMagenta 
            }
            $diagram += "  $nodeId [label=`"$shortSHA`""

            # Check if this commit should be highlighted
            $isHighlighted = $highlightedCommitSHAs.ContainsKey($commit.CommitSHA) -or 
                           $highlightedCommitSHAs.ContainsKey($commit.ShortSHA)

            # Create single commit link if repo URL is provided
            if ($vmrRepoUrl) {
                $commitUrl = "$vmrRepoUrl/commit/$($commit.CommitSHA)"
                $diagram += ", URL=`"$commitUrl`", target=`"_blank`""
            }

            # Apply highlighting styles if this commit is highlighted
            if ($isHighlighted) {
                $diagram += ", fillcolor=`"orange`", fontcolor=`"black`", style=`"filled, bold`", penwidth=3"
                if ($Verbose) {
                    Write-Host "  Applied highlighting to VMR commit $($commit.ShortSHA)" -ForegroundColor Magenta
                }
            } else {
                $diagram += ", fontcolor=`"blue`", style=`"filled, bold`""
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

        $diagram += "  $vmrHeaderNodeId -> $($vmrNodeIds[0]) [arrowhead=none, color=black];`n" # Use the stored header node ID
    }

    # Add cross-repository connections
    if ($crossRepoConnections -and $crossRepoConnections.Count -gt 0) {
        $diagram += "`n  // Cross-repository connections with clickable URLs and colored arrows`n"

        # Keep track of which connections we've already created to avoid duplicates
        $processedConnections = @{}

        foreach ($connection in $crossRepoConnections) {
            $isBackflow = $connection.ConnectionType -eq "BackFlow"
            $linkColor = if ($isBackflow) { "red" } else { "green" }

            $actualSourceCommitSHA = if ($isBackflow) { $connection.VMRCommitSHA } else { $connection.RepoCommitSHA }
            $actualTargetCommitSHA = if ($isBackflow) { $connection.RepoCommitSHA } else { $connection.VMRCommitSHA }

            $finalSourceNodeId = ""
            $finalTargetNodeId = ""

            if ($Verbose) {
                Write-Host "Attempting to draw connection: Type '$($connection.ConnectionType)', SourceSHA '$actualSourceCommitSHA', TargetSHA '$actualTargetCommitSHA'"
            }

            # Determine Source Node ID
            if ($isBackflow) { # Source is VMR
                $sourceCommitObject = $vmrCommits | Where-Object { $_.CommitSHA -eq $actualSourceCommitSHA } | Select-Object -First 1
                if ($sourceCommitObject) {
                    $finalSourceNodeId = "vmr___$($sourceCommitObject.ShortSHA)" # Default individual node ID
                    foreach ($range in $vmrCollapsibleRanges) {
                        if ($range | Where-Object { $_.CommitSHA -eq $sourceCommitObject.CommitSHA }) {
                            $rangeIdx = [array]::IndexOf($vmrCollapsibleRanges, $range)
                            $finalSourceNodeId = "vmr_$($range[0].ShortSHA)_$($range[-1].ShortSHA)_$rangeIdx"
                            if ($Verbose) { Write-Host "  Source VMR commit $($sourceCommitObject.ShortSHA) is part of collapsed range $finalSourceNodeId" }
                            break
                        }
                    }
                } elseif ($actualSourceCommitSHA) {
                    if ($Verbose) { Write-Host "  Source VMR commit $actualSourceCommitSHA (from connection) not found in \$vmrCommits array." -ForegroundColor DarkYellow }
                    continue
                } else {
                     if ($Verbose) { Write-Host "  Source VMR commit SHA is null or empty in connection object." -ForegroundColor DarkYellow }
                    continue
                }
            } else { # Source is Repo
                $sourceCommitObject = $repoCommits | Where-Object { $_.CommitSHA -eq $actualSourceCommitSHA } | Select-Object -First 1
                if ($sourceCommitObject) {
                    $finalSourceNodeId = "repo___$($sourceCommitObject.ShortSHA)" # Default individual node ID
                    foreach ($range in $repoCollapsibleRanges) {
                        if ($range | Where-Object { $_.CommitSHA -eq $sourceCommitObject.CommitSHA }) {
                            $rangeIdx = [array]::IndexOf($repoCollapsibleRanges, $range)
                            $finalSourceNodeId = "repo_$($range[0].ShortSHA)_$($range[-1].ShortSHA)_$rangeIdx"
                            if ($Verbose) { Write-Host "  Source Repo commit $($sourceCommitObject.ShortSHA) is part of collapsed range $finalSourceNodeId" }
                            break
                        }
                    }
                } elseif ($actualSourceCommitSHA) {
                    if ($Verbose) { Write-Host "  Source Repo commit $actualSourceCommitSHA (from connection) not found in \$repoCommits array." -ForegroundColor DarkYellow }
                    continue
                } else {
                    if ($Verbose) { Write-Host "  Source Repo commit SHA is null or empty in connection object." -ForegroundColor DarkYellow }
                    continue
                }
            }

            # Determine Target Node ID
            if ($isBackflow) { # Target is Repo
                $targetCommitObject = $repoCommits | Where-Object { $_.CommitSHA -eq $actualTargetCommitSHA } | Select-Object -First 1
                if ($targetCommitObject) {
                    $finalTargetNodeId = "repo___$($targetCommitObject.ShortSHA)" # Default individual node ID
                    foreach ($range in $repoCollapsibleRanges) {
                        if ($range | Where-Object { $_.CommitSHA -eq $targetCommitObject.CommitSHA }) {
                            $rangeIdx = [array]::IndexOf($repoCollapsibleRanges, $range)
                            $finalTargetNodeId = "repo_$($range[0].ShortSHA)_$($range[-1].ShortSHA)_$rangeIdx"
                            if ($Verbose) { Write-Host "  Target Repo commit $($targetCommitObject.ShortSHA) is part of collapsed range $finalTargetNodeId" }
                            break
                        }
                    }
                } elseif ($actualTargetCommitSHA) {
                    if ($Verbose) { Write-Host "  Target Repo commit $actualTargetCommitSHA (from connection) not found in \$repoCommits array." -ForegroundColor DarkYellow }
                    $finalTargetNodeId = "repo_commit_$($actualTargetCommitSHA.Substring(0,7))_NOT_IN_LOADED_REPO_COMMITS"
                } else {
                    Write-Host "  Target Repo commit SHA is null or empty in connection object." -ForegroundColor DarkYellow
                }
            } else { # Target is VMR
                $targetCommitObject = $vmrCommits | Where-Object { $_.CommitSHA -eq $actualTargetCommitSHA } | Select-Object -First 1
                if ($targetCommitObject) {
                    $finalTargetNodeId = "vmr___$($targetCommitObject.ShortSHA)" # Default individual node ID
                    foreach ($range in $vmrCollapsibleRanges) {
                        if ($range | Where-Object { $_.CommitSHA -eq $targetCommitObject.CommitSHA }) {
                            $rangeIdx = [array]::IndexOf($vmrCollapsibleRanges, $range)
                            $finalTargetNodeId = "vmr_$($range[0].ShortSHA)_$($range[-1].ShortSHA)_$rangeIdx"
                            if ($Verbose) { Write-Host "  Target VMR commit $($targetCommitObject.ShortSHA) is part of collapsed range $finalTargetNodeId" }
                            break
                        }
                    }
                } elseif ($actualTargetCommitSHA) {
                    if ($Verbose) { Write-Host "  Target VMR commit $actualTargetCommitSHA (from connection) not found in \$vmrCommits array." -ForegroundColor DarkYellow }
                    continue
                } else {
                    if ($Verbose) { Write-Host "  Target VMR commit SHA is null or empty in connection object." -ForegroundColor DarkYellow }
                    continue
                }
            }

            # Check if both determined node IDs exist in the graph's node lists
            $sourceNodeExists = if ($isBackflow) { $vmrNodeIds -contains $finalSourceNodeId } else { $repoNodeIds -contains $finalSourceNodeId }
            $targetNodeExists = if ($isBackflow) { $repoNodeIds -contains $finalTargetNodeId } else { $vmrNodeIds -contains $finalTargetNodeId }

            if ($sourceNodeExists -and $targetNodeExists) {
                $linkId = "link_$($connection.ConnectionType)_$($finalSourceNodeId)_to_$finalTargetNodeId"
                if (-not $processedConnections.ContainsKey($linkId)) {
                    $processedConnections[$linkId] = $true
                    $linkUrl = ""
                    if ($isBackflow -and $vmrRepoUrl) { # VMR -> Repo, link is to VMR commit
                        $linkUrl = "$vmrRepoUrl/commit/$actualSourceCommitSHA"
                    } elseif (-not $isBackflow -and $repoUrl) { # Repo -> VMR, link is to Repo commit
                        $linkUrl = "$repoUrl/commit/$actualSourceCommitSHA"
                    }

                    $diagram += "  $finalSourceNodeId -> $finalTargetNodeId [penwidth=3, constraint=false, color=$linkColor"

                    # Force the direction of the arrow
                    if ($connection.ConnectionType -eq "BackFlow") {
                        $diagram += ", tailport=w, headport=e"
                    } else {
                        $diagram += ", tailport=e, headport=w"
                    }

                    if ($linkUrl) {
                        $diagram += ", URL=`"$linkUrl`", target=`"_blank`""
                    }
                    $diagram += "];`n"
                    if ($Verbose) { Write-Host "  Successfully added connection from '$finalSourceNodeId' to '$finalTargetNodeId'" -ForegroundColor Green }
                } else {
                    if ($Verbose) { Write-Host "  Skipping duplicate connection from '$finalSourceNodeId' to '$finalTargetNodeId'" -ForegroundColor DarkGray }
                }
            } else {
                if ($Verbose) { 
                    Write-Host "  Skipping $($connection.ConnectionType) connection: Source '$finalSourceNodeId' (from SHA $actualSourceCommitSHA) or Target '$finalTargetNodeId' (from SHA $actualTargetCommitSHA) not found in generated graph node ID lists." -ForegroundColor DarkYellow
                    if (-not $sourceNodeExists) { Write-Host "    Source Node ID '$finalSourceNodeId' (for SHA $actualSourceCommitSHA) NOT found in its respective graph node list." }
                    if (-not $targetNodeExists) { Write-Host "    Target Node ID '$finalTargetNodeId' (for SHA $actualTargetCommitSHA) NOT found in its respective graph node list." }
                }
            }
            $crossRepoLinkIndex++
        }
    }

    # Close the diagram
    $diagram += "}"

    return $diagram
}

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
                    Write-Host "  Found Source tag with SHA: $sourceSHA" -ForegroundColor Green
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
                        Write-Host "  Found Sha element near Source: $sourceSHA" -ForegroundColor Green
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
                            Write-Host "  Found Source and Sha elements: $sourceSHA" -ForegroundColor Green
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
                            Write-Host "  Found Sha in Dependency: $sourceSHA" -ForegroundColor Green
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

function Get-MaxVmrDepth {
    param (
        [string]$repoPath,
        [string]$vmrPath,
        [array]$repoCommits,
        [int]$minDepth = 1,
        [switch]$Verbose
    )

    $verboseParam = @{}
    if ($Verbose) {
        $verboseParam.Verbose = $true
        Write-Verbose "Determining optimal VMR depth..."
    }
    
    # Only look at the most recent commit from the repo to determine VMR depth
    Write-Host "Checking latest commit for VMR references..." -ForegroundColor Yellow

    # Get the most recent commit (first one in the array)
    $latestCommit = $repoCommits[$repoCommits.Count - 1]
    
    if ($Verbose) {
        Write-Host "  Using latest commit $($latestCommit.ShortSHA) to determine VMR depth" -ForegroundColor Cyan
    }
    
    # Extract the Source tag SHA from this commit
    $sourceSha = Get-SourceTagShaFromCommit -repoPath $repoPath -commitSha $latestCommit.CommitSHA -filePath "eng/Version.Details.xml" @verboseParam

    # Use the source SHA from the latest commit to determine VMR depth
    try {
        # First check if the commit exists in VMR history
        $commitExists = git -C $vmrPath cat-file -e $sourceSha.SourceSHA 2>$null
        if ($? -eq $false) {
            if ($Verbose) {
                Write-Host "  VMR commit $($sourceSha.ShortSourceSHA) not found in VMR history" -ForegroundColor Yellow
            }
        }
        else {
            # Find how many commits back this is from HEAD
            $commitAge = git -C $vmrPath rev-list --count "$($sourceSha.SourceSHA)..HEAD" 2>$null
            if ($commitAge) {
                $commitDepth = [int]$commitAge
                if ($Verbose) {
                    Write-Host "  VMR commit $($sourceSha.ShortSourceSHA) is $commitDepth commits old" -ForegroundColor Cyan
                }

                return $commitDepth
            }
        }
    } catch {
        Write-Host "  Could not analyze VMR commit $($sourceSha.ShortSourceSHA): $_" -ForegroundColor Red
    }

    Write-Host "  Could not find matching VMR commit range. Make sure to check out the right branches and pull" -ForegroundColor Red
    return 100
}

function Get-GitCommits {
    param (
        [string]$repoPath,
        [int]$count = 30,
        [switch]$Verbose
    )

    # Get the commit history in format: <sha> <parent-sha>
    # This gives us both the commit SHA and its parent commit SHA
    # We use the %P format to get all parent SHAs (important for merge commits)
    $commits = git -C $repoPath log -n $count --format="%H %P"

    # Convert the raw git log output into structured objects
    $commitObjects = @()
    foreach ($commit in $commits) {
        $parts = $commit.Trim().Split(' ')
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

# Function to find forward flows (source repo commit to VMR commit relationships)
function Find-ForwardFlows {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory=$true)]
        [string]$vmrPath,
        [Parameter(Mandatory=$true)]
        [string]$repoMapping,
        [string]$filePath = "src/source-manifest.json",
        [int]$depth = 20
    )

    Write-Verbose "Finding forward flows for repository mapping '$repoMapping' with depth $depth..."

    $forwardFlows = @()
    $currentCommit = git -C $vmrPath rev-parse HEAD # Start from the latest commit
    $processedCommits = @{} # Track processed commits to avoid loops
    $currentDepth = 0

    while ($currentCommit -and $currentDepth -lt $depth) {
        # Check if we've already processed this commit
        if ($processedCommits.ContainsKey($currentCommit)) {
            Write-Verbose "Already processed commit $currentCommit, stopping recursion"
            break
        }

        # Mark this commit as processed
        $processedCommits[$currentCommit] = $true

        $shortSHA = $currentCommit.Substring(0, 7)
        Write-Verbose "Processing commit $shortSHA (depth $currentDepth of $depth)..."

        # 1. Get the repository commit SHA from source-manifest.json
        $repoSha = Get-RepoShaFromSourceManifestJson -vmrPath $vmrPath -commitSHA $currentCommit -repoMapping $repoMapping -filePath $filePath

        if (-not $repoSha) {
            Write-Verbose "No commitSha found for repository $repoMapping at commit $shortSHA, stopping"
            break
        }

        # 2. Get blame information for this commit
        $blameInfo = Get-BlameInfoForRepoInSourceManifest -vmrPath $vmrPath -commitSHA $currentCommit -repoMapping $repoMapping -filePath $filePath

        if ($blameInfo) {
            $flow = [PSCustomObject]@{
                VMRCommitSHA = $blameInfo.BlamedCommitSha
                RepoCommitSHA = $repoSha
                RepoMapping = $repoMapping
                Depth = $currentDepth
                ConnectionType = "ForwardFlow"
            }

            $forwardFlows += $flow

            Write-Verbose "Added forward flow Repo:$($flow.RepoCommitSHA.Substring(0, 7)) -> VMR:$($flow.VMRCommitSHA.Substring(0, 7))"

            # 4. Continue from the blamed commit
            # Get the parent commit of the blamed commit instead of using the blamed commit directly
            $parentCommit = git -C $vmrPath log -n 1 --format="%P" $flow.VMRCommitSHA 2>$null
            if ($parentCommit) {
                $currentCommit = $parentCommit
                Write-Verbose "Moving to parent commit $($parentCommit.Substring(0, 7)) of blamed commit $($flow.VMRCommitSHA.Substring(0, 7))"
            } else {
                Write-Verbose "Could not find parent commit of $($flow.VMRCommitSHA.Substring(0, 7)), stopping"
                break
            }
        }
        else {
            Write-Verbose "Could not get blame information for commit $currentCommit, stopping"
            break
        }

        # Recalculate the depth based on distance from HEAD to avoid excessive recursion
        $distanceFromHead = git -C $vmrPath rev-list --count "$currentCommit..HEAD" 2>$null
        if ($distanceFromHead -and [int]$distanceFromHead -gt $currentDepth) {
            $currentDepth = [int]$distanceFromHead
            Write-Verbose "Updated depth to $currentDepth based on distance from HEAD"
        }
    }

    return $forwardFlows
}

# Function to find backflows (VMR commit to source repo commit relationships)
function Find-BackFlows {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory=$true)]
        [string]$repoPath,
        [string]$filePath = "eng/Version.Details.xml",
        [int]$depth = 20
    )

    Write-Verbose "Finding backflows in repository with depth $depth..."

    $backFlows = @()
    $currentCommit = git -C $repoPath rev-parse HEAD  # Start from the latest commit
    $processedCommits = @{}  # Track processed commits to avoid loops
    $currentDepth = 0

    while ($currentCommit -and $currentDepth -lt $depth) {
        # Check if we've already processed this commit
        if ($processedCommits.ContainsKey($currentCommit)) {
            Write-Verbose "Already processed commit $currentCommit, stopping recursion"
            break
        }

        # Mark this commit as processed
        $processedCommits[$currentCommit] = $true

        # Safe substring to avoid out of range error
        $shortSHA = $currentCommit.Substring(0, 7)
        Write-Verbose "Processing commit $shortSHA (depth $currentDepth of $depth)..."

        # 1. Check if the Version.Details.xml exists at this commit
        $fileExists = git -C $repoPath cat-file -e "$currentCommit`:$filePath" 2>$null
        if ($? -eq $false) {
            Write-Verbose "File $filePath doesn't exist in commit $shortSHA, stopping"
            break
        }

        # 2. Get the VMR commit SHA from Version.Details.xml
        $vmrSha = Get-VmrShaFromVersionDetailsXml -repoPath $repoPath -commitSHA $currentCommit -filePath $filePath

        if (-not $vmrSha) {
            Write-Verbose "No VMR SHA found in commit $shortSHA, stopping"
            break
        }

        # 3. Find who changed the SHA line using git blame
        $lineNumber = $null
        $lines = git -C $repoPath show "$currentCommit`:$filePath" | Select-String -Pattern "<Sha>|Sha=" | ForEach-Object { $_.LineNumber }
        if ($lines.Count -gt 0) {
            $lineNumber = $lines[0]  # Just use the first one for now
        }

        $blamedCommit = $null
        if ($lineNumber) {
            $blameOutput = git -C $repoPath blame --first-parent -lL "$lineNumber,$lineNumber" "$currentCommit" -- "$filePath" 2>$null
            if ($blameOutput -match '^([0-9a-f]+)') {
                $blamedCommit = $matches[1]
            }
        }

        # If we couldn't get blame info, just use the current commit
        if (-not $blamedCommit) {
            $blamedCommit = $currentCommit
            Write-Verbose "Could not get blame information, using current commit as blamed commit"
        }

        # 4. Create the flow object
        $flow = [PSCustomObject]@{
            RepoCommitSHA = $blamedCommit
            VMRCommitSHA = $vmrSha
            Depth = $currentDepth
            ConnectionType = "BackFlow"
        }

        $backFlows += $flow

        Write-Verbose "Added backflow: VMR:$($flow.VMRCommitSHA.Substring(0, 7)) -> Repo:$($flow.RepoCommitSHA.Substring(0, 7))"

        # 5. Continue from the blamed commit
        # Get the parent commit of the blamed commit instead of using the blamed commit directly
        $parentCommit = git -C $repoPath log -n 1 --format="%P" $blamedCommit 2>$null
        if ($parentCommit) {
            $currentCommit = $parentCommit
            Write-Verbose "Moving to parent commit $($parentCommit.Substring(0, 7)) of blamed commit $($blamedCommit.Substring(0, 7))"
        } else {
            Write-Verbose "Could not find parent of blamed commit $($blamedCommit.Substring(0, 7)), stopping"
            break
        }

        # Recalculate the depth based on distance from HEAD to avoid excessive recursion
        $distanceFromHead = git -C $repoPath rev-list --count "$currentCommit..HEAD" 2>$null
        if ($distanceFromHead -and [int]$distanceFromHead -gt $currentDepth) {
            $currentDepth = [int]$distanceFromHead
            Write-Verbose "Updated depth to $currentDepth based on distance from HEAD"
        }
    }

    return $backFlows
}

# Function to find the repository commit SHA for a given repo mapping in source-manifest.json
function Get-RepoShaFromSourceManifestJson {
    [CmdletBinding()]
    param (
        [string]$vmrPath,
        [string]$commitSHA,
        [string]$repoMapping,
        [string]$filePath = "src/source-manifest.json"
    )

    # Get the content of source-manifest.json at specific commit
    try {
        # Get the object ID (blob) for the file in this commit
        $blobId = git -C $vmrPath rev-parse "$commitSHA`:$filePath" 2>$null

        # Check if the file exists in this commit
        if (-not $blobId) {
            # Safe substring to avoid out of range error
            $shortSHA = if ($commitSHA.Length -ge 7) { $commitSHA.Substring(0, 7) } else { $commitSHA }
            Write-Verbose "File $filePath doesn't exist in commit $shortSHA"
            return $null
        }

        # Use git cat-file to get the content directly without creating a temp file
        $fileContent = git -C $vmrPath cat-file -p $blobId

        # Parse the JSON
        $manifest = $fileContent | ConvertFrom-Json

        # Find the repository entry with the matching path
        foreach ($repo in $manifest.repositories) {
            if ($repo.path -eq $repoMapping) {
                # Safe substring to avoid out of range error
                $shortSHA = if ($commitSHA.Length -ge 7) { $commitSHA.Substring(0, 7) } else { $commitSHA }
                Write-Verbose "Found repository with path '$repoMapping' in manifest at commit $shortSHA`: $($repo.commitSha)"
                return $repo.commitSha
            }
        }

        # Safe substring to avoid out of range error
        $shortSHA = if ($commitSHA.Length -ge 7) { $commitSHA.Substring(0, 7) } else { $commitSHA }
        Write-Verbose "No repository with path '$repoMapping' found in manifest at commit $shortSHA"
        return $null
    }
    catch {
        # Safe substring to avoid out of range error
        $shortSHA = if ($commitSHA.Length -ge 7) { $commitSHA.Substring(0, 7) } else { $commitSHA }
        Write-Verbose "Error parsing source-manifest.json at commit $shortSHA`: $_"
        return $null
    }
}

# Function to find the VMR commit SHA for a given Sha attribute in Version.Details.xml
function Get-VmrShaFromVersionDetailsXml {
    [CmdletBinding()]
    param (
        [string]$repoPath,
        [string]$commitSHA,
        [string]$filePath = "eng/Version.Details.xml"
    )

    try {
        # Get the object ID (blob) for the file in this commit
        $blobId = git -C $repoPath rev-parse "$commitSHA`:$filePath" 2>$null
        $shortSHA = $commitSHA.Substring(0, 7)

        # Check if the file exists in this commit
        if (-not $blobId) {
            Write-Verbose "File $filePath doesn't exist in commit $shortSHA"
            return $null
        }

        # Use git cat-file to get the content directly without creating a temp file
        $fileContent = git -C $repoPath cat-file -p $blobId

        # Try to parse the XML
        try {
            $xml = New-Object System.Xml.XmlDocument
            $xml.LoadXml($fileContent)

            # Look for the Source tag with Sha attribute
            $source = $xml.SelectSingleNode("//*[local-name()='Source' and @Sha]")
            if ($source) {
                $sourceSHA = $source.GetAttribute("Sha")
                if ($sourceSHA -match '^[0-9a-f]+$') {
                    Write-Verbose "Found Source tag with SHA: $sourceSHA in commit $shortSHA"
                    return $sourceSHA
                }
            }

            # If not found, try with Sha element
            $sha = $xml.SelectSingleNode("//*[local-name()='Dependency']/*[local-name()='Source']/parent::*/*[local-name()='Sha']")
            if ($sha) {
                $sourceSHA = $sha.InnerText
                if ($sourceSHA -match '^[0-9a-f]+$') {
                    Write-Verbose "Found Sha element near Source: $sourceSHA in commit $shortSHA"
                    return $sourceSHA
                }
            }

            # Try more general approach
            $dependency = $xml.SelectSingleNode("//*[local-name()='Dependency']")
            if ($dependency) {
                $sourceNode = $dependency.SelectSingleNode("*[local-name()='Source']")
                $shaNode = $dependency.SelectSingleNode("*[local-name()='Sha']")

                if ($sourceNode -and $shaNode) {
                    $sourceSHA = $shaNode.InnerText
                    if ($sourceSHA -match '^[0-9a-f]+$') {
                        Write-Verbose "Found Source and Sha elements: $sourceSHA in commit $shortSHA"
                        return $sourceSHA
                    }
                }
            }

            # Try dependency with vmr repository
            $dep = $xml.SelectSingleNode("//*[local-name()='Dependency' and contains(@Repository, 'dotnet')]")
            if ($dep) {
                $shaNode = $dep.SelectSingleNode("*[local-name()='Sha']")
                if ($shaNode) {
                    $sourceSHA = $shaNode.InnerText
                    if ($sourceSHA -match '^[0-9a-f]+$') {
                        Write-Verbose "Found Sha in Dependency: $sourceSHA in commit $shortSHA"
                        return $sourceSHA
                    }
                }
            }
        }
        catch {
            # XML parsing failed, fall back to regex
            Write-Verbose "XML parsing failed for commit $shortSHA`: $_"
            Write-Verbose "Falling back to regex parsing"
        }

        # If XML parsing didn't work, try regex as fallback
        $lines = $fileContent -split "`n"
        foreach ($line in $lines) {
            if ($line -match '<Source.*Sha="([0-9a-f]+)".*>' -or
                $line -match 'Sha="([0-9a-f]+)".*<Source' -or
                $line -match '<Source[^>]*>[^<]*<Sha>([0-9a-f]+)</Sha>' -or
                $line -match '<Sha>([0-9a-f]+)</Sha>.*<Source' -or
                $line -match '<Sha>([0-9a-f]+)</Sha>') {

                $sourceSHA = $matches[1]
                Write-Verbose "Found SHA using regex: $sourceSHA in commit $shortSHA"
                return $sourceSHA
            }
        }

        Write-Verbose "No Source tag with SHA found in commit $shortSHA"
        return $null
    }
    catch {
        return $null
    }
}

# Function to get line number and blame info for a specific repository in source-manifest.json
function Get-BlameInfoForRepoInSourceManifest {
    [CmdletBinding()]
    param (
        [string]$vmrPath,
        [string]$commitSHA,
        [string]$repoMapping,
        [string]$filePath = "src/source-manifest.json"
    )
    $shortSHA = $commitSHA.Substring(0, 7)

    try {
        # Get the content of source-manifest.json at the specific commit
        $blobId = git -C $vmrPath rev-parse "$commitSHA`:$filePath" 2>$null
        if (-not $blobId) {
            Write-Verbose "File $filePath doesn't exist in commit $shortSHA"
            return $null
        }

        $fileContent = git -C $vmrPath cat-file -p $blobId

        # Parse the JSON to find the line number of the commitSha for the repository
        $lineNumber = $null
        $repoCommitSha = $null
        $lines = $fileContent -split "`n"

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]

            # First check if this line is in the section for our repository
            if ($line -match [regex]::Escape("`"path`": `"$repoMapping`"")) {
                # Now find the commitSha line in the next few lines
                for ($j = $i; $j -lt [Math]::Min($i + 10, $lines.Count); $j++) {
                    if ($lines[$j] -match '^\s*"commitSha":\s*"([0-9a-f]+)"') {
                        $lineNumber = $j + 1  # Git blame uses 1-based line numbers
                        $repoCommitSha = $matches[1]
                        break
                    }
                }

                if ($lineNumber) {
                    break
                }
            }
        }

        if (-not $lineNumber) {
            Write-Verbose "Could not find commitSha line for repository $repoMapping in commit $shortSHA"
            return $null
        }

        # Run git blame to find who changed this specific line
        $blameOutput = git -C $vmrPath blame --first-parent -lL "$lineNumber,$lineNumber" "$commitSHA" -- "$filePath" 2>$null
        if (-not $blameOutput) {
            Write-Verbose "Could not get blame information for line $lineNumber in commit $shortSHA"
            return $null
        }

        # Extract the commit SHA from the blame output
        if ($blameOutput -match '^([0-9a-f]+)') {
            $blamedCommitSha = $matches[1]

            Write-Verbose "Line $lineNumber with commitSha '$repoCommitSha' was last modified by commit $blamedCommitSha in $shortSHA"

            return [PSCustomObject]@{
                LineNumber = $lineNumber
                RepoCommitSha = $repoCommitSha
                BlamedCommitSha = $blamedCommitSha
            }
        }

        Write-Verbose "Could not parse blame output for commit $shortSHA`: $blameOutput"
        return $null
    }
    catch {
        Write-Verbose "Error getting blame information for commit $shortSHA`: $_"
        return $null
    }
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
    $vmrDepth = Get-MaxVmrDepth -repoPath $RepoPath -vmrPath $VmrPath -repoCommits $repoCommits -minDepth 10 @verboseParam

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
            # Updated verbose log to show full SHAs for clarity
            Write-Verbose "  + Added backflow: vmr $($flow.VMRCommitSHA.Substring(0,7)) ($($flow.ShortSHA)) -> repo $($flow.RepoCommitSHA.Substring(0,7)) ($($flow.ShortSourceSHA))"
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
    }    # Create Mermaid diagram with cross-repository connections
    $diagramParams = @{
        vmrCommits = $vmrCommits
        repoCommits = $repoCommits
        vmrName = "VMR"
        repoName = (Split-Path -Path $RepoPath -Leaf)
        crossRepoConnections = $crossRepoConnections
        collapseThreshold = $CollapseThreshold
        vmrRepoUrl = $vmrRepoUrl
        repoUrl = $sourceRepoUrl
        highlightedCommits = $HighlightedCommits
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

