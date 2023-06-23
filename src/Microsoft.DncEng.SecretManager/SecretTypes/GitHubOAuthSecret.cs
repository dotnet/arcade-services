using Microsoft.DncEng.CommandLineLib;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("github-oauth-secret")]
public class GitHubOAuthSecret : SecretType<GitHubOAuthSecret.Parameters>
{
    public class Parameters
    {
        public string AppName { get; set; }
        public string Description { get; set; }
    }

    private const string ClientId = "-client-id";
    private const string ClientSecret = "-client-secret";

    private readonly List<string> _suffixes = new List<string> { ClientId, ClientSecret };
    private readonly ISystemClock _clock;
    private readonly IConsole _console;

    public GitHubOAuthSecret(ISystemClock clock, IConsole console)
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
            throw new HumanInterventionRequiredException($"User intervention required for creation or rotation of GitHub OAuth secret.");
        }

        string clientId = await context.GetSecretValue(new SecretReference(context.SecretName + ClientId));
        string clientSecret = await context.GetSecretValue(new SecretReference(context.SecretName + ClientSecret));

        if (string.IsNullOrEmpty(clientId))
        {
            _console.WriteLine($"Please login to https://github.com/settings/applications/new using your GitHub account, create a new GitHub OAuth application and generate a new client secret.");

            clientId = await _console.PromptAndValidateAsync("Client Id",
                "It should be a hexadecimal number.",
                l => !string.IsNullOrEmpty(l) && l.All(c => c.IsHexChar()));

            clientSecret = await _console.PromptAndValidateAsync("Client Secret",
                "It should be a hexadecimal number with at least 40 digits",
                l => !string.IsNullOrEmpty(l) && l.Length >= 40 && l.All(c => c.IsHexChar()));
        }
        else
        {
            _console.WriteLine($"Please login to https://github.com/settings/developers using your GitHub account, open {parameters.AppName} and generate a new client secret.");

            string clientSecretNewValue = await _console.PromptAndValidateAsync($"Client Secret (empty to keep existing), {parameters.Description}",
                "It should be a hexadecimal number with at least 40 digits",
                l => string.IsNullOrEmpty(l) || (l.Length >= 40 && l.All(c => c.IsHexChar())));

            if (!string.IsNullOrEmpty(clientSecretNewValue))
            {
                clientSecret = clientSecretNewValue;
            }
        }

        return new List<SecretData> {
            new SecretData(clientId, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue),
            new SecretData(clientSecret, DateTimeOffset.MaxValue, _clock.UtcNow.AddMonths(6))};
    }
}
