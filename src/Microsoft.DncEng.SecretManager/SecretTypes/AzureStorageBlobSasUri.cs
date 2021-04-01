using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("azure-storage-blob-sas-uri")]
    public class AzureStorageBlobSasUri : AzureStorageSasSecretType
    {
        private readonly ISystemClock _clock;
        private readonly string _containerName;
        private readonly string _blobName;
        private readonly string _permissions;

        public AzureStorageBlobSasUri(IReadOnlyDictionary<string, string> parameters, ISystemClock clock) : base(parameters)
        {
            _clock = clock;
            ReadRequiredParameter("blob", ref _blobName);
            ReadRequiredParameter("container", ref _containerName);
            ReadRequiredParameter("permissions", ref _permissions);
        }

        protected override async Task<SecretData> RotateValue(RotationContext context, CancellationToken cancellationToken)
        {
            DateTimeOffset now = _clock.UtcNow;
            CloudStorageAccount account = await ConnectToAccount(context, cancellationToken);
            CloudBlobClient blobClient = account.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference(_containerName);
            CloudBlob blob = container.GetBlobReference(_blobName);
            string sas = blob.GetSharedAccessSignature(new SharedAccessBlobPolicy
            {
                Permissions = SharedAccessBlobPolicy.PermissionsFromString(_permissions),
                SharedAccessExpiryTime = now.AddMonths(1),
            });
            string result = blob.Uri.AbsoluteUri + sas;

            return new SecretData(result, now.AddMonths(1), now.AddDays(15));
        }
    }
}
