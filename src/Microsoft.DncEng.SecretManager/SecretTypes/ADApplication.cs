using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("ad-application")]
    public class ADApplication : SecretType<ADApplication.Parameters>
    {
        public class Parameters
        {
            public string ApplicationName { get; set; }
            public string KustoConnectionStrings { get; set; }
        }

        private const string ApplicationClientId = "Application Client Id=";
        private const string ApplicationKey = "Application Key=";
        private const string AppIdSuffix = "-app-id";
        private const string AppSecretSuffix = "-app-secret";

        private readonly IConsole _console;
        private readonly List<string> _suffixes = new List<string> { AppIdSuffix, AppSecretSuffix };


        public ADApplication(IConsole console)
        {
            _console = console;
        }

        public override List<string> GetCompositeSecretSuffixes()
        {
            return _suffixes;
        }

        public override async Task<List<SecretData>> RotateValues(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
        {
            if (!_console.IsInteractive)
            {
                throw new InvalidOperationException($"User intervention required for creation or rotation of an AD Application.");
            }

            string appId = await context.GetSecretValue(new SecretReference(context.SecretName + AppIdSuffix));

            if (string.IsNullOrEmpty(appId))
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
                _console.WriteLine($@"Be aware that all dependent Kusto connection strings {parameters.KustoConnectionStrings} will be regenerated.
Steps:
1. Open https://ms.portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/Overview/quickStartType//sourceType/Microsoft_AAD_IAM/appId/{appId} under your account.
2. Navigate to Certificates & secrets.
3. Create a new secret.
4. Delete old secret once it's saved.");
            }

            string appSecret = await _console.PromptAndValidateAsync("Application Client Secret",
                                "Expecting at least 30 character.",
                                l => l != null && l.Length >= 30);

            DateTime expiresOn = await _console.PromptAndValidateAsync($"Secret expiration (M/d/yyyy)",
                "Secret expiration format must be M/d/yyyy.",
                (string value, out DateTime parsedValue) => DateTime.TryParseExact(value, "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedValue));

            DateTime rotateOn = expiresOn.AddDays(-15);

            var kustoConnectionStringsSecrets = parameters.KustoConnectionStrings.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var kustoConnectionStringSecret in kustoConnectionStringsSecrets)
            {
                await RollKustoConnectionString(context, kustoConnectionStringSecret.Trim(), appId, appSecret, expiresOn, rotateOn);
            }

            return new List<SecretData> {
                    new SecretData(appId, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue),
                    new SecretData(appSecret, expiresOn, rotateOn) };
        }

        private async Task RollKustoConnectionString(RotationContext context, string secretName, string appId, string appSecret, DateTime expiresOn, DateTime rotateOn)
        {
            string connectionString = await context.GetSecretValue(new SecretReference(secretName));

            if (string.IsNullOrEmpty(connectionString))
            {
                string dataSource = await _console.PromptAndValidateAsync($"Data source for Kusto connection string {secretName}",
                                                "Expecting URI",
                                                l => Uri.TryCreate(l, UriKind.Absolute, out _));
                string initialCatalog = await _console.PromptAndValidateAsync($"Initial catalog for Kusto connection string {secretName}",
                                                "Expecting non empty string",
                                                l => !string.IsNullOrWhiteSpace(l));
                string aadFederatedSecurity = await _console.PromptAndValidateAsync($"AAD Federated Security for Kusto connection string {secretName}",
                                                "Expecting boolean",
                                                l => bool.TryParse(l, out _));
                string additionalParameters = await _console.PromptAsync($"Enter additional parameters for Kusto connection string {secretName}: ");

                if (!string.IsNullOrWhiteSpace(additionalParameters))
                    additionalParameters = ";" + additionalParameters;

                connectionString = $"Data Source={dataSource};Initial Catalog={initialCatalog};AAD Federated Security={aadFederatedSecurity};{ApplicationClientId}{appId};{ApplicationKey}{appSecret}{additionalParameters}";
            }
            else
            {
                string[] parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].StartsWith(ApplicationKey, true, CultureInfo.InvariantCulture))
                        parts[i] = ApplicationKey + appSecret;
                }
                connectionString = string.Join(';', parts);
            }

            _console.WriteLine($"Storing new value in storage for secret Kusto connection string {secretName}: {connectionString}");

            await context.SetSecretValue(secretName, new SecretValue(connectionString, context.GetValues(), expiresOn, rotateOn));
        }
    }
}
