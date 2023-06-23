using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("github-access-token")]
public class GitHubAccessToken : GitHubAccountInteractiveSecretType<GitHubAccessToken.Parameters>
{
    public class Parameters
    {
        public string Name { get; set; }
        public SecretReference GitHubBotAccountSecret { get; set; }
        public string GitHubBotAccountName { get; set; }
    }

    public GitHubAccessToken(ISystemClock clock, IConsole console) : base(clock, console)
    {
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        if (!Console.IsInteractive)
        {
            throw new HumanInterventionRequiredException($"User intervention required for creation or rotation of a GitHub access token.");
        }

        const string helpUrl = "https://github.com/settings/tokens";
        await ShowGitHubLoginInformation(context, parameters.GitHubBotAccountSecret, helpUrl, parameters.GitHubBotAccountName);

        var pat = await Console.PromptAndValidateAsync("PAT",
            "PAT must have at least 40 characters.",
            value => value != null && value.Length >= 40);

        return new SecretData(pat, DateTimeOffset.MaxValue, Clock.UtcNow.AddMonths(6));
    }
}
