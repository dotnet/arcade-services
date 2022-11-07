using Microsoft.DncEng.CommandLineLib;
using System.Threading.Tasks;
using Microsoft.DotNet.Authentication.Algorithms;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

public abstract class GitHubAccountInteractiveSecretType<TParameters> : SecretType<TParameters>
    where TParameters : new()
{
    protected const string GitHubPasswordSuffix = "-password";
    protected const string GitHubSecretSuffix = "-otp";
    protected const string GitHubRecoveryCodesSuffix = "-recovery-codes";

    protected ISystemClock Clock { get; }
    protected IConsole Console { get; }

    public GitHubAccountInteractiveSecretType(ISystemClock clock, IConsole console)
    {
        Clock = clock;
        Console = console;
    }

    protected async Task ShowGitHubLoginInformation(RotationContext context, SecretReference gitHubSecret, string helpUrl, string gitHubAccountName)
    {
        var passwordReference = new SecretReference{Name = gitHubSecret.Name + GitHubPasswordSuffix, Location = gitHubSecret.Location};
        var secretReference = new SecretReference{Name = gitHubSecret.Name + GitHubSecretSuffix, Location = gitHubSecret.Location};
        var password = await context.GetSecretValue(passwordReference);
        var secret = await context.GetSecretValue(secretReference);

        await ShowGitHubLoginInformation(helpUrl, gitHubAccountName, password, secret);
    }

    protected async Task ShowGitHubLoginInformation(string helpUrl, string gitHubAccountName, string gitHubPassword, string gitHubSecret)
    {
        Console.WriteLine($"Please login to {helpUrl} using GitHub account {gitHubAccountName} and password: {gitHubPassword}");
        await ShowGitHubOneTimePassword(gitHubSecret);
    }

    protected async Task ShowGitHubOneTimePassword(string secret)
    {
        var passwordGenerator = new OneTimePasswordGenerator(secret);
        var generateTotp = true;
        while (generateTotp)
        {
            var oneTimePassword = passwordGenerator.Generate(Clock.UtcNow);
            generateTotp = await Console.ConfirmAsync($"Your one time password: {oneTimePassword}. Enter yes to generate another one: ");
        }
    }
}
