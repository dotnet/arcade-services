using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Management.Storage;
using Microsoft.Azure.Management.Storage.Models;
using Microsoft.DncEng.CommandLineLib.Authentication;
using Microsoft.Rest;
using Microsoft.Rest.Azure;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("azure-storage-connection-string")]
    public class AzureStorageConnectionString : SecretType
    {
        private readonly TokenCredentialProvider _tokenCredentialProvider;
        private readonly Guid _subscription;
        private readonly string _accountName;

        public AzureStorageConnectionString(IReadOnlyDictionary<string, string> parameters, TokenCredentialProvider tokenCredentialProvider) : base(parameters)
        {
            _tokenCredentialProvider = tokenCredentialProvider;
            ReadRequiredParameter("subscription", ref _subscription);
            ReadRequiredParameter("account", ref _accountName);
        }

        private async Task<StorageManagementClient> CreateManagementClient(CancellationToken cancellationToken)
        {
            var creds = await _tokenCredentialProvider.GetCredentialAsync();
            var token = await creds.GetTokenAsync(new TokenRequestContext(new[]
            {
                "https://management.azure.com/.default",
            }), cancellationToken);
            var serviceClientCredentials = new TokenCredentials(token.Token);
            var client = new StorageManagementClient(serviceClientCredentials)
            {
                SubscriptionId = _subscription.ToString(),
            };
            return client;
        }

        protected override async Task<SecretData> RotateValue(RotationContext context, CancellationToken cancellationToken)
        {
            var client = await CreateManagementClient(cancellationToken);
            var account = await FindAccount(client, cancellationToken);
            if (account == null)
            {
                throw new ArgumentException($"Storage account '{_accountName}' in subscription '{_subscription}' not found.");
            }

            var currentKey = context.GetValue("currentKey", "key1");
            var id = ResourceId.FromString(account.Id);
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

            var key = keys.Keys.FirstOrDefault(k => k.KeyName == keyToReturn) ?? throw new InvalidOperationException($"Key {keyToReturn} not found.");
            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={id.Name};AccountKey={key.Value}";

            context.SetValue("currentKey", keyToReturn);
            return new SecretData(connectionString, DateTimeOffset.MaxValue, DateTimeOffset.UtcNow.AddMonths(6));
        }

        private async Task<StorageAccount> FindAccount(StorageManagementClient client, CancellationToken cancellationToken)
        {
            IPage<StorageAccount> page = await client.StorageAccounts.ListAsync(cancellationToken);
            while (true)
            {
                foreach (StorageAccount account in page)
                {
                    if (account.Name == _accountName)
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
