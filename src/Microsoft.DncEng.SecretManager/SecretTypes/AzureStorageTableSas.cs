using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("azure-storage-table-sas-uri")]
    public class AzureStorageTableSas : AzureStorageSasSecretType
    {
        private readonly string _table;
        private readonly string _permissions;
        public AzureStorageTableSas(IReadOnlyDictionary<string, string> parameters) : base(parameters)
        {
            ReadRequiredParameter("table", ref _table);
            ReadRequiredParameter("permissions", ref _permissions);
        }

        protected override async Task<SecretData> RotateValue(RotationContext context, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            var account = await ConnectToAccount(context, cancellationToken);
            var tableClient = account.CreateCloudTableClient();
            var table = tableClient.GetTableReference(_table);
            var sas = table.GetSharedAccessSignature(new SharedAccessTablePolicy
            {
                Permissions = SharedAccessTablePolicy.PermissionsFromString(_permissions),
                SharedAccessExpiryTime = now.AddMonths(1),
            });
            var result = table.Uri.AbsoluteUri + sas;

            return new SecretData(result, now.AddMonths(1), now.AddDays(15));
        }
    }
}