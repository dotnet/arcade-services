using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("azure-storage-blob-sas-uri")]
public class AzureStorageBlobSasUri : SecretType<AzureStorageBlobSasUri.Parameters>
{
    public class Parameters
    {
        public SecretReference ConnectionString { get; set; }
        public string Container { get; set; }
        public string Blob { get; set; }
        public string Permissions { get; set; }
    }

    private readonly ISystemClock _clock;

    public AzureStorageBlobSasUri(ISystemClock clock)
    {
        _clock = clock;
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;
        CloudStorageAccount account = CloudStorageAccount.Parse(await context.GetSecretValue(parameters.ConnectionString));
        CloudBlobClient blobClient = account.CreateCloudBlobClient();
        CloudBlobContainer container = blobClient.GetContainerReference(parameters.Container);
        CloudBlob blob = container.GetBlobReference(parameters.Blob);
        string sas = blob.GetSharedAccessSignature(new SharedAccessBlobPolicy
        {
            Permissions = SharedAccessBlobPolicy.PermissionsFromString(parameters.Permissions),
            SharedAccessExpiryTime = now.AddMonths(1),
        });
        string result = blob.Uri.AbsoluteUri + sas;

        return new SecretData(result, now.AddMonths(1), now.AddDays(15));
    }
}
