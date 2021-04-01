using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    public abstract class AzureStorageSasSecretType : SecretType
    {
        private readonly string _connectionStringName;

        protected AzureStorageSasSecretType(IReadOnlyDictionary<string, string> parameters) : base(parameters)
        {
            ReadRequiredParameter("connectionStringName", ref _connectionStringName);
        }

        protected async Task<CloudStorageAccount> ConnectToAccount(RotationContext context, CancellationToken cancellationToken)
        {
            var connectionString = await context.GetSecretValue(_connectionStringName);
            return CloudStorageAccount.Parse(connectionString);
        }
    }
}
