using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("azure-storage-container-sas-uri")]
    public class AzureStorageContainerSas : AzureStorageSasSecretType
    {
        private readonly string _containerName;
        private readonly string _permissions;
        public AzureStorageContainerSas(IReadOnlyDictionary<string, string> parameters) : base(parameters)
        {
            ReadRequiredParameter("container", ref _containerName);
            ReadRequiredParameter("permissions", ref _permissions);
        }

        protected override async Task<SecretData> RotateValue(RotationContext context, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            var account = await ConnectToAccount(context, cancellationToken);
            var blobClient = account.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(_containerName);
            var sas = container.GetSharedAccessSignature(new SharedAccessBlobPolicy
            {
                Permissions = SharedAccessBlobPolicy.PermissionsFromString(_permissions),
                SharedAccessExpiryTime = now.AddMonths(1),
            });
            var result = container.Uri.AbsoluteUri + sas;

            return new SecretData(result, now.AddMonths(1), now.AddDays(15));
        }
    }
}