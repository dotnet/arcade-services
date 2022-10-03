using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

public abstract class GenericAccessToken : SecretType<GenericAccessToken.Parameters>
{
    public class Parameters
    {
        public string Environment { get; set; }
    }

    protected ISystemClock Clock { get; }
    protected IConsole Console { get; }

    protected abstract bool HasExpiration { get; }
    protected abstract string HelpMessage { get; }
    protected abstract string TokenName { get; }
    protected abstract string TokenFormatDescription { get; }
    protected abstract IEnumerable<KeyValuePair<string, string>> EnvironmentToHost { get; }
    protected virtual string ExpirationFormatDescription { get { return string.Empty; } }
    protected virtual TimeSpan RotateBeforeExpiration { get { return TimeSpan.FromDays(-15); } }

    public GenericAccessToken(ISystemClock clock, IConsole console)
    {
        Clock = clock;
        Console = console;
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        if (!Console.IsInteractive)
        {
            throw new HumanInterventionRequiredException($"User intervention required for creation or rotation of a {TokenName}.");
        }

        var mapHelpEnvironmentToHost = new Dictionary<string, string>(EnvironmentToHost, StringComparer.OrdinalIgnoreCase);
        string helpUrl;
        if (parameters == null)
        {
            helpUrl = "";
        }
        else if (!mapHelpEnvironmentToHost.TryGetValue(parameters.Environment, out helpUrl))
        {
            helpUrl = parameters.Environment;
        }
        Console.WriteLine(string.Format(HelpMessage, helpUrl));

        var token = await Console.PromptAndValidateAsync(TokenName,
            $"{TokenName} must be {TokenFormatDescription}.",
            ValidateToken);

        DateTimeOffset expiresOn = DateTimeOffset.MaxValue;
        DateTimeOffset nextRotationOn = Clock.UtcNow.AddMonths(6);

        if (HasExpiration)
        {
            expiresOn = await Console.PromptAndValidateAsync($"{TokenName} expiration",
                $"{TokenName} expiration must be {ExpirationFormatDescription}.",
                (ConsoleExtension.TryParse<DateTime>)TryParseExpirationDate);

            var calculatedNextRotationOn = expiresOn.Add(RotateBeforeExpiration);
            if (calculatedNextRotationOn < nextRotationOn)
                nextRotationOn = calculatedNextRotationOn;

            Console.WriteLine($"Next rotation was set to {nextRotationOn:yyyy-MM-dd}");
        }

        return new SecretData(token, expiresOn, nextRotationOn);
    }

    protected abstract bool ValidateToken(string token);
    protected abstract bool TryParseExpirationDate(string value, out DateTime parsedValue);
}
