using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("kusto-connection-string")]
    public class KustoConnectionString : SecretType<KustoConnectionString.Parameters>
    {
        public class Parameters
        {
            public string DataSource { get; set; }
            public string InitialCatalog { get; set; }
            public string AdditionalParameters { get; set; }
            public SecretReference ADApplication { get; set; }
        }

        private readonly ISystemClock _clock;

        public KustoConnectionString(ISystemClock clock)
        {
            _clock = clock;
        }

        protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
        {
            string adAppId = await context.GetSecretValue(new SecretReference { Location = parameters.ADApplication.Location, Name = parameters.ADApplication.Name + ADApplication.AppIdSuffix });
            SecretValue adAppSecret = await context.GetSecret(new SecretReference { Location = parameters.ADApplication.Location, Name = parameters.ADApplication.Name + ADApplication.AppSecretSuffix });

            string connectionString = $"Data Source={parameters.DataSource};Initial Catalog={parameters.InitialCatalog};AAD Federated Security=True;Application Client Id={adAppId};Application Key={adAppSecret?.Value}";
            if (!string.IsNullOrWhiteSpace(parameters.AdditionalParameters))
                connectionString += $";{parameters.AdditionalParameters}";

            return new SecretData(connectionString, adAppSecret.ExpiresOn, adAppSecret.NextRotationOn);
        }
    }
}
