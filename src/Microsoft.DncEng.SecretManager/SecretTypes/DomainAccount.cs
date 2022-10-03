using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("domain-account")]
public class DomainAccount : SecretType<DomainAccount.Parameters>
{
    public class Parameters
    {
        public string AccountName { get; set; }
        public string Description { get; set; }
    }

    private readonly ISystemClock _clock;
    private readonly IConsole _console;

    public DomainAccount(ISystemClock clock, IConsole console)
    {
        _clock = clock;
        _console = console;
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        if (!_console.IsInteractive)
        {
            throw new HumanInterventionRequiredException($"User intervention required for creation or rotation of a Domain Account.");
        }

        string generatedPassword = PasswordGenerator.GenerateRandomPassword(15, false);
        string password = await context.GetSecretValue(new SecretReference(context.SecretName));
        if (!string.IsNullOrEmpty(password))
            _console.WriteLine($"Current password for account {parameters.AccountName}: {password}");

        _console.WriteLine($@"Steps:
1. Ctrl-alt-delete on a domain joined windows computer
2. Put in the name of the domain account {parameters.AccountName}
3. Put the current secret {password} into the ""Old Password""
4. Put the new password {generatedPassword} or your custom one in the ""New Password"" field
5. Update the account");

        if (!string.IsNullOrWhiteSpace(parameters.Description))
            _console.WriteLine($"Additional information: {parameters.Description}");

        string newPassword = await _console.PromptAsync($"Enter a new password or press enter to use a generated password {generatedPassword} : ");
        if (string.IsNullOrWhiteSpace(newPassword))
            newPassword = generatedPassword;

        return new SecretData(newPassword, DateTimeOffset.MaxValue, _clock.UtcNow.AddMonths(6));
    }
}
