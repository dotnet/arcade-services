using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("github-account")]
public class GitHubBotAccount : GitHubAccountInteractiveSecretType<GitHubBotAccount.Parameters>
{
    private readonly List<string> _suffixes = new List<string> { GitHubPasswordSuffix, GitHubRecoveryCodesSuffix, GitHubSecretSuffix };
    private readonly Regex _recoveryCodesRegex = new Regex(@"^([a-fA-F0-9]{5}-?[a-fA-F0-9]{5}\s+)*[a-fA-F0-9]{5}-?[a-fA-F0-9]{5}$");
    private readonly Regex _secretRegex = new Regex(@"[A-Za-z2-7]+");

    public class Parameters
    {
        public string Name { get; set; }
    }

    public GitHubBotAccount(ISystemClock clock, IConsole console) : base(clock, console)
    {
    }

    public override List<string> GetCompositeSecretSuffixes()
    {
        return _suffixes;
    }

    public override async Task<List<SecretData>> RotateValues(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        if (!Console.IsInteractive)
        {
            throw new HumanInterventionRequiredException($"User intervention required for creation or rotation of GitHub bot account.");
        }

        string password = await context.GetSecretValue(new SecretReference(context.SecretName + GitHubPasswordSuffix));
        if (string.IsNullOrEmpty(password))
        {
            return await NewAccount(parameters);
        }
        else
        {
            return await UpdateAccount(password, parameters, context);
        }
    }

    private async Task<List<SecretData>> UpdateAccount(string password, Parameters parameters, RotationContext context)
    {
        var secrets = new List<SecretData>(3);
        var secret = await context.GetSecretValue(new SecretReference(context.SecretName + GitHubSecretSuffix));

        const string helpUrl = "https://github.com/settings/security";
        await ShowGitHubLoginInformation(helpUrl, parameters.Name, secret, password);

        var rollPassword = await Console.ConfirmAsync("Do you want to roll bot's password (yes/no): ");
        if (rollPassword)
        {
            var newPassword = await AskUserForPassword();
            secrets.Add(new SecretData(newPassword, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue));
        }
        else
        {
            secrets.Add(new SecretData(password, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue));
        }

        bool rollSecret = await Console.ConfirmAsync("Do you want to roll bot's secret (yes/no): "); ;

        bool rollRecoveryCodes = true;
        if (!rollSecret)
            rollRecoveryCodes = await Console.ConfirmAsync("Do you want to roll recovery codes (yes/no): ");
        else
            Console.WriteLine("Be aware that roll of the secret also rolls recovery codes.");

        if (rollRecoveryCodes)
        {
            var newRecoveryCodes = await AskUserForRecoveryCodes();
            secrets.Add(new SecretData(newRecoveryCodes, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue));
        }
        else
        {
            string recoveryCodes = await context.GetSecretValue(new SecretReference(context.SecretName + GitHubRecoveryCodesSuffix));
            secrets.Add(new SecretData(recoveryCodes, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue));
        }

        if (rollSecret)
        {
            var newSecret = await AskUserForSecretAndShowConfirmationCode();
            secrets.Add(new SecretData(newSecret, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue));
        }
        else
        {
            secrets.Add(new SecretData(secret, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue));
        }

        return secrets;
    }

    private async Task<List<SecretData>> NewAccount(Parameters parameters)
    {
        Console.WriteLine($"Please sign up for a new GitHub account {parameters.Name}.");

        var password = await AskUserForPassword();

        Console.WriteLine("Enable two factor authentification using Authenticator app.");

        var recoveryCodes = await AskUserForRecoveryCodes();
        var secret = await AskUserForSecretAndShowConfirmationCode();

        return new List<SecretData> {
            new SecretData(password, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue),
            new SecretData(recoveryCodes, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue),
            new SecretData(secret, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue)};
    }

    private async Task<string> AskUserForRecoveryCodes()
    {
        return await Console.PromptAndValidateAsync("recovery codes",
            "It should be a list of 10 hexadecimal digits with optional dash in the middle, separated by space.",
            l => l != null && _recoveryCodesRegex.IsMatch(l));
    }

    private async Task<string> AskUserForSecretAndShowConfirmationCode()
    {
        string secret = await Console.PromptAndValidateAsync("secret",
            "Allowed chars are A-Z and digits 2-7.",
            l => l != null && _secretRegex.IsMatch(l));

        await ShowGitHubOneTimePassword(secret);
        return secret;
    }

    private async Task<string> AskUserForPassword()
    {
        var password = PasswordGenerator.GenerateRandomPassword(15, true);
        var customPassword = await Console.PromptAsync($"Enter a new password or press enter to use a generated password {password} : ");
        if (string.IsNullOrWhiteSpace(customPassword))
            return password;

        return customPassword.Trim();
    }
}
