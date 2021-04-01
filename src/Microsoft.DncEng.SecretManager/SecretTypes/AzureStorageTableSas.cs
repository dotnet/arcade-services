using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("azure-storage-table-sas-uri")]
    public class AzureStorageTableSas : AzureStorageSasSecretType
    {
        private readonly ISystemClock _clock;
        private readonly string _table;
        private readonly string _permissions;

        public AzureStorageTableSas(IReadOnlyDictionary<string, string> parameters, ISystemClock clock) : base(parameters)
        {
            _clock = clock;
            ReadRequiredParameter("table", ref _table);
            ReadRequiredParameter("permissions", ref _permissions);
        }

        protected override async Task<SecretData> RotateValue(RotationContext context, CancellationToken cancellationToken)
        {
            DateTimeOffset now = _clock.UtcNow;
            CloudStorageAccount account = await ConnectToAccount(context, cancellationToken);
            CloudTableClient tableClient = account.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference(_table);
            string sas = table.GetSharedAccessSignature(new SharedAccessTablePolicy
            {
                Permissions = SharedAccessTablePolicy.PermissionsFromString(_permissions),
                SharedAccessExpiryTime = now.AddMonths(1),
            });
            string result = table.Uri.AbsoluteUri + sas;

            return new SecretData(result, now.AddMonths(1), now.AddDays(15));
        }
    }
}
