using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

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

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context,
        CancellationToken cancellationToken)
    {
        string adAppId = await context.GetSecretValue(new SecretReference
        {
            Location = parameters.ADApplication.Location,
            Name = parameters.ADApplication.Name + ADApplication.AppIdSuffix
        });
        SecretValue adAppSecret = await context.GetSecret(new SecretReference
        {
            Location = parameters.ADApplication.Location,
            Name = parameters.ADApplication.Name + ADApplication.AppSecretSuffix
        });

        if (adAppSecret == null)
        {
            throw new InvalidOperationException($"The secret referenced by secret {context.SecretName} parameter 'adApplication' does not exist.");
        }

        var connectionString = new StringBuilder();
        connectionString.Append($"Data Source={parameters.DataSource}");
        if (!string.IsNullOrEmpty(parameters.InitialCatalog))
        {
            connectionString.Append($";Initial Catalog={parameters.InitialCatalog}");
        }

        connectionString.Append($";AAD Federated Security=True;Application Client Id={adAppId};Application Key={adAppSecret.Value}");
        if (!string.IsNullOrWhiteSpace(parameters.AdditionalParameters))
        {
            connectionString.Append($";{parameters.AdditionalParameters}");
        }

        return new SecretData(connectionString.ToString(), adAppSecret.ExpiresOn, adAppSecret.NextRotationOn);
    }
}
