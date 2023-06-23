using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Management.Storage.Models;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.CommandLineLib.Authentication;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("azure-storage-connection-string")]
public class AzureStorageConnectionString : SecretType<AzureStorageConnectionString.Parameters>
{
    public class Parameters
    {
        public Guid Subscription { get; set; }
        public SecretReference StorageKeySecret { get; set; }
        public string Account { get; set; }
    }

    private readonly TokenCredentialProvider _tokenCredentialProvider;
    private readonly ISystemClock _clock;

    public AzureStorageConnectionString(TokenCredentialProvider tokenCredentialProvider, ISystemClock clock)
    {
        _tokenCredentialProvider = tokenCredentialProvider;
        _clock = clock;
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        string key;
        DateTimeOffset expiresOn;
        DateTimeOffset nextRotationOn;

        if (parameters.StorageKeySecret != null)
        {
            SecretValue storageKeySecret = await context.GetSecret(parameters.StorageKeySecret);

            key = storageKeySecret.Value;
            expiresOn = storageKeySecret.ExpiresOn;
            nextRotationOn = storageKeySecret.NextRotationOn;
        }
        else
        {
            key = await StorageUtils.RotateStorageAccountKey(parameters.Subscription.ToString(), parameters.Account, context, _tokenCredentialProvider, cancellationToken);
            expiresOn = DateTimeOffset.MaxValue;
            nextRotationOn = _clock.UtcNow.AddMonths(6);
        }

        string connectionString = $"DefaultEndpointsProtocol=https;AccountName={parameters.Account};AccountKey={key}";

        return new SecretData(connectionString, expiresOn, nextRotationOn);
    }
}
