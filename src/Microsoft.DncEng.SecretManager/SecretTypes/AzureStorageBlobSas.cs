using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("azure-storage-blob-sas-uri")]
    public class AzureStorageBlobSas : AzureStorageSasSecretType
    {
        private readonly string _containerName;
        private readonly string _blobName;
        private readonly string _permissions;
        public AzureStorageBlobSas(IReadOnlyDictionary<string, string> parameters) : base(parameters)
        {
            ReadRequiredParameter("blob", ref _blobName);
            ReadRequiredParameter("container", ref _containerName);
            ReadRequiredParameter("permissions", ref _permissions);
        }

        protected override async Task<SecretData> RotateValue(RotationContext context, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            var account = await ConnectToAccount(context, cancellationToken);
            var blobClient = account.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(_containerName);
            var blob = container.GetBlobReference(_blobName);
            var sas = blob.GetSharedAccessSignature(new SharedAccessBlobPolicy
            {
                Permissions = SharedAccessBlobPolicy.PermissionsFromString(_permissions),
                SharedAccessExpiryTime = now.AddMonths(1),
            });
            var result = blob.Uri.AbsoluteUri + sas;

            return new SecretData(result, now.AddMonths(1), now.AddDays(15));
        }
    }
}