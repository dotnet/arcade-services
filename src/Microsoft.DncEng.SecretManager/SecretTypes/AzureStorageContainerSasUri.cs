using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("azure-storage-container-sas-uri")]
    public class AzureStorageContainerSasUri : SecretType<AzureStorageContainerSasUri.Parameters>
    {
        public class Parameters
        {
            public string ConnectionStringName { get; set; }
            public string Container { get; set; }
            public string Permissions { get; set; }
        }

        private readonly ISystemClock _clock;

        public AzureStorageContainerSasUri(ISystemClock clock)
        {
            _clock = clock;
        }

        protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
        {
            DateTimeOffset now = _clock.UtcNow;
            CloudStorageAccount account = CloudStorageAccount.Parse(await context.GetSecretValue(parameters.ConnectionStringName));
            CloudBlobClient blobClient = account.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference(parameters.Container);
            string sas = container.GetSharedAccessSignature(new SharedAccessBlobPolicy
            {
                Permissions = SharedAccessBlobPolicy.PermissionsFromString(parameters.Permissions),
                SharedAccessExpiryTime = now.AddMonths(1),
            });
            string result = container.Uri.AbsoluteUri + sas;

            return new SecretData(result, now.AddMonths(1), now.AddDays(15));
        }
    }
}
