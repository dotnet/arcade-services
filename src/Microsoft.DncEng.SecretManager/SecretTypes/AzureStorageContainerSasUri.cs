using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("azure-storage-container-sas-uri")]
public class AzureStorageContainerSasUri : SecretType<AzureStorageContainerSasUri.Parameters>
{
    public class Parameters
    {
        public SecretReference ConnectionString { get; set; }
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
        DateTimeOffset expiresOn = _clock.UtcNow.AddMonths(1);
        DateTimeOffset nextRotationOn = _clock.UtcNow.AddDays(15);

        string connectionString = await context.GetSecretValue(parameters.ConnectionString);
        (string containerUri, string sas) containerUriAndSas = StorageUtils.GenerateBlobContainerSas(connectionString, parameters.Container, parameters.Permissions, expiresOn);
        string uriWithSas = containerUriAndSas.containerUri + containerUriAndSas.sas;

        return new SecretData(uriWithSas, expiresOn, nextRotationOn);
    }
}
