[CmdletBinding()]
param(
    [Parameter(HelpMessage="Number of commits to retrieve from history")]
    [int]$HistoryCount = 50,
    [switch]$VerboseScript
)

# This script loads Git commits from specified repositories and generates a Mermaid TD diagram
# The diagram shows commits as nodes in subgroups with parent-child relationships
# Use the -HistoryCount parameter to specify how many commits to retrieve from history

# Function to get last N commits from a Git repository
function Get-GitCommits {
    param (
        [string]$repoPath,
        [int]$count = 30,
        [switch]$Verbose
    )

    # Change to repository directory
    $currentDir = Get-Location
    Set-Location -Path $repoPath

    try {
        # Get the commit history in format: <sha> <parent-sha>
        # This gives us both the commit SHA and its parent commit SHA
        # Removed --first-parent to include merge commits that might reference other repos
        $commits = git log -n $count --format="%H %P" main

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
    finally {
        # Change back to original directory
        Set-Location -Path $currentDir
    }
}

# Function to create Mermaid diagram text from commits
function Create-MermaidDiagram {
    param (
        [array]$repo1Commits,
        [array]$repo2Commits,
        [string]$repo1Name,
        [string]$repo2Name,
        [array]$crossRepoConnections = @(),
        [switch]$Verbose
    )

    # Start building the Mermaid diagram string
    $diagram = "flowchart TD`r`n"

    # Add repository 1 subgraph
    $diagram += "    subgraph $repo1Name`r`n"
    
    # Add nodes and connections for repo1
    foreach ($commit in $repo1Commits) {
        $diagram += "        $($commit.ShortSHA)[$($commit.ShortSHA)]`r`n"
    }
    
    # Add connections between commits (parent -> child)
    foreach ($commit in $repo1Commits) {
        # Find children of current commit
        $childCommit = $repo1Commits | Where-Object { $_.ParentSHA -eq $commit.CommitSHA }
        if ($childCommit) {
            $diagram += "        $($childCommit.ShortSHA)-->$($commit.ShortSHA)`r`n"
        }
    }

    # Close repository 1 subgraph
    $diagram += "    end`r`n"

    # Add repository 2 subgraph
    $diagram += "    subgraph $repo2Name`r`n"
    
    # Add nodes for repo2
    foreach ($commit in $repo2Commits) {
        $diagram += "        $($commit.ShortSHA)[$($commit.ShortSHA)]`r`n"
    }
    
    # Add connections between commits (parent -> child)
    foreach ($commit in $repo2Commits) {
        # Find children of current commit
        $childCommit = $repo2Commits | Where-Object { $_.ParentSHA -eq $commit.CommitSHA }
        if ($childCommit) {
            $diagram += "        $($childCommit.ShortSHA)-->$($commit.ShortSHA)`r`n"
        }
    }

    # Close repository 2 subgraph
    $diagram += "    end`r`n"    # Add cross-repository connections (Source tag connections)
    if ($crossRepoConnections -and $crossRepoConnections.Count -gt 0) {
        $diagram += "`r`n    %% Cross-repository connections from Source tag references`r`n"
        $diagram += "    classDef sourceReference stroke:#f66,stroke-width:2px,color:#f66,stroke-dasharray: 5 5`r`n"
        $diagram += "    classDef externalCommit fill:#f99,stroke:#f66,stroke-width:1px,color:#000,stroke-dasharray: 5 5`r`n"
        $diagram += "    classDef crossReference stroke:#0c0,stroke-width:2px,color:#0c0`r`n"
        
        # Track which external commits we've added
        $externalDotnetCommits = @{}
        $processedConnections = @{}
        
        foreach ($connection in $crossRepoConnections) {
            # Create a unique identifier for this connection to avoid duplicates
            $connectionKey = "$($connection.ShortSourceSHA)_$($connection.ShortSHA)"
            
            # Skip if we've already processed this connection
            if ($processedConnections.ContainsKey($connectionKey)) {
                continue
            }
            
            # Mark this connection as processed
            $processedConnections[$connectionKey] = $true
            
            # Check if the source commit (from dotnet) exists in our diagram
            $dotnetCommitExists = $false
            foreach ($commit in $repo1Commits) {
                if ($commit.CommitSHA -eq $connection.SourceSHA) {
                    $dotnetCommitExists = $true
                    break
                }
            }
            
            # If the dotnet commit doesn't exist in our diagram, we need to add it as an external reference
            if (-not $dotnetCommitExists) {
                # Only add it once
                if (-not $externalDotnetCommits.ContainsKey($connection.ShortSourceSHA)) {
                    $diagram += "    $($connection.ShortSourceSHA)[$($connection.ShortSourceSHA)*]`r`n"
                    $diagram += "    class $($connection.ShortSourceSHA) externalCommit`r`n"
                    $externalDotnetCommits[$connection.ShortSourceSHA] = $true
                }
            }
            
            # Format: dotnet commit <- aspnetcore commit (shows aspnet commit references dotnet commit)
            # $linkId = "link_$($connection.ShortSourceSHA)_$($connection.ShortSHA)"
            $diagram += "    $($connection.ShortSourceSHA) -. forward flow .-> $($connection.ShortSHA)`r`n"
            # $diagram += "    linkStyle $linkId stroke:#0c0,stroke-width:1px,stroke-dasharray: 3 3`r`n"

            # Apply styling for commits
            if ($dotnetCommitExists) {
                $diagram += "    class $($connection.ShortSourceSHA) sourceReference`r`n"
            }
            $diagram += "    class $($connection.ShortSHA) crossReference`r`n"
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

    # Change to repository directory
    $currentDir = Get-Location
    Set-Location -Path $repoPath

    try {
        # Get commits that changed the specific file
        $commits = git log -n $count --format="%H" -- $filePath

        return $commits
    }
    finally {
        # Change back to original directory
        Set-Location -Path $currentDir
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

    # Change to repository directory
    $currentDir = Get-Location
    Set-Location -Path $repoPath
    try {
        # Get the object ID (blob) for the file in this commit
        $blobId = git rev-parse "$commitSHA`:$filePath" 2>$null
        
        # Check if the file exists in this commit
        if (-not $blobId) {
            if ($Verbose) {
                Write-Host "File $filePath doesn't exist in commit $commitSHA" -ForegroundColor DarkYellow
            }
            return $null
        }
        
        # Use git cat-file to get the content directly without creating a temp file
        $fileContent = git cat-file -p $blobId

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
            
            # 4. Try to find any Dependency with dotnet repository if still not found
            if ($result -eq $null) {
                $dep = $xml.SelectSingleNode("//*[local-name()='Dependency' and contains(@Repository, 'dotnet')]")
                if ($dep) {
                    $shaNode = $dep.SelectSingleNode("*[local-name()='Sha']")
                    if ($shaNode) {
                        $sourceSHA = $shaNode.InnerText
                        if ($sourceSHA -match '^[0-9a-f]+$') {
                            if ($Verbose) {
                                Write-Host "  Found dotnet Dependency with Sha: $sourceSHA" -ForegroundColor Green
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
    finally {
        # Change back to original directory
        Set-Location -Path $currentDir
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

    # Change to repository directory
    $currentDir = Get-Location
    Set-Location -Path $repoPath

    try {
        Write-Host "Examining XML in commit $commitSHA..." -ForegroundColor Yellow
        # Get the content of the file at the specific commit
        $fileContent = git show "$commitSHA`:$filePath" 2>$null
        
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
    finally {
        # Change back to original directory
        Set-Location -Path $currentDir
    }
}

# Main script execution

try {
    Write-Host "Generating Git commit Mermaid diagram..." -ForegroundColor Green

    # Repository paths
    $repo1Path = "D:\repos\dotnet"
    $repo2Path = "D:\tmp\aspnetcore"
    
    $repo1Commits = @()
    $repo2Commits = @()
      # Create a parameter hashtable for verbose if needed
    $verboseParam = @{}
    if ($VerbosePreference -eq 'Continue') {
        $verboseParam.Verbose = $true
        Write-Verbose "Verbose mode enabled"
    }    Write-Host "Loading commits from $repo1Path..." -ForegroundColor Yellow
    $repo1Commits = Get-GitCommits -repoPath $repo1Path -count $($HistoryCount*8) @verboseParam
    Write-Host "Loaded $($repo1Commits.Count) commits from first repository" -ForegroundColor Green
    
    Write-Host "Loading commits from $repo2Path..." -ForegroundColor Yellow
    $repo2Commits = Get-GitCommits -repoPath $repo2Path -count $HistoryCount @verboseParam
    Write-Host "Loaded $($repo2Commits.Count) commits from second repository" -ForegroundColor Green
    
    # Determine if we're using real repositories (missing from original script)
    $useRealRepositories = $true
    if (-not (Test-Path -Path $repo1Path) -or -not (Test-Path -Path $repo2Path)) {
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

    # Find commits in aspnetcore that reference dotnet commits via Source tags
    $crossRepoConnections = @()
    
    if ($useRealRepositories) {        Write-Host "Finding Source tag changes in eng/Version.Details.xml..." -ForegroundColor Yellow
        # Increased count to find more source tag changes across a wider history
        
        # Check if Verbose is being used and pass it through
        $verboseParam = @{}
        if ($PSBoundParameters.ContainsKey('Verbose')) {
            $verboseParam.Verbose = $true
        }
          $sourceTagChanges = Find-SourceTagChanges -repoPath $repo2Path -filePath "eng/Version.Details.xml" -count $HistoryCount @verboseParam
        Write-Host "Found $($sourceTagChanges.Count) commits where Source tag actually changed" -ForegroundColor Green
          # Filter connections to only include commits that are in our diagram
        Write-Host "Checking which source tag changes match commits in our diagram..." -ForegroundColor Yellow
        
        # Keep track of external commits we need to add
        $externalDotnetCommits = @{}
          foreach ($change in $sourceTagChanges) {
            $dotnetCommitExists = Test-CommitInDiagram -commitSHA $change.SourceSHA -commits $repo1Commits @verboseParam
            $aspnetCommitExists = Test-CommitInDiagram -commitSHA $change.CommitSHA -commits $repo2Commits @verboseParam
            
            if ($dotnetCommitExists -and $aspnetCommitExists) {
                $crossRepoConnections += $change
                Write-Host "  + Added: aspnetcore $($change.ShortSHA) references dotnet $($change.ShortSourceSHA)" -ForegroundColor Green
            } else {
                # Record external dotnet commits for later inclusion
                if (-not $dotnetCommitExists) {
                    $externalDotnetCommits[$change.SourceSHA] = $change
                    Write-Host "  ~ External: dotnet commit $($change.ShortSourceSHA) will be added as external node" -ForegroundColor Magenta
                }
                if (-not $aspnetCommitExists) {
                    Write-Host "  - Skipped: aspnetcore commit $($change.ShortSHA) not in loaded history" -ForegroundColor DarkYellow
                }
                
                # Always add the connection if aspnetcore commit exists
                if ($aspnetCommitExists) {
                    $crossRepoConnections += $change
                    Write-Host "  + Added: aspnetcore $($change.ShortSHA) references external dotnet $($change.ShortSourceSHA)" -ForegroundColor Cyan
                }
            }
        }
        
        Write-Host "Added $($crossRepoConnections.Count) cross-repository connections (only from commits where Source tag changed)" -ForegroundColor Green
    }    else {
        # In sample mode, generate more realistic fake connections
        Write-Host "Generating sample cross-repository connections..." -ForegroundColor Yellow
        
        # Create sample connections every few commits to simulate periodic dependency updates
        # This creates a more realistic pattern of dependency updates
        $dotnetStep = [Math]::Max(1, [Math]::Floor($repo1Commits.Count / 5))
        $aspnetStep = [Math]::Max(1, [Math]::Floor($repo2Commits.Count / 4))
        
        for ($i = 0; $i -lt 4; $i++) {
            $dotnetIndex = [Math]::Min($dotnetStep * $i + 1, $repo1Commits.Count - 1)
            $aspnetIndex = [Math]::Min($aspnetStep * $i, $repo2Commits.Count - 1)
            
            $crossRepoConnections += [PSCustomObject]@{
                CommitSHA = $repo2Commits[$aspnetIndex].CommitSHA
                ShortSHA = $repo2Commits[$aspnetIndex].ShortSHA
                SourceSHA = $repo1Commits[$dotnetIndex].CommitSHA
                ShortSourceSHA = $repo1Commits[$dotnetIndex].ShortSHA
            }
            
            Write-Host "  - Added sample connection: aspnetcore $($repo2Commits[$aspnetIndex].ShortSHA) references dotnet $($repo1Commits[$dotnetIndex].ShortSHA)" -ForegroundColor Cyan
        }
        
        Write-Host "Added 4 sample cross-repository connections" -ForegroundColor Green
    }    # Create Mermaid diagram with cross-repository connections
    $diagramText = Create-MermaidDiagram -repo1Commits $repo1Commits -repo2Commits $repo2Commits `
                                        -repo1Name "dotnet" -repo2Name "aspnetcore" `
                                        -crossRepoConnections $crossRepoConnections @verboseParam# Output file path
    $outputPath = Join-Path -Path $PSScriptRoot -ChildPath "git-commit-diagram.mmd"
    
    # Add summary information to the diagram
    $diagramSummary = "%%% Git Commit Diagram - Generated $(Get-Date)`r`n"
    $diagramSummary += "%%% Repositories: dotnet ($repo1Path) and aspnetcore ($repo2Path)`r`n"
    $diagramSummary += "%%% Total Commits: $($repo1Commits.Count) dotnet commits, $($repo2Commits.Count) aspnetcore commits`r`n"
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
