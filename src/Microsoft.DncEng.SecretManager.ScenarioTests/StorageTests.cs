using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.Management.Storage;
using Microsoft.Azure.Management.Storage.Models;
using Microsoft.DncEng.CommandLineLib.Authentication;
using Microsoft.Rest;
using Microsoft.WindowsAzure.Storage.Blob;
using NUnit.Framework;

namespace Microsoft.DncEng.SecretManager.Tests
{
    [TestFixture]
    [Category("PostDeployment")]
    public class StorageTests : ScenarioTestsBase
    {
        const string AccountName = "secretmanagertests";
        const string ConnectionStringNamePrefix = "azure-storage-connection-string";
        const string BlobSasNamePrefix = "azure-storage-blob-sas-uri";
        const string TableSasNamePrefix = "azure-storage-table-sas-uri";
        const string ContainerSasNamePrefix = "azure-storage-container-sas-uri";
        const string AccountKeyPrefix = "AccountKey=";

        readonly string Manifest = @$"storageLocation:
  type: azure-key-vault
  parameters:
    name: {KeyVaultName}
    subscription: {SubscriptionId}
secrets:
  {ConnectionStringNamePrefix}{{0}}:
    type: azure-storage-connection-string
    owner: scenarioTests
    description: storage connection string
    parameters:
      Subscription: {SubscriptionId}
      Account: {AccountName}
  {BlobSasNamePrefix}{{0}}:
    type: azure-storage-blob-sas-uri
    owner: scenarioTests
    description: blob sas
    parameters:
      ConnectionString: {ConnectionStringNamePrefix}{{0}}
      Container: test
      Blob: test.txt
      Permissions: r
  {TableSasNamePrefix}{{0}}:
    type: azure-storage-table-sas-uri
    owner: scenarioTests
    description: table sas
    parameters:
      ConnectionString: {ConnectionStringNamePrefix}{{0}}
      Table: testTable
      Permissions: r
  {ContainerSasNamePrefix}{{0}}:
    type: azure-storage-container-sas-uri
    owner: scenarioTests
    description: container sas
    parameters:
      ConnectionString: {ConnectionStringNamePrefix}{{0}}
      Container: test
      Permissions: lr";


        [Test]
        public async Task NewStorageSecretsTest()
        {
            string nameSuffix = Guid.NewGuid().ToString("N");
            string connectionStringSecretName = ConnectionStringNamePrefix + nameSuffix;
            string blobSasSecretName = BlobSasNamePrefix + nameSuffix;
            string tableSasSecretName = TableSasNamePrefix + nameSuffix;
            string containerSasSecretName = ContainerSasNamePrefix + nameSuffix;
            string manifest = string.Format(Manifest, nameSuffix);

            await ExecuteSynchronizeCommand(manifest);

            SecretClient client = GetSecretClient();
            Response<KeyVaultSecret> connectionStringSecret = await client.GetSecretAsync(connectionStringSecretName);
            HashSet<string> connectionStringAccessKeys = await GetAccessKeys();

            string extractedAccountKey = ExtractKeyFromConnectionString(connectionStringSecret.Value);
            Assert.IsTrue(connectionStringAccessKeys.Contains(extractedAccountKey));

            Response<KeyVaultSecret> blobSasSecret = await client.GetSecretAsync(blobSasSecretName);
            AssertValidSAS(blobSasSecret.Value.Value);
            Response<KeyVaultSecret> tableSasSecret = await client.GetSecretAsync(tableSasSecretName);
            AssertValidSAS(tableSasSecret.Value.Value);
            Response<KeyVaultSecret> containerSasSecret = await client.GetSecretAsync(containerSasSecretName);
            AssertValidSAS(containerSasSecret.Value.Value);
        }

        [Test]
        public async Task RotateSecretTest()
        {
            string nameSuffix = Guid.NewGuid().ToString("N");
            string connectionStringSecretName = ConnectionStringNamePrefix + nameSuffix;
            string blobSasSecretName = BlobSasNamePrefix + nameSuffix;
            string tableSasSecretName = TableSasNamePrefix + nameSuffix;
            string containerSasSecretName = ContainerSasNamePrefix + nameSuffix;
            string manifest = string.Format(Manifest, nameSuffix);

            SecretClient client = GetSecretClient();

            Response<KeyVaultSecret> connectionStringSecret = await client.SetSecretAsync(connectionStringSecretName, "TEST");
            await UpdateNextRotationTagIntoPast(client, connectionStringSecret.Value);
            Response<KeyVaultSecret> blobSasSecret = await client.SetSecretAsync(blobSasSecretName, "TEST");
            await UpdateNextRotationTagIntoFuture(client, blobSasSecret.Value);
            Response<KeyVaultSecret> tableSasSecret = await client.SetSecretAsync(tableSasSecretName, "TEST");
            await UpdateNextRotationTagIntoFuture(client, tableSasSecret.Value);
            Response<KeyVaultSecret> containerSasSecret = await client.SetSecretAsync(containerSasSecretName, "TEST");
            await UpdateNextRotationTagIntoFuture(client, containerSasSecret.Value);


            HashSet<string> accessKeys = await GetAccessKeys();

            await ExecuteSynchronizeCommand(manifest);

            HashSet<string> accessKeysRotated = await GetAccessKeys();

            accessKeysRotated.ExceptWith(accessKeys);


            Assert.AreEqual(1, accessKeysRotated.Count);
            var rotatedAccountKey = accessKeysRotated.First();

            connectionStringSecret = await client.GetSecretAsync(connectionStringSecretName);
            string accountKeyFromConnectionString = ExtractKeyFromConnectionString(connectionStringSecret.Value);

            Assert.AreEqual(rotatedAccountKey, accountKeyFromConnectionString);

            blobSasSecret = await client.GetSecretAsync(blobSasSecretName);
            AssertValidSAS(blobSasSecret.Value.Value);
            tableSasSecret = await client.GetSecretAsync(tableSasSecretName);
            AssertValidSAS(tableSasSecret.Value.Value);
            containerSasSecret = await client.GetSecretAsync(containerSasSecretName);
            AssertValidSAS(containerSasSecret.Value.Value);
        }

        [OneTimeTearDown]
        public async Task Cleanup()
        {
            await PurgeAllSecrets();
        }

        private static void AssertValidSAS(string uriText)
        {
            var uri = new Uri(uriText);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            Assert.IsNotNull(query["sig"]);
        }

        private static string ExtractKeyFromConnectionString(KeyVaultSecret secret)
        {
            if (secret == null || secret.Value == null)
                return null;

            var accountKey = secret.Value.Split(';').FirstOrDefault(l => l.StartsWith(AccountKeyPrefix));
            if (accountKey == null)
                return null;

            return accountKey.Substring(AccountKeyPrefix.Length);
        }

        private async Task<HashSet<string>> GetAccessKeys()
        {
            TokenCredentials credentials = await GetServiceClientCredentials();
            var client = new StorageManagementClient(credentials)
            {
                SubscriptionId = SubscriptionId,
            };

            var result = await client.StorageAccounts.ListKeysAsync(ResourceGroup, AccountName);
            return result.Keys.Select(l => l.Value).ToHashSet();
        }
    }
}
