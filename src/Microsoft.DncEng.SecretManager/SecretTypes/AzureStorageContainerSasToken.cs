using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("azure-storage-container-sas-token")]
public class AzureStorageContainerSasToken : SecretType<AzureStorageContainerSasUri.Parameters>
{        
    private readonly ISystemClock _clock;

    public AzureStorageContainerSasToken(ISystemClock clock)
    {
        _clock = clock;
    }

    protected override async Task<SecretData> RotateValue(AzureStorageContainerSasUri.Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {            
        DateTimeOffset expiresOn = _clock.UtcNow.AddMonths(1);
        DateTimeOffset nextRotationOn = _clock.UtcNow.AddDays(15);

        string connectionString = await context.GetSecretValue(parameters.ConnectionString);
        (string containerUri, string sas) containerUriAndSas = StorageUtils.GenerateBlobContainerSas(connectionString, parameters.Container, parameters.Permissions, expiresOn);

        return new SecretData(containerUriAndSas.sas, expiresOn, nextRotationOn);
    }
}
