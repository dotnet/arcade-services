[CmdletBinding()]
param(
    [Parameter(HelpMessage="Number of commits to retrieve from history")]
    [int]$HistoryCount = 50,
    [Parameter(Mandatory=$false, HelpMessage="Path to the repository")]
    [string]$repo = "D:\tmp\aspnetcore",
    [Parameter(Mandatory=$false, HelpMessage="Path to the VMR (Virtual Mono Repository)")]
    [string]$vmr = "D:\repos\dotnet",
    [switch]$VerboseScript
)

# This script loads Git commits from specified repositories and generates a Mermaid TD diagram
# The diagram shows commits as nodes in subgroups with parent-child relationships
# Use the -HistoryCount parameter to specify how many commits to retrieve from history
# Use the -repo parameter to specify the path to the source repository
# Use the -vmr parameter to specify the path to the VMR (Virtual Mono Repository)

# Function to get last N commits from a Git repository
function Get-GitCommits {
    param (
        [string]$repoPath,
        [int]$count = 30,
        [switch]$Verbose
    )

    # Get the commit history in format: <sha> <parent-sha>
    # This gives us both the commit SHA and its parent commit SHA
    # Removed --first-parent to include merge commits that might reference other repos
    $commits = git -C $repoPath log -n $count --format="%H %P" main

    # Convert the raw git log output into structured objects
    $commitObjects = @()
    foreach ($commit in $commits) {
        $parts = $commit.Split(' ')
        if ($parts.Count -ge 2) {
            $commitObjects += [PSCustomObject]@{
                CommitSHA = $parts[0]
                ParentSHA = $parts[1]
                ShortSHA = $parts[0].Substring(0, 7)  # Truncate to 7 chars
                ShortParentSHA = $parts[1].Substring(0, 7)  # Truncate to 7 chars
            }
        }
    }
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
        [switch]$Verbose
    )

    # Start building the Mermaid diagram string
    $diagram = "flowchart TD`r`n"

    # Add VMR repository subgraph
    $diagram += "    subgraph $vmrName`r`n"

    # Add nodes and connections for vmr
    foreach ($commit in $vmrCommits) {
        $diagram += "        $($commit.ShortSHA)[$($commit.ShortSHA)]`r`n"
    }

    # Add connections between commits (parent -> child)
    foreach ($commit in $vmrCommits) {
        # Find children of current commit
        $childCommit = $vmrCommits | Where-Object { $_.ParentSHA -eq $commit.CommitSHA }
        if ($childCommit) {
            $diagram += "        $($childCommit.ShortSHA)-->$($commit.ShortSHA)`r`n"
        }
    }

    # Close VMR repository subgraph
    $diagram += "    end`r`n"

    # Add source repository subgraph
    $diagram += "    subgraph $repoName`r`n"

    # Add nodes for repo
    foreach ($commit in $repoCommits) {
        $diagram += "        $($commit.ShortSHA)[$($commit.ShortSHA)]`r`n"
    }

    # Add connections between commits (parent -> child)
    foreach ($commit in $repoCommits) {
        # Find children of current commit
        $childCommit = $repoCommits | Where-Object { $_.ParentSHA -eq $commit.CommitSHA }
        if ($childCommit) {
            $diagram += "        $($childCommit.ShortSHA)-->$($commit.ShortSHA)`r`n"
        }
    }

    # Close repository subgraph
    $diagram += "    end`r`n"    # Add cross-repository connections (Source tag connections)
    if ($crossRepoConnections -and $crossRepoConnections.Count -gt 0) {
        $diagram += "`r`n    %% Cross-repository connections from Source tag references`r`n"
        $diagram += "    classDef backflowSourceCommit stroke:#0c0,stroke-width:2px,color:#0c0,stroke-dasharray: 5 5`r`n"
        $diagram += "    classDef externalCommit fill:#f99,stroke:#f66,stroke-width:1px,color:#000,stroke-dasharray: 5 5`r`n"
        $diagram += "    classDef backflowTargetCommit stroke:#0c0,stroke-width:2px,color:#0c0`r`n"

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

            # Check if the source commit (from vmr) exists in our diagram
            $vmrCommitExists = $false
            foreach ($commit in $vmrCommits) {
                if ($commit.CommitSHA -eq $connection.SourceSHA) {
                    $vmrCommitExists = $true
                    break
                }
            }

            # If the vmr commit doesn't exist in our diagram, we need to add it as an external reference
            if (-not $vmrCommitExists) {
                # Only add it once
                if (-not $externalVmrCommits.ContainsKey($connection.ShortSourceSHA)) {
                    $diagram += "    $($connection.ShortSourceSHA)[$($connection.ShortSourceSHA)*]`r`n"
                    $diagram += "    class $($connection.ShortSourceSHA) externalCommit`r`n"
                    $externalVmrCommits[$connection.ShortSourceSHA] = $true
                }
            }

            # Format: vmr commit <- repo commit (shows repo commit references vmr commit)
            $diagram += "    $($connection.ShortSourceSHA) -. forward flow .-> $($connection.ShortSHA)`r`n"

            # Apply styling for commits
            if ($vmrCommitExists) {
                $diagram += "    class $($connection.ShortSourceSHA) backflowSourceCommit`r`n"
            }
            $diagram += "    class $($connection.ShortSHA) backflowTargetCommit`r`n"
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

    $result = $null        # Try to parse the XML - if it's valid XML, we can extract Source tag more reliably
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
            }            }
    }
    catch {
        if ($Verbose) {
            Write-Host "XML parsing failed: $_" -ForegroundColor Red
        }
        # If XML parsing fails, fall back to regex parsing
    }# If XML parsing didn't find anything or failed, use regex as fallback
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
    if ($VerbosePreference -eq 'Continue') {
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

    if ($VerbosePreference -eq 'Continue') {
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
        if ($PSBoundParameters.ContainsKey('Verbose')) {
            $verboseParam.Verbose = $true
        }

        $sourceTagChanges = Find-SourceTagChanges -repoPath $repoPath -filePath "eng/Version.Details.xml" -count $HistoryCount @verboseParam
        Write-Host "Found $($sourceTagChanges.Count) commits where Source tag actually changed" -ForegroundColor Green

        # Filter connections to only include commits that are in our diagram
        Write-Host "Checking which source tag changes match commits in our diagram..." -ForegroundColor Yellow

        # Keep track of external commits we need to add
        $externalVmrCommits = @{
        }

        foreach ($change in $sourceTagChanges) {
            $vmrCommitExists = Test-CommitInDiagram -commitSHA $change.SourceSHA -commits $vmrCommits @verboseParam
            $repoCommitExists = Test-CommitInDiagram -commitSHA $change.CommitSHA -commits $repoCommits @verboseParam

            if ($vmrCommitExists -and $repoCommitExists) {
                $crossRepoConnections += $change
                Write-Host "  + Added: repo $($change.ShortSHA) references vmr $($change.ShortSourceSHA)" -ForegroundColor Green
            } else {
                # Record external vmr commits for later inclusion
                if (-not $vmrCommitExists) {
                    $externalVmrCommits[$change.SourceSHA] = $change
                    Write-Host "  ~ External: vmr commit $($change.ShortSourceSHA) will be added as external node" -ForegroundColor Magenta
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

        Write-Host "Added $($crossRepoConnections.Count) cross-repository connections (only from commits where Source tag changed)" -ForegroundColor Green
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
    $diagramText = Create-MermaidDiagram -vmrCommits $vmrCommits -repoCommits $repoCommits `
                                        -vmrName "vmr" -repoName "repo" `
                                        -crossRepoConnections $crossRepoConnections @verboseParam

    # Output file path
    $outputPath = Join-Path -Path $PSScriptRoot -ChildPath "git-commit-diagram.mmd"

    # Add summary information to the diagram
    $diagramSummary = "%%% Git Commit Diagram - Generated $(Get-Date)`r`n"
    $diagramSummary += "%%% Repositories: vmr ($vmrPath) and repo ($repoPath)`r`n"
    $diagramSummary += "%%% Total Commits: $($vmrCommits.Count) vmr commits, $($repoCommits.Count) repo commits`r`n"
    $diagramSummary += "%%% Cross-Repository Connections: $($crossRepoConnections.Count) connections found`r`n"
    $diagramText = $diagramSummary + $diagramText

    # Save the diagram to a file with explanation
    $diagramText | Out-File -FilePath $outputPath -Encoding utf8
    Write-Host "Mermaid diagram saved to: $outputPath" -ForegroundColor Green
}
catch {
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
}
