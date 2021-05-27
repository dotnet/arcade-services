using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using System.Linq;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("kusto-connection-string")]
    public class KustoConnectionString : SecretType<KustoConnectionString.Parameters>
    {
        public class Parameters
        {
            public string ApplicationName { get; set; }
            public string DataSource { get; set; }
            public string InitialCatalog { get; set; }
            public string AdditionalParameters { get; set; }
        }

        private readonly IConsole _console;
        private const string ApplicationClientId = "Application Client Id=";


        public KustoConnectionString(IConsole console)
        {
            _console = console;
        }

        protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
        {
            if (!_console.IsInteractive)
            {
                throw new InvalidOperationException($"User intervention required for creation or rotation of a Kusto Connection String.");
            }

            string connectionString = await context.GetSecretValue(context.SecretName);
            string appId;

            if (string.IsNullOrEmpty(connectionString))
            {
                _console.WriteLine($@"Steps:
1. Open https://ms.portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/CreateApplicationBlade/quickStartType//isMSAApp/ under your account.
2. Register a new application.
3. Create a new secret under the new application.");

                appId = await _console.PromptAndValidateAsync("Application Client Id",
                                "Expecting GUID",
                                l => Guid.TryParse(l, out _));
            }
            else
            {
                string appClientIdPart = connectionString.Split(';').Single(l => l.StartsWith(ApplicationClientId));
                appId = appClientIdPart.Split('=')[1].Trim();

                _console.WriteLine($@"Steps:
1. Open https://ms.portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/Overview/quickStartType//sourceType/Microsoft_AAD_IAM/appId/{appId} under your account.
2. Navigate to Certificates & secrets.
3. Create a new secret.
4. Delete old secret once it's saved.");
            }

            string newSecret = await _console.PromptAndValidateAsync("Application Client Secret",
                                "Expecting at least 30 character.",
                                l => l != null && l.Length >= 30);

            connectionString = $"Data Source={parameters.DataSource};Initial Catalog={parameters.InitialCatalog};{ApplicationClientId}{appId};Application Key={newSecret};{parameters.AdditionalParameters}";
            _console.WriteLine($"Generated connection string: {connectionString}");

            DateTime expiresOn = await _console.PromptAndValidateAsync($"Secret expiration (M/d/yyyy)",
                "Secret expiration format must be M/d/yyyy.",
                (string value, out DateTime parsedValue) => DateTime.TryParseExact(value, "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedValue));

            return new SecretData(connectionString, expiresOn, expiresOn.AddDays(-15));
        }
    }
}
