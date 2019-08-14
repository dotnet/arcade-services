param(
    [string]$maestroInstallation,
    [string]$darcVersion,
    [string]$maestroBearerToken,
    [string]$githubPAT,
    [string]$azdoPAT
)

$repo1Name = "maestro-test1"
$repo2Name = "maestro-test2"
$channel1Name = Get-Random
$channel2Name = Get-Random

try {
    Write-Host
    Write-Host "Darc/Maestro Subscription tests"
    Write-Host

    # Import common tooling and prep for tests
    . $PSScriptRoot/common.ps1

    Write-Host "Subscription management tests..."
    Write-Host

    $repo1Uri = Get-Github-RepoUri $repo1Name
    $repo2Uri = Get-Github-RepoUri $repo2Name
    $repo3Uri = Get-AzDO-RepoUri $repo1Name

    # Add new channels
    Write-Host "Creating channels '$channel1Name' and '$channel2Name'"
    Darc-Add-Channel -channelName $channel1Name -classification 'test'
    Darc-Add-Channel -channelName $channel2Name -classification 'test'

    Write-Host "Testing various command line parameters of add-subscription"
    $subscriptionId = Darc-Add-Subscription --channel "$channel1Name" --source-repo "$repo1Uri" --target-repo "$repo3Uri" --update-frequency everyWeek --standard-automerge --target-branch "master"
    $expectedSubscriptionInfo = @(
        "$repo1Uri \($channel1Name\) ==> '$repo3Uri' \('master'\)",
        "Id: .*",
        "  - Update Frequency: EveryWeek",
        "  - Enabled: True",
        "  - Batchable: False",
        "  - Merge Policies:",
        "    Standard"
    )
    Validate-Subscription-Info $subscriptionId $expectedSubscriptionInfo
    Darc-Delete-Subscription $subscriptionId

    $subscriptionId = Darc-Add-Subscription --channel "$channel1Name" --source-repo "$repo1Uri" --target-repo "$repo2Uri" --update-frequency none --all-checks-passed --no-extra-commits --no-requested-changes --ignore-checks "WIP,license/cla" --target-branch "master"
    $expectedSubscriptionInfo = @(
        "$repo1Uri \($channel1Name\) ==> '$repo2Uri' \('master'\)",
        "Id: .*",
        "  - Update Frequency: None",
        "  - Enabled: True",
        "  - Batchable: False",
        "  - Merge Policies:",
        "    NoExtraCommits",
        "    AllChecksSuccessful",
        "      ignoreChecks = ",
        "                       \[",
        "                         `"WIP`",",
        "                         `"license/cla`"",
        "                       \]",
        "    NoRequestedChanges"
    )
    Validate-Subscription-Info $subscriptionId $expectedSubscriptionInfo
    Darc-Delete-Subscription $subscriptionId

    # Attempt to create a batchable subscription with merge policies.
    # Should fail, merge policies are set separately for batched subs
    try {
        Darc-Add-Subscription --channel "$channel1Name" --source-repo "$repo1Uri" --target-repo "$repo2Uri" --update-frequency none --standard-automerge --batchable --target-branch "master"
        throw "darc add-subscription with merge policies and batchable set should fail"
    } catch {
        if (-not $_.Message -match "Batchable subscriptions cannot be combined with merge policies. Merge policies are specified at a repository\+branch level") {
            throw "darc add-subscription with merge policies and batchable set should fail"
        } else {
            Write-Host "Maestro successfully blocked creation of batchable subscription with merge policies"
        }
    }

    # Create a batchable subscription
    $subscriptionId = Darc-Add-Subscription --channel "$channel1Name" --source-repo "$repo1Uri" --target-repo "$repo2Uri" --update-frequency everyWeek --batchable --target-branch "master"
    $expectedSubscriptionInfo = @(
        "$repo1Uri \($channel1Name\) ==> '$repo2Uri' \('master'\)",
        "Id: .*",
        "  - Update Frequency: EveryWeek",
        "  - Enabled: True",
        "  - Batchable: True",
        "  - Merge Policies:"
    )
    Validate-Subscription-Info $subscriptionId $expectedSubscriptionInfo
    Darc-Delete-Subscription $subscriptionId

    Write-Host ""
    Write-Host "Testing YAML for darc add-subscription"

$yaml=@"
Channel: $channel1Name
Source Repository URL: $repo1Uri
Target Repository URL: $repo3Uri
Target Branch: master
Update Frequency: everyWeek
Batchable: False
Merge Policies:
- Name: Standard
"@
    $subscriptionId = Darc-Add-Subscription-From-Yaml $yaml
    $expectedSubscriptionInfo = @(
        "$repo1Uri \($channel1Name\) ==> '$repo3Uri' \('master'\)",
        "Id: .*",
        "  - Update Frequency: EveryWeek",
        "  - Enabled: True",
        "  - Batchable: False",
        "  - Merge Policies:",
        "    Standard"
    )
    Validate-Subscription-Info $subscriptionId $expectedSubscriptionInfo
    Darc-Delete-Subscription $subscriptionId

    # Change casing of the update frequency and various boolean properties.  Should
    # still work.
$yaml=@"
Channel: $channel1Name
Source Repository URL: $repo1Uri
Target Repository URL: $repo3Uri
Target Branch: master
Update Frequency: everyweek
Batchable: false
Merge Policies:
- Name: standard
"@
    $subscriptionId = Darc-Add-Subscription-From-Yaml $yaml
    $expectedSubscriptionInfo = @(
        "$repo1Uri \($channel1Name\) ==> '$repo3Uri' \('master'\)",
        "Id: .*",
        "  - Update Frequency: EveryWeek",
        "  - Enabled: True",
        "  - Batchable: False",
        "  - Merge Policies:",
        "    Standard"
    )
    Validate-Subscription-Info $subscriptionId $expectedSubscriptionInfo
    Darc-Delete-Subscription $subscriptionId

    # Duplicate subscription tests

    Write-Host ""
    Write-Host "Testing duplicate subscription handling..."
    Darc-Add-Subscription --channel "$channel1Name" --source-repo "$repo1Uri" --target-repo "$repo2Uri" --update-frequency none --target-branch "master"
    # Try adding it again
    try {
        Darc-Add-Subscription --channel "$channel1Name" --source-repo "$repo1Uri" --target-repo "$repo2Uri" --update-frequency everyDay --target-branch "master"
        throw "darc add-subscription should fail when creating an equivalent subscription."
    } catch {
        if (-not $_.Message -match "The subscription .* already performs the same update.") {
            throw "darc add-subscription should fail when creating an equivalent subscription"
        } else {
            Write-Host "Maestro successfully blocked creation of duplicate subscription"
        }
    }
    # Try adding it with different casing for one parameter
    try {
        Darc-Add-Subscription --channel "$channel1Name" --source-repo "$($repo1Uri.toUpper())" --target-repo "$repo2Uri" --update-frequency everyDay --target-branch "master"
        throw "darc add-subscription should fail when creating an equivalent subscription."
    } catch {
        if (-not $_.Message -match "The subscription .* already performs the same update.") {
            throw "darc add-subscription should fail when creating an equivalent subscription"
        } else {
            Write-Host "Maestro successfully blocked creation of duplicate subscription"
        }
    }

    Write-Host "Tests passed."
} finally {
    Teardown
}