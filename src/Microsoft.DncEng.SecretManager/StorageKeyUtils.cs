using Microsoft.Azure.Management.Storage;
using Microsoft.Azure.Management.Storage.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Azure.Core;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.DncEng.CommandLineLib.Authentication;
using Microsoft.Rest;
using Microsoft.Rest.Azure;

namespace Microsoft.DncEng.SecretManager
{
    public static class StorageKeyUtils
    {
        public static async Task<string> RotateStorageAccountKey(string subscriptionId, string accountName, RotationContext context, TokenCredentialProvider tokenCredentialProvider, CancellationToken cancellationToken)
        {
            StorageManagementClient client = await CreateManagementClient(subscriptionId, tokenCredentialProvider, cancellationToken);
            StorageAccount account = await FindAccount(accountName, client, cancellationToken);
            if (account == null)
            {
                throw new ArgumentException($"Storage account '{accountName}' in subscription '{subscriptionId}' not found.");
            }

            string currentKey = context.GetValue("currentKey", "key1");
            ResourceId id = ResourceId.FromString(account.Id);
            StorageAccountListKeysResult keys;
            string keyToReturn;
            switch (currentKey)
            {
                case "key1":
                    keys = await client.StorageAccounts.RegenerateKeyAsync(id.ResourceGroupName, id.Name, "key2", cancellationToken: cancellationToken);
                    keyToReturn = "key2";
                    break;
                case "key2":
                    keys = await client.StorageAccounts.RegenerateKeyAsync(id.ResourceGroupName, id.Name, "key1", cancellationToken: cancellationToken);
                    keyToReturn = "key1";
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected 'currentKey' value '{currentKey}'.");
            }

            StorageAccountKey key = keys.Keys.FirstOrDefault(k => k.KeyName == keyToReturn) ?? throw new InvalidOperationException($"Key {keyToReturn} not found.");
            context.SetValue("currentKey", keyToReturn);

            return key.Value;
        }

        private static async Task<StorageManagementClient> CreateManagementClient(string subscriptionId, TokenCredentialProvider tokenCredentialProvider, CancellationToken cancellationToken)
        {
            TokenCredential credentials = await tokenCredentialProvider.GetCredentialAsync();
            AccessToken token = await credentials.GetTokenAsync(new TokenRequestContext(new[]
            {
                "https://management.azure.com/.default",
            }), cancellationToken);
            var serviceClientCredentials = new TokenCredentials(token.Token);
            var client = new StorageManagementClient(serviceClientCredentials)
            {
                SubscriptionId = subscriptionId,
            };
            return client;
        }

        private static async Task<StorageAccount> FindAccount(string accountName, StorageManagementClient client, CancellationToken cancellationToken)
        {
            IPage<StorageAccount> page = await client.StorageAccounts.ListAsync(cancellationToken);
            while (true)
            {
                foreach (StorageAccount account in page)
                {
                    if (account.Name == accountName)
                    {
                        return account;
                    }
                }

                if (string.IsNullOrEmpty(page.NextPageLink))
                {
                    return null;
                }

                page = await client.StorageAccounts.ListNextAsync(page.NextPageLink, cancellationToken);
            }
        }
    }
}
