using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("azure-storage-account-sas-token")]
public class AzureStorageAccountSas : SecretType<AzureStorageAccountSas.Parameters>
{
    public class Parameters
    {
        public SecretReference ConnectionString { get; set; }
        public string Permissions { get; set; }
        public string Service { get; set; }
    }

    private readonly ISystemClock _clock;

    public AzureStorageAccountSas(ISystemClock clock)
    {
        _clock = clock;
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        DateTimeOffset expiresOn = _clock.UtcNow.AddMonths(1);
        DateTimeOffset nextRotationOn = _clock.UtcNow.AddDays(15);

        string connectionString = await context.GetSecretValue(parameters.ConnectionString);
        (string accountUri, string sas) = StorageUtils.GenerateBlobAccountSas(connectionString, parameters.Permissions, parameters.Service, expiresOn);

        return new SecretData(sas, expiresOn, nextRotationOn);
    }
}
