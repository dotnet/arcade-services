using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("azure-storage-container-sas-uri")]
    public class AzureStorageContainerSas : AzureStorageSasSecretType
    {
        private readonly ISystemClock _clock;
        private readonly string _containerName;
        private readonly string _permissions;

        public AzureStorageContainerSas(IReadOnlyDictionary<string, string> parameters, ISystemClock clock) : base(parameters)
        {
            _clock = clock;
            ReadRequiredParameter("container", ref _containerName);
            ReadRequiredParameter("permissions", ref _permissions);
        }

        protected override async Task<SecretData> RotateValue(RotationContext context, CancellationToken cancellationToken)
        {
            DateTimeOffset now = _clock.UtcNow;
            CloudStorageAccount account = await ConnectToAccount(context, cancellationToken);
            CloudBlobClient blobClient = account.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference(_containerName);
            string sas = container.GetSharedAccessSignature(new SharedAccessBlobPolicy
            {
                Permissions = SharedAccessBlobPolicy.PermissionsFromString(_permissions),
                SharedAccessExpiryTime = now.AddMonths(1),
            });
            string result = container.Uri.AbsoluteUri + sas;

            return new SecretData(result, now.AddMonths(1), now.AddDays(15));
        }
    }
}
