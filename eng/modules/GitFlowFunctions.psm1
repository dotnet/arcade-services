# GitFlowFunctions.psm1
# Functions for working with Git repository flow and connections between repositories

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
                        $previousCommitSha = $repoInfo.SourceSHA
                        $manifestChanges += $repoInfo
                        if ($Verbose) {
                            Write-Host "  Added manifest change: VMR $($repoInfo.ShortSHA) -> repo $($repoInfo.ShortSourceSHA)" -ForegroundColor Cyan
                        }
                    } else {
                        if ($Verbose) {
                            Write-Host "  Skipped duplicate manifest reference: VMR $($repoInfo.ShortSHA) -> repo $($repoInfo.ShortSourceSHA)" -ForegroundColor DarkYellow
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
                            Write-Host "  Added Source tag change: repo $($change.ShortSHA) -> VMR $($change.ShortSourceSHA)" -ForegroundColor Cyan
                        }
                        $sourceTagChanges += $change
                        $previousSourceSHA = $change.SourceSHA
                    }
                    else {
                        if ($Verbose) {
                            Write-Host "  Skipped duplicate Source tag: repo $($change.ShortSHA) -> VMR $($change.ShortSourceSHA)" -ForegroundColor DarkYellow
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
function Get-MaxVmrDepth {
    param (
        [string]$repoPath,
        [string]$vmrPath,
        [array]$repoCommits,
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
                
                # Make sure the depth is within the acceptable range
                if ($commitDepth -lt $minDepth) {
                    Write-Host "  VMR depth ($commitDepth) is below minimum ($minDepth), using minimum depth" -ForegroundColor Yellow
                    return $minDepth
                }
                elseif ($commitDepth -gt $maxDepth) {
                    Write-Host "  VMR depth ($commitDepth) exceeds maximum ($maxDepth), using maximum depth" -ForegroundColor Yellow
                    return $maxDepth
                }
                else {
                    return $commitDepth
                }
            }
        }
    } catch {
        Write-Host "  Could not analyze VMR commit $($sourceSha.ShortSourceSHA): $_" -ForegroundColor Red
    }

    # If we couldn't determine an optimal depth, use a default value (2x the repo depth)
    $defaultVmrDepth = $defaultDepth * 2
    Write-Host "  Could not determine VMR history depth from repository references, using default of $defaultVmrDepth" -ForegroundColor Yellow
    return $defaultVmrDepth
}

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

# Helper function to check if a commit exists in an array of commits
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
            $blameOutput = git -C $repoPath blame -lL "$lineNumber,$lineNumber" "$currentCommit" -- "$filePath" 2>$null
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
        $blameOutput = git -C $vmrPath blame -lL "$lineNumber,$lineNumber" "$commitSHA" -- "$filePath" 2>$null
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

# Export functions
Export-ModuleMember -Function Get-GitRepositoryUrl
Export-ModuleMember -Function Get-CommitsThatChangedFile
Export-ModuleMember -Function Get-SourceManifestRepositoryInfo
Export-ModuleMember -Function Get-SourceTagMappingFromVersionDetails
Export-ModuleMember -Function Get-SourceTagShaFromCommit
Export-ModuleMember -Function Find-SourceManifestChanges
Export-ModuleMember -Function Find-SourceTagChanges
Export-ModuleMember -Function Show-VersionDetailsContent
Export-ModuleMember -Function Get-MaxVmrDepth
Export-ModuleMember -Function Get-GitCommits
Export-ModuleMember -Function Test-CommitInDiagram
Export-ModuleMember -Function Find-ForwardFlows
Export-ModuleMember -Function Find-BackFlows
Export-ModuleMember -Function Get-RepoShaFromSourceManifestJson
Export-ModuleMember -Function Get-VmrShaFromVersionDetailsXml
Export-ModuleMember -Function Get-BlameInfoForRepoInSourceManifest
