using Microsoft.DncEng.CommandLineLib;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("github-app-secret")]
public class GitHubAppSecret : SecretType<GitHubAppSecret.Parameters>
{
    public class Parameters
    {
        public bool HasPrivateKey { get; set; }
        public bool HasWebhookSecret { get; set; }
        public bool HasOAuthSecret { get; set; }

    }

    private const string AppId = "-app-id";
    private const string AppPrivateKey = "-app-private-key";
    private const string OAuthId = "-oauth-id";
    private const string OAuthSecret = "-oauth-secret";
    private const string AppHookSecret = "-app-webhook-secret";

    private readonly List<string> _suffixes = new List<string> { AppId, AppPrivateKey, OAuthId, OAuthSecret, AppHookSecret };
    private readonly ISystemClock _clock;
    private readonly IConsole _console;

    public GitHubAppSecret(ISystemClock clock, IConsole console)
    {
        _clock = clock;
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
            throw new HumanInterventionRequiredException($"User intervention required for creation or rotation of GitHub App Secret.");
        }

        string appId = await context.GetSecretValue(new SecretReference(context.SecretName + AppId));
        string privateKey = await context.GetSecretValue(new SecretReference(context.SecretName + AppPrivateKey));
        string oauthId = await context.GetSecretValue(new SecretReference(context.SecretName + OAuthId));
        string oauthSecret = await context.GetSecretValue(new SecretReference(context.SecretName + OAuthSecret));
        string webhookSecret = await context.GetSecretValue(new SecretReference(context.SecretName + AppHookSecret));
        bool isNew = string.IsNullOrEmpty(appId);

        if (isNew)
        {
            _console.WriteLine("Please login to https://github.com/settings/apps/new using your GitHub account, create a new GitHub App and generate a new private key.");

            string name = await _console.PromptAsync("Application Name (From the Url)");
            context.SetValue("app-name", name);
            appId = await _console.PromptAndValidateAsync("App Id",
                "Allowed are only digits.",
                l => !string.IsNullOrEmpty(l) && l.All(c => char.IsDigit(c)));
        }
        else
        {
            string name = context.GetValue("app-name", "<null>");
            _console.WriteLine($"Please login to https://github.com/settings/apps/{name} using your GitHub account.");
            _console.WriteLine("To roll Private Key and Client Secret: first generate a new one and remove the old once it was successfully saved.");
        }

        if (parameters.HasPrivateKey)
        {                
            privateKey = await _console.PromptAndValidateAsync("Private Key file path",
                "Allowed are only valid pem files with private key.",
                (ConsoleExtension.TryParse<string>)TryParsePemFileWithPrivateKey);
        }

        if (parameters.HasOAuthSecret)
        {
            oauthId = await _console.PromptAndValidateAsync("OAuth Client Id",
                "Iv1. followed by 16 hex digits",
                l => !string.IsNullOrEmpty(l) && Regex.IsMatch(l, "^Iv1\\.[a-fA-F0-9]{16}$"));
            oauthSecret = await _console.PromptAndValidateAsync("OAuth Client Secret",
                "Hexadecimal number with at least 40 digits",
                l => !string.IsNullOrEmpty(l) && l.Length >= 40 && l.All(c => c.IsHexChar()));
        }

        if (parameters.HasWebhookSecret)
        {
            webhookSecret = await _console.PromptAndValidateAsync("Webhook Secret",
                "is required",
                l => !string.IsNullOrWhiteSpace(l));
        }
            

        DateTimeOffset rollOn = _clock.UtcNow.AddMonths(6);

        return new List<SecretData> {
            new SecretData(appId, DateTimeOffset.MaxValue, rollOn),
            new SecretData(privateKey, DateTimeOffset.MaxValue, rollOn),
            new SecretData(oauthId, DateTimeOffset.MaxValue, rollOn),
            new SecretData(oauthSecret, DateTimeOffset.MaxValue, rollOn),
            new SecretData(webhookSecret, DateTimeOffset.MaxValue, rollOn)};
    }

    private bool TryParsePemFileWithPrivateKey(string value, out string parsedValue)
    {
        parsedValue = null;
        if (!File.Exists(value))
            return false;

        try
        {
            parsedValue = File.ReadAllText(value);
            return Regex.IsMatch(parsedValue, @"^\s*\-+BEGIN RSA PRIVATE KEY\-+[A-Za-z0-9=\/\+\s]+\-+END RSA PRIVATE KEY\-+\s*$");
        }
        catch (Exception ex)
        {
            _console.WriteError($"Failed to read PEM file {value} with {ex}");
        }

        return false;
    }
}
