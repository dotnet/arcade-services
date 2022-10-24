using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("azure-storage-table-sas-uri")]
public class AzureStorageTableSasUri : SecretType<AzureStorageTableSasUri.Parameters>
{
    public class Parameters
    {
        public SecretReference ConnectionString { get; set; }
        public string Table { get; set; }
        public string Permissions { get; set; }
    }

    private readonly ISystemClock _clock;

    public AzureStorageTableSasUri(ISystemClock clock)
    {
        _clock = clock;
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;
        CloudStorageAccount account = CloudStorageAccount.Parse(await context.GetSecretValue(parameters.ConnectionString));
        CloudTableClient tableClient = account.CreateCloudTableClient();
        CloudTable table = tableClient.GetTableReference(parameters.Table);
        string sas = table.GetSharedAccessSignature(new SharedAccessTablePolicy
        {
            Permissions = SharedAccessTablePolicy.PermissionsFromString(parameters.Permissions),
            SharedAccessExpiryTime = now.AddMonths(1),
        });
        string result = table.Uri.AbsoluteUri + sas;

        return new SecretData(result, now.AddMonths(1), now.AddDays(15));
    }
}
