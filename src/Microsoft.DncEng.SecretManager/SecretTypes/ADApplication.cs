using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("ad-application")]
public class ADApplication : SecretType<ADApplication.Parameters>
{
    public class Parameters
    {
        public string ApplicationName { get; set; }
    }


    public const string AppIdSuffix = "-app-id";
    public const string AppSecretSuffix = "-app-secret";

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
            throw new HumanInterventionRequiredException($"User intervention required for creation or rotation of an AD Application.");
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
            _console.WriteLine($@"Steps:
1. Open https://ms.portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/Overview/quickStartType//sourceType/Microsoft_AAD_IAM/appId/{appId} under your account.
2. Navigate to Certificates & Secrets.
3. Create a new secret.
4. Delete old secret once it's saved.");
        }

        string appSecret = await _console.PromptAndValidateAsync("Application Client Secret",
            "Expecting at least 30 characters.",
            l => l != null && l.Length >= 30);

        DateTime expiresOn = await _console.PromptAndValidateAsync($"Secret expiration (M/d/yyyy)",
            "Secret expiration format must be M/d/yyyy.",
            (string value, out DateTime parsedValue) => DateTime.TryParseExact(value, "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedValue));

        return new List<SecretData> {
            new SecretData(appId, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue),
            new SecretData(appSecret, expiresOn, expiresOn.AddDays(-15)) };
    }
}
