
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.Management.EventHub;
using Microsoft.Azure.Management.ServiceBus;
using Microsoft.Azure.Management.ServiceBus.Models;
using Microsoft.Azure.Management.Storage;
using Microsoft.Rest;
using Microsoft.Rest.Azure;
using NUnit.Framework;

namespace Microsoft.DncEng.SecretManager.Tests
{
    [TestFixture]
    [Category("PostDeployment")]
    public class ServiceBusTests : ScenarioTestsBase
    {
        const string AccessPolicySufix = "-access-policy";
        const string Namespace = "servicebussecretstest";
        const string ServiceBusNamePrefix = "sb";

        readonly string Manifest = @$"storageLocation:
  type: azure-key-vault
  parameters:
    name: {KeyVaultName}
    subscription: {SubscriptionId}
secrets:
  {ServiceBusNamePrefix}{{0}}:
    type: service-bus-connection-string
    owner: scenarioTests
    description: service bus connection string
    parameters:
      Subscription: {SubscriptionId}
      ResourceGroup: {ResourceGroup}
      Namespace: {Namespace}      
      Permissions: l
  ";

        [Test]
        public async Task NewConnectionStringSecretTest()
        {
            string nameSuffix = Guid.NewGuid().ToString("N");
            string connectionStringSecretName = ServiceBusNamePrefix + nameSuffix;
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
            string connectionStringSecretName = ServiceBusNamePrefix + nameSuffix;
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
            ServiceBusManagementClient client = await GetServiceBusManagementClient();
            IPage<SBAuthorizationRule> rules = await client.Namespaces.ListAuthorizationRulesAsync(ResourceGroup, Namespace);

            foreach (var rule in rules)
            {
                if (rule.Name.EndsWith(AccessPolicySufix))
                    await client.Namespaces.DeleteAuthorizationRuleAsync(ResourceGroup, Namespace, rule.Name);
            }

            await PurgeAllSecrets();
        }


        private async Task<HashSet<string>> GetAccessKeys(string secretName)
        {
            var client = await GetServiceBusManagementClient();
            var authotizationRuleName = secretName + AccessPolicySufix;
            var result = await client.Namespaces.ListKeysAsync(ResourceGroup, Namespace, authotizationRuleName);

            return new HashSet<string>(new[] { result.PrimaryConnectionString, result.SecondaryConnectionString });
        }

        private async Task<ServiceBusManagementClient> GetServiceBusManagementClient()
        {
            TokenCredentials credentials = await GetServiceClientCredentials();
            var client = new ServiceBusManagementClient(credentials)
            {
                SubscriptionId = SubscriptionId,
            };

            return client;
        }
    }
}
