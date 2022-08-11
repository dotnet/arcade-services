using Microsoft.Azure.Management.Storage;
using Microsoft.Azure.Management.Storage.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using Azure.Core;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.DncEng.CommandLineLib.Authentication;
using Microsoft.Rest;
using Microsoft.Rest.Azure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.DncEng.SecretManager
{
    public static class StorageUtils
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

        public static SharedAccessAccountPermissions AccountPermissionsFromString(string input)
        {
            var accessAccountPermissions = SharedAccessAccountPermissions.None;
            foreach (char ch in input)
            {
                accessAccountPermissions |= ch switch
                {
                    'a' => SharedAccessAccountPermissions.Add,
                    'c' => SharedAccessAccountPermissions.Create,
                    'd' => SharedAccessAccountPermissions.Delete,
                    'l' => SharedAccessAccountPermissions.List,
                    'r' => SharedAccessAccountPermissions.Read,
                    'w' => SharedAccessAccountPermissions.Write,
                    'u' => SharedAccessAccountPermissions.Update,
                    'p' => SharedAccessAccountPermissions.ProcessMessages,
                    _ => throw new ArgumentOutOfRangeException(nameof(input)),
                };
            }

            return accessAccountPermissions;
        }

        public static string GenerateBlobAccountSas(string connectionString, string permissions, string service, DateTimeOffset expiryTime)
        {
            CloudStorageAccount account = CloudStorageAccount.Parse(connectionString);
            SharedAccessAccountServices serviceList = default(SharedAccessAccountServices);
            if(service.Contains("|"))
            {
                HashSet<string> servicesUsed = new HashSet<string>();
                foreach(var serviceString in service.Split("|"))
                {
                    if(!servicesUsed.Add(serviceString))
                    {
                        throw new ArgumentOutOfRangeException(nameof(service));
                    }
                    switch(serviceString)
                    {
                        case "blob": serviceList |= SharedAccessAccountServices.Blob;
                            break;
                        case "table": serviceList |= SharedAccessAccountServices.Table;
                            break;
                        case "file": serviceList |= SharedAccessAccountServices.File;
                            break;
                        case "queue": serviceList |= SharedAccessAccountServices.Queue;
                            break;
                        default: throw new ArgumentOutOfRangeException(nameof(service));
                    }
                }
            }
            else
            {
                switch(service)
                    {
                        case "blob": serviceList = SharedAccessAccountServices.Blob;
                            break;
                        case "table": serviceList = SharedAccessAccountServices.Table;
                            break;
                        case "file": serviceList = SharedAccessAccountServices.File;
                            break;
                        case "queue": serviceList = SharedAccessAccountServices.Queue;
                            break;
                        default: throw new ArgumentOutOfRangeException(nameof(service));
                    }
            }

            string sas = account.GetSharedAccessSignature(new SharedAccessAccountPolicy
            {
                SharedAccessExpiryTime = expiryTime,
                Permissions = AccountPermissionsFromString(permissions),
                Services = serviceList,
                ResourceTypes = SharedAccessAccountResourceTypes.Service | SharedAccessAccountResourceTypes.Container | SharedAccessAccountResourceTypes.Object,
                Protocols = SharedAccessProtocol.HttpsOnly,
            });
            return sas;
        }

        public static (string containerUri,string sas) GenerateBlobContainerSas(string connectionString, string containerName, string permissions, DateTimeOffset expiryTime)
        {
            CloudStorageAccount account = CloudStorageAccount.Parse(connectionString);
            CloudBlobClient blobClient = account.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference(containerName);
            string sas = container.GetSharedAccessSignature(new SharedAccessBlobPolicy
            {
                Permissions = SharedAccessBlobPolicy.PermissionsFromString(permissions),
                SharedAccessExpiryTime = expiryTime,
            });

            return (container.Uri.AbsoluteUri, sas);
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
