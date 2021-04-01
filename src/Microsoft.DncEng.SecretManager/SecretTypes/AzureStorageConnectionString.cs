using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Management.Storage;
using Microsoft.Azure.Management.Storage.Models;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.CommandLineLib.Authentication;
using Microsoft.Rest;
using Microsoft.Rest.Azure;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("azure-storage-connection-string")]
    public class AzureStorageConnectionString : SecretType<AzureStorageConnectionString.Parameters>
    {
        public class Parameters
        {
            public Guid Subscription { get; set; }
            public string Account { get; set; }
        }

        private readonly TokenCredentialProvider _tokenCredentialProvider;
        private readonly ISystemClock _clock;

        public AzureStorageConnectionString(TokenCredentialProvider tokenCredentialProvider, ISystemClock clock)
        {
            _tokenCredentialProvider = tokenCredentialProvider;
            _clock = clock;
        }

        private async Task<StorageManagementClient> CreateManagementClient(Parameters parameters, CancellationToken cancellationToken)
        {
            TokenCredential credentials = await _tokenCredentialProvider.GetCredentialAsync();
            AccessToken token = await credentials.GetTokenAsync(new TokenRequestContext(new[]
            {
                "https://management.azure.com/.default",
            }), cancellationToken);
            var serviceClientCredentials = new TokenCredentials(token.Token);
            var client = new StorageManagementClient(serviceClientCredentials)
            {
                SubscriptionId = parameters.Subscription.ToString(),
            };
            return client;
        }

        protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
        {
            StorageManagementClient client = await CreateManagementClient(parameters, cancellationToken);
            StorageAccount account = await FindAccount(parameters, client, cancellationToken);
            if (account == null)
            {
                throw new ArgumentException($"Storage account '{parameters.Account}' in subscription '{parameters.Subscription}' not found.");
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
            string connectionString = $"DefaultEndpointsProtocol=https;AccountName={id.Name};AccountKey={key.Value}";

            context.SetValue("currentKey", keyToReturn);
            return new SecretData(connectionString, DateTimeOffset.MaxValue, _clock.UtcNow.AddMonths(6));
        }

        private async Task<StorageAccount> FindAccount(Parameters parameters, StorageManagementClient client, CancellationToken cancellationToken)
        {
            IPage<StorageAccount> page = await client.StorageAccounts.ListAsync(cancellationToken);
            while (true)
            {
                foreach (StorageAccount account in page)
                {
                    if (account.Name == parameters.Account)
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
