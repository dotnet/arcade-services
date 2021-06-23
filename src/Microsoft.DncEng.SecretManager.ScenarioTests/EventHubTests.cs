
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.Management.EventHub;
using Microsoft.Azure.Management.EventHub.Models;
using Microsoft.Azure.Management.Storage;
using Microsoft.Rest;
using Microsoft.Rest.Azure;
using NUnit.Framework;

namespace Microsoft.DncEng.SecretManager.Tests
{
    [TestFixture]
    [Category("PostDeployment")]
    public class EventHubTests : ScenarioTestsBase
    {
        const string Name = "test-event-hub";
        const string Namespace = "EventHubSecretsTest";
        const string EventHubNamePrefix = "event-hub-connection-string";

        readonly string Manifest = @$"storageLocation:
  type: azure-key-vault
  parameters:
    name: {KeyVaultName}
    subscription: {SubscriptionId}
secrets:
  {EventHubNamePrefix}{{0}}:
    type: event-hub-connection-string
    owner: scenarioTests
    description: storage connection string
    parameters:
      Subscription: {SubscriptionId}
      ResourceGroup: {ResourceGroup}
      Namespace: {Namespace}
      Name: {Name}
      Permissions: l
  ";

        [Test]
        public async Task NewConnectionStringSecretTest()
        {
            string nameSuffix = Guid.NewGuid().ToString("N");
            string connectionStringSecretName = EventHubNamePrefix + nameSuffix;
            string manifest = string.Format(Manifest, nameSuffix);

            await ExecuteSynchronizeCommand(manifest);

            SecretClient client = GetSecretClient();
            Response<KeyVaultSecret> connectionStringSecret = await client.GetSecretAsync(connectionStringSecretName);
            HashSet<string> connectionStringAccessKeys = await GetAccessKeys(connectionStringSecretName);

            Assert.IsTrue(connectionStringAccessKeys.Contains(connectionStringSecret.Value.Value));
        }

        [Test]
        public async Task RotateConnectionStringSecretTest()
        {
            string nameSuffix = Guid.NewGuid().ToString("N");
            string connectionStringSecretName = EventHubNamePrefix + nameSuffix;
            string manifest = string.Format(Manifest, nameSuffix);

            SecretClient client = GetSecretClient();

            await ExecuteSynchronizeCommand(manifest);

            HashSet<string> accessKeys = await GetAccessKeys(connectionStringSecretName);
            Response<KeyVaultSecret> connectionStringSecret = await client.GetSecretAsync(connectionStringSecretName);
            await UpdateNextRotationTagIntoPast(client, connectionStringSecret.Value);

            await ExecuteSynchronizeCommand(manifest);

            HashSet<string> accessKeysRotated = await GetAccessKeys(connectionStringSecretName);

            accessKeysRotated.ExceptWith(accessKeys);

            Assert.AreEqual(1, accessKeysRotated.Count);
            connectionStringSecret = await client.GetSecretAsync(connectionStringSecretName);
            Assert.AreEqual(connectionStringSecret.Value.Value, accessKeysRotated.First());
        }

        [OneTimeTearDown]
        public async Task Cleanup()
        {
            EventHubManagementClient client = await GetEventHubManagementClient();
            IPage<AuthorizationRule> rules = await client.EventHubs.ListAuthorizationRulesAsync(ResourceGroup, Namespace, Name);

            foreach (var rule in rules)
            {
                await client.EventHubs.DeleteAuthorizationRuleAsync(ResourceGroup, Namespace, Name, rule.Name);
            }

            await PurgeAllSecrets();
        }

        private async Task<HashSet<string>> GetAccessKeys(string secretName)
        {
            EventHubManagementClient client = await GetEventHubManagementClient();
            string accessPolicyName = secretName + "-access-policy";
            AccessKeys result = await client.EventHubs.ListKeysAsync(ResourceGroup, Namespace, Name, accessPolicyName);

            return new HashSet<string>(new[] { result.PrimaryConnectionString, result.SecondaryConnectionString });
        }

        private async Task<EventHubManagementClient> GetEventHubManagementClient()
        {
            TokenCredentials credentials = await GetServiceClientCredentials();
            var client = new EventHubManagementClient(credentials)
            {
                SubscriptionId = SubscriptionId,
            };

            return client;
        }
    }
}
