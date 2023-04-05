using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.Management.Storage;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.Rest;
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
        const string ContainerSasUriNamePrefix = "azure-storage-container-sas-uri";
        const string ContainerSasTokenNamePrefix = "azure-storage-container-sas-token";
        const string StorageSasUriTokenNamePrefix = "azure-storage-account-sas-uri";
        const string KeyNamePrefix = "azure-storage-key";
        const string AccountKeyPrefix = "AccountKey=";

        readonly string Manifest = @$"storageLocation:
  type: azure-key-vault
  parameters:
    name: {KeyVaultName}
    subscription: {SubscriptionId}
secrets:
  {KeyNamePrefix}{{0}}:
    type: azure-storage-key
    owner: scenarioTests
    description: storage key
    parameters:
      Subscription: {SubscriptionId}
      Account: {AccountName}
  {ConnectionStringNamePrefix}{{0}}:
    type: azure-storage-connection-string
    owner: scenarioTests
    description: storage connection string
    parameters:
      StorageKeySecret: {KeyNamePrefix}{{0}}
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
  {ContainerSasUriNamePrefix}{{0}}:
    type: azure-storage-container-sas-uri
    owner: scenarioTests
    description: container sas uri
    parameters:
      ConnectionString: {ConnectionStringNamePrefix}{{0}}
      Container: test
      Permissions: lr
  {ContainerSasTokenNamePrefix}{{0}}:
    type: azure-storage-container-sas-token
    owner: scenarioTests
    description: container sas token
    parameters:
      ConnectionString: {ConnectionStringNamePrefix}{{0}}
      Container: test
      Permissions: l
  {StorageSasUriTokenNamePrefix}{{0}}:
    type: azure-storage-account-sas-uri
    owner: scenarioTests
    description: storage account sas URI
    parameters:
      ConnectionString: {ConnectionStringNamePrefix}{{0}}
      Service: blob
      Permissions: l";


        [Test]
        public async Task NewStorageSecretsTest()
        {
            string nameSuffix = Guid.NewGuid().ToString("N");
            string keySecretName = KeyNamePrefix + nameSuffix;
            string connectionStringSecretName = ConnectionStringNamePrefix + nameSuffix;
            string blobSasSecretName = BlobSasNamePrefix + nameSuffix;
            string tableSasSecretName = TableSasNamePrefix + nameSuffix;
            string containerSasUriSecretName = ContainerSasUriNamePrefix + nameSuffix;
            string containerSasTokenSecretName = ContainerSasTokenNamePrefix + nameSuffix;
            string storageSasUriTokenSecretName = StorageSasUriTokenNamePrefix + nameSuffix;
            string manifest = string.Format(Manifest, nameSuffix);

            await ExecuteSynchronizeCommand(manifest);

            SecretClient client = GetSecretClient();
            Response<KeyVaultSecret> keySecret = await client.GetSecretAsync(keySecretName);
            Response<KeyVaultSecret> connectionStringSecret = await client.GetSecretAsync(connectionStringSecretName);

            HashSet<string> connectionStringAccessKeys = await GetAccessKeys();

            string extractedAccountKey = ExtractKeyFromConnectionString(connectionStringSecret.Value);
            Assert.IsTrue(connectionStringAccessKeys.Contains(extractedAccountKey));
            Assert.AreEqual(keySecret.Value.Value, extractedAccountKey);

            Response<KeyVaultSecret> blobSasSecret = await client.GetSecretAsync(blobSasSecretName);
            AssertValidSasUri(blobSasSecret.Value.Value);
            Response<KeyVaultSecret> tableSasSecret = await client.GetSecretAsync(tableSasSecretName);
            AssertValidSasUri(tableSasSecret.Value.Value);
            Response<KeyVaultSecret> containerSasUriSecret = await client.GetSecretAsync(containerSasUriSecretName);
            AssertValidSasUri(containerSasUriSecret.Value.Value);
            Response<KeyVaultSecret> containerSasTokenSecret = await client.GetSecretAsync(containerSasTokenSecretName);
            AssertValidSas(containerSasTokenSecret.Value.Value);
            Response<KeyVaultSecret> storageSasUriTokenSecret = await client.GetSecretAsync(storageSasUriTokenSecretName);
            AssertValidSas(storageSasUriTokenSecret.Value.Value);
        }

        [Test]
        public async Task RotateSecretTest()
        {
            string nameSuffix = Guid.NewGuid().ToString("N");
            string keySecretName = KeyNamePrefix + nameSuffix;
            string connectionStringSecretName = ConnectionStringNamePrefix + nameSuffix;
            string blobSasSecretName = BlobSasNamePrefix + nameSuffix;
            string tableSasSecretName = TableSasNamePrefix + nameSuffix;
            string containerSasUriSecretName = ContainerSasUriNamePrefix + nameSuffix;
            string containerSasTokenSecretName = ContainerSasTokenNamePrefix + nameSuffix;
            string storageSasUriTokenSecretName = StorageSasUriTokenNamePrefix + nameSuffix;
            string manifest = string.Format(Manifest, nameSuffix);

            SecretClient client = GetSecretClient();

            Response<KeyVaultSecret> keySecret = await client.SetSecretAsync(keySecretName, "TEST");
            await UpdateNextRotationTagIntoPast(client, keySecret.Value);
            Response<KeyVaultSecret> connectionStringSecret = await client.SetSecretAsync(connectionStringSecretName, "TEST");
            await UpdateNextRotationTagIntoPast(client, connectionStringSecret.Value);
            Response<KeyVaultSecret> blobSasSecret = await client.SetSecretAsync(blobSasSecretName, "TEST");
            await UpdateNextRotationTagIntoFuture(client, blobSasSecret.Value);
            Response<KeyVaultSecret> tableSasSecret = await client.SetSecretAsync(tableSasSecretName, "TEST");
            await UpdateNextRotationTagIntoFuture(client, tableSasSecret.Value);
            Response<KeyVaultSecret> containerSasUriSecret = await client.SetSecretAsync(containerSasUriSecretName, "TEST");
            await UpdateNextRotationTagIntoFuture(client, containerSasUriSecret.Value);
            Response<KeyVaultSecret> containerSasTokenSecret = await client.SetSecretAsync(containerSasTokenSecretName, "TEST");
            await UpdateNextRotationTagIntoFuture(client, containerSasTokenSecret.Value);
            Response<KeyVaultSecret> storageSasUriTokenSecret = await client.SetSecretAsync(storageSasUriTokenSecretName, "TEST");
            await UpdateNextRotationTagIntoFuture(client, storageSasUriTokenSecret.Value);


            HashSet<string> accessKeys = await GetAccessKeys();

            await ExecuteSynchronizeCommand(manifest);

            HashSet<string> accessKeysRotated = await GetAccessKeys();

            accessKeysRotated.ExceptWith(accessKeys);


            Assert.AreEqual(1, accessKeysRotated.Count);
            var rotatedAccountKey = accessKeysRotated.First();

            keySecret = await client.GetSecretAsync(keySecretName);
            connectionStringSecret = await client.GetSecretAsync(connectionStringSecretName);
            string accountKeyFromConnectionString = ExtractKeyFromConnectionString(connectionStringSecret.Value);

            Assert.AreEqual(rotatedAccountKey, accountKeyFromConnectionString);
            Assert.AreEqual(keySecret.Value.Value, accountKeyFromConnectionString);

            blobSasSecret = await client.GetSecretAsync(blobSasSecretName);
            AssertValidSasUri(blobSasSecret.Value.Value);
            tableSasSecret = await client.GetSecretAsync(tableSasSecretName);
            AssertValidSasUri(tableSasSecret.Value.Value);
            containerSasUriSecret = await client.GetSecretAsync(containerSasUriSecretName);
            AssertValidSasUri(containerSasUriSecret.Value.Value);
            containerSasTokenSecret = await client.GetSecretAsync(containerSasTokenSecretName);
            AssertValidSas(containerSasTokenSecret.Value.Value);
            storageSasUriTokenSecret = await client.GetSecretAsync(storageSasUriTokenSecretName);
            AssertValidSas(storageSasUriTokenSecret.Value.Value);
        }

        [OneTimeTearDown]
        public async Task Cleanup()
        {
            await PurgeAllSecrets();
        }

        private static void AssertValidSasUri(string uriText)
        {
            var uri = new Uri(uriText);
            AssertValidSas(uri.Query);
        }

        private static void AssertValidSas(string sas)
        {            
            var query = System.Web.HttpUtility.ParseQueryString(sas);
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
