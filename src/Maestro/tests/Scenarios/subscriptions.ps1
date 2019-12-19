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
    $subscription1Id = Darc-Add-Subscription --channel "$channel1Name" --source-repo "$repo1Uri" --target-repo "$repo3Uri" --update-frequency everyWeek --standard-automerge --target-branch "master"
    $expectedSubscription1Info = @(
        "$repo1Uri \($channel1Name\) ==> '$repo3Uri' \('master'\)",
        "Id: .*",
        "  - Update Frequency: EveryWeek",
        "  - Enabled: True",
        "  - Batchable: False",
        "  - Merge Policies:",
        "    Standard"
    )
    Validate-Subscription-Info $subscription1Id $expectedSubscription1Info

    $subscription2Id = Darc-Add-Subscription --channel "$channel1Name" --source-repo "$repo1Uri" --target-repo "$repo2Uri" --update-frequency none --all-checks-passed --no-extra-commits --no-requested-changes --ignore-checks "WIP,license/cla" --target-branch "master"
    $expectedSubscription2Info = @(
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
    Validate-Subscription-Info $subscription2Id $expectedSubscription2Info

    $subscription3Id = Darc-Add-Subscription --channel "$channel2Name" --source-repo "$repo1Uri" --target-repo "$repo2Uri" --update-frequency none --all-checks-passed --no-extra-commits --no-requested-changes --ignore-checks "WIP,license/cla" --target-branch "master"
    $expectedSubscription3Info = @(
        "$repo1Uri \($channel2Name\) ==> '$repo2Uri' \('master'\)",
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

    # Disable the first two subscriptions, but not the third.
    Darc-Command subscription-status --disable --quiet --channel "$channel1Name"
    if ($(Darc-Get-Subscription-Enabled $subscription1Id) -or $(Darc-Get-Subscription-Enabled $subscription2Id)) {
        throw "Expected subscriptions $subscription1Id and $subscription2Id to be disabled"
    }

    Darc-Command subscription-status --enable --quiet --channel "$channel1Name"
    if (-not $(Darc-Get-Subscription-Enabled $subscription1Id) -or -not $(Darc-Get-Subscription-Enabled $subscription2Id)) {
        throw "Expected subscriptions $subscription1Id and $subscription2Id to be enabled"
    }

    # Disable one by id (classic usage) to make sure that works
    Darc-Command subscription-status --disable --quiet --id "$subscription3Id"
    if (Darc-Get-Subscription-Enabled $subscription3Id) {
        throw "Expected subscription $subscription3Id to be disabled"
    }

    # Reenable
    Darc-Command subscription-status --enable --quiet --id "$subscription3Id"
    if (-not $(Darc-Get-Subscription-Enabled $subscription3Id)) {
        throw "Expected subscription $subscription3Id to be enabled"
    }

    # Mass delete the subscriptions. Delete the first two but not the third.
    Darc-Command delete-subscriptions --quiet --channel "$channel1Name"

    # Check that there are no subscriptions against channel1 now
    try {
        Darc-Command get-subscriptions --channel "$channel1Name"
        throw "Expected that all subscriptions in channel1 would have been deleted."
    }
    catch {
        if (-not $_.Message -match "No subscriptions found matching the specified criteria") {
            throw "Expected that all subscriptions in channel1 would have been deleted."
        }
    }

    # Validate the third subscription, which should still exist
    Validate-Subscription-Info $subscription3Id $expectedSubscription3Info
    Darc-Delete-Subscription $subscription3Id

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

    # Attempt to add multiple of the same merge policy checks. Should fail.
$yaml=@"
Channel: $channel1Name
Source Repository URL: $repo1Uri
Target Repository URL: $repo3Uri
Target Branch: master
Update Frequency: everyweek
Batchable: false
Merge Policies:
- Name: AllChecksSuccessful
  Properties:
    ignoreChecks:
    - WIP
    - license/cla
- Name: AllChecksSuccessful
  Properties:
    ignoreChecks:
    - WIP
    - MySpecialCheck
"@
    try {
        Darc-Add-Subscription-From-Yaml $yaml
        throw "darc add-subscription should fail when creating a subscription with conflicting merge policies."
    } catch {
        if (-not ($_ -match ".*Subscriptions may not have duplicates of merge policies.*")) {
            throw "darc add-subscription should fail when creating a subscription with conflicting merge policies"
        } else {
            Write-Host "Maestro successfully blocked creation of subscription with conflicting merge policies."
        }
    }

    # Duplicate subscription tests

    Write-Host ""
    Write-Host "Testing duplicate subscription handling..."
    Darc-Add-Subscription --channel "$channel1Name" --source-repo "$repo1Uri" --target-repo "$repo2Uri" --update-frequency none --target-branch "master"
    # Try adding it again
    try {
        Darc-Add-Subscription --channel "$channel1Name" --source-repo "$repo1Uri" --target-repo "$repo2Uri" --update-frequency everyDay --target-branch "master"
        throw "darc add-subscription should fail when creating an equivalent subscription."
    } catch {
        if (-not ($_ -match ".*The subscription .* already performs the same update.*")) {
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
        if (-not ($_ -match ".*The subscription .* already performs the same update.*")) {
            throw "darc add-subscription should fail when creating an equivalent subscription"
        } else {
            Write-Host "Maestro successfully blocked creation of duplicate subscription"
        }
    }

    Write-Host "Tests passed."
} finally {
    Teardown
}