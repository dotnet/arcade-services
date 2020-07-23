using System.Collections.Generic;
using System.Threading.Tasks;
using Maestro.ScenarioTests.ObjectHelpers;
using Microsoft.DotNet.Darc;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Maestro.ScenarioTests
{
    [TestFixture]
    [Category("PostDeployment")]
    public class ScenarioTests_Subscriptions : MaestroScenarioTestBase
    {
        private TestParameters _parameters;

        [TearDown]
        public Task DisposeAsync()
        {
            _parameters.Dispose();
            return Task.CompletedTask;
        }

        [Test]
        public async Task Subscriptions_EndToEnd()
        {
            TestContext.WriteLine("Subscription management tests...");
            string repo1Name = "maestro-test1";
            string repo2Name = "maestro-test2";
            string channel1Name = "subscriptionTestChannel1";
            string channel2Name = "subscriptionTestChannel2";

            _parameters = await TestParameters.GetAsync();
            SetTestParameters(_parameters);

            string repo1Uri = GetRepoUrl(repo1Name);
            string repo2Uri = GetRepoUrl(repo2Name);
            string repo1AzDoUri = GetAzDoRepoUrl(repo1Name);
            string targetBranch = "master";

            TestContext.WriteLine($"Creating channels {channel1Name} and {channel2Name}");
            await using (AsyncDisposableValue<string> channel1 = await CreateTestChannelAsync(channel1Name).ConfigureAwait(false))
            {
                await using (AsyncDisposableValue<string> channel2 = await CreateTestChannelAsync(channel2Name).ConfigureAwait(false))
                {

                    TestContext.WriteLine("Testing various command line parameters of add-subscription");
                    await using AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionAsync(
                        channel1Name, repo1Name, repo2Name, targetBranch, "everyWeek", "maestro-auth-test");

                    Subscription expectedSubscription1 = SubscriptionBuilder.BuildSubscription(
                        repo1Uri, 
                        repo2Uri, 
                        targetBranch, 
                        channel1Name, 
                        subscription1Id.Value, 
                        UpdateFrequency.EveryWeek, 
                        false);

                    string expectedSubscription1Info = UxHelpers.GetTextSubscriptionDescription(expectedSubscription1, null);

                    await ValidateSubscriptionInfo(subscription1Id.Value, expectedSubscription1Info);

                    await using AsyncDisposableValue<string> subscription2Id = await CreateSubscriptionAsync(
                        channel1Name, repo1Name, repo1Name, targetBranch, "none", "maestro-auth-test",
                        new List<string>
                        { "--all-checks-passed", "--no-extra-commits", "--no-requested-changes", "--ignore-checks", "WIP,license/cla" }, targetIsAzDo: true);

                    Subscription expectedSubscription2 = SubscriptionBuilder.BuildSubscription(
                        repo1Uri,
                        repo1AzDoUri,
                        targetBranch,
                        channel1Name,
                        subscription2Id.Value,
                        UpdateFrequency.None,
                        false,
                        new List<string> { Constants.NoExtraCommitsMergePolicyName, Constants.AllCheckSuccessfulMergePolicyName, Constants.NoRequestedChangesMergePolicyName },
                        new List<string> { "WIP", "license/cla" });

                    string expectedSubscription2Info = UxHelpers.GetTextSubscriptionDescription(expectedSubscription2, null);

                    await ValidateSubscriptionInfo(subscription2Id.Value, expectedSubscription2Info);

                    await using AsyncDisposableValue<string> subscription3Id = await CreateSubscriptionAsync(
                        channel2Name, repo1Name, repo2Name, targetBranch, "none", "maestro-auth-test",
                        new List<string>
                        { "--all-checks-passed", "--no-extra-commits", "--no-requested-changes", "--ignore-checks", "WIP,license/cla" });

                    Subscription expectedSubscription3 = SubscriptionBuilder.BuildSubscription(
                        repo1Uri,
                        repo2Uri,
                        targetBranch,
                        channel2Name,
                        subscription3Id.Value,
                        UpdateFrequency.None,
                        false,
                        new List<string> { Constants.NoExtraCommitsMergePolicyName, Constants.AllCheckSuccessfulMergePolicyName, Constants.NoRequestedChangesMergePolicyName },
                        new List<string> { "WIP", "license/cla" });

                    string expectedSubscription3Info = UxHelpers.GetTextSubscriptionDescription(expectedSubscription3, null);

                    await ValidateSubscriptionInfo(subscription3Id.Value, expectedSubscription3Info);

                    // Disable the first two subscriptions, but not the third.
                    TestContext.WriteLine("Disable the subscriptions for test channel 1");
                    await SetSubscriptionStatus(false, channelName: channel1Name);

                    // Disable one by id (classic usage) to make sure that works
                    TestContext.WriteLine("Disable the third subscription by id");
                    await SetSubscriptionStatus(false, subscriptionId: subscription3Id.Value);

                    // Re-enable
                    TestContext.WriteLine("Enable the third subscription by id");
                    await SetSubscriptionStatus(true, subscriptionId: subscription3Id.Value);
                    StringAssert.Contains("Enabled: True", await GetSubscriptionInfo(subscription3Id.Value), $"Expected subscription {subscription3Id} to be enabled");

                    // Mass delete the subscriptions. Delete the first two but not the third.
                    TestContext.WriteLine("Delete the subscriptions for test channel 1");
                    string message = await DeleteSubscriptionsForChannel(channel1Name);

                    // Check that there are no subscriptions against channel1 now
                    TestContext.WriteLine("Verify that there are no subscriptions in test channel 1");
                    Assert.ThrowsAsync<MaestroTestException>(async () => await GetSubscriptions(channel1Name), "Subscriptions for channel 1 were not deleted.");

                    // Validate the third subscription, which should still exist
                    TestContext.WriteLine("Verify that the third subscription still exists, then delete it");
                    await ValidateSubscriptionInfo(subscription3Id.Value, expectedSubscription3Info);
                    string message2 = await DeleteSubscriptionById(subscription3Id.Value);

                    // Attempt to create a batchable subscription with merge policies.
                    // Should fail, merge policies are set separately for batched subs
                    TestContext.WriteLine("Attempt to create a batchable subscription with merge policies");
                    Assert.ThrowsAsync<MaestroTestException>(async () =>
                        await CreateSubscriptionAsync(channel1Name, repo1Name, repo2Name, "master", "none", additionalOptions: new List<string> { "--standard-automerge", "--batchable" }),
                        "Attempt to create a batchable subscription with merge policies");

                    // Create a batchable subscription
                    TestContext.WriteLine("Create a batchable subscription");
                    await using AsyncDisposableValue<string> batchSubscriptionId = await CreateSubscriptionAsync(
                        channel1Name, repo1Name, repo2Name, targetBranch, "everyWeek", "maestro-auth-test", additionalOptions: new List<string> { "--batchable" });

                    Subscription expectedBatchedSubscription = SubscriptionBuilder.BuildSubscription(
                        repo1Uri, 
                        repo2Uri, 
                        targetBranch, 
                        channel1Name, 
                        batchSubscriptionId.Value, 
                        UpdateFrequency.EveryWeek, 
                        true);

                    string expectedBatchedSubscriptionInfo = UxHelpers.GetTextSubscriptionDescription(expectedBatchedSubscription, null);

                    await ValidateSubscriptionInfo(batchSubscriptionId.Value, expectedBatchedSubscriptionInfo);
                    await DeleteSubscriptionById(batchSubscriptionId.Value);

                    TestContext.WriteLine("Testing YAML for darc add-subscription");

                    string yamlDefinition = $@"
                    Channel: {channel1Name}
                    Source Repository URL: {repo1Uri}
                    Target Repository URL: {repo2Uri}
                    Target Branch: master
                    Update Frequency: everyWeek
                    Batchable: False
                    Merge Policies:
                    - Name: Standard
                    ";

                    await using AsyncDisposableValue<string> yamlSubscriptionId = await CreateSubscriptionAsync(yamlDefinition);

                    Subscription expectedYamlSubscription = SubscriptionBuilder.BuildSubscription(
                        repo1Uri, 
                        repo2Uri, 
                        targetBranch, 
                        channel1Name, 
                        yamlSubscriptionId.Value, 
                        UpdateFrequency.EveryWeek, 
                        false, 
                        new List<string> { Constants.StandardMergePolicyName });

                    string expectedYamlSubscriptionInfo = UxHelpers.GetTextSubscriptionDescription(expectedYamlSubscription, null);

                    await ValidateSubscriptionInfo(yamlSubscriptionId.Value, expectedYamlSubscriptionInfo);
                    await DeleteSubscriptionById(yamlSubscriptionId.Value);

                    TestContext.WriteLine("Change casing of the various properties. Expecting no changes.");

                    string yamlDefinition2 = $@"
                    Channel: {channel1Name}
                    Source Repository URL: {repo1Uri}
                    Target Repository URL: {repo2Uri}
                    Target Branch: master
                    Update Frequency: everyweek
                    Batchable: False
                    Merge Policies:
                    - Name: standard
                    ";

                    await using AsyncDisposableValue<string> yamlSubscription2Id = await CreateSubscriptionAsync(yamlDefinition2);

                    Subscription expectedYamlSubscription2 = SubscriptionBuilder.BuildSubscription(
                        repo1Uri, 
                        repo2Uri,
                        targetBranch, 
                        channel1Name, 
                        yamlSubscription2Id.Value, 
                        UpdateFrequency.EveryWeek, false, 
                        new List<string> { Constants.StandardMergePolicyName });

                    string expectedYamlSubscriptionInfo2 = UxHelpers.GetTextSubscriptionDescription(expectedYamlSubscription2, null);

                    await ValidateSubscriptionInfo(yamlSubscription2Id.Value, expectedYamlSubscriptionInfo2);
                    await DeleteSubscriptionById(yamlSubscription2Id.Value);

                    TestContext.WriteLine("Attempt to add multiple of the same merge policy checks. Should fail.");

                    string yamlDefinition3 = $@"
                    Channel: {channel1Name}
                    Source Repository URL: {repo1Uri}
                    Target Repository URL: {repo2Uri}
                    Target Branch: master
                    Update Frequency: everyweek
                    Batchable: False
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
                        - MySpecialCheck";

                    Assert.ThrowsAsync<MaestroTestException>(async () =>
                        await CreateSubscriptionAsync(yamlDefinition3), "Attempt to create a subscription with multiples of the same merge policy.");

                    TestContext.WriteLine("Testing duplicate subscription handling...");
                    AsyncDisposableValue<string> yamlSubscription3Id = await CreateSubscriptionAsync(channel1Name, repo1Name, repo2Name, targetBranch, "everyWeek", "maestro-auth-test");

                    Assert.ThrowsAsync<MaestroTestException>(async () =>
                        await CreateSubscriptionAsync(channel1Name, repo1Name, repo2Name, targetBranch, "everyWeek", "maestro-auth-test"),
                        "Attempt to create a subscription with the same values as an existing subscription.");

                    Assert.ThrowsAsync<MaestroTestException>(async () =>
                        await CreateSubscriptionAsync(channel1Name, repo1Name, repo2Name, targetBranch, "everyweek", "maestro-auth-test"),
                        "Attempt to create a subscription with the same values as an existing subscription (except for the casing of one parameter.");

                    await DeleteSubscriptionById(yamlSubscription3Id.Value);

                    TestContext.WriteLine("End of test case. Starting clean up.");
                }
            }
        }
    }
}
