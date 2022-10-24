using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("text")]
public class TextSecretType : SecretType<TextSecretType.Parameters>
{
    private readonly IConsole _console;

    public class Parameters
    {
        public string Description { get; set; }
    }

    public TextSecretType(IConsole console)
    {
        _console = console;
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        var existing = await context.GetSecretValue(new SecretReference(context.SecretName));
        if (!_console.IsInteractive)
        {
            throw new HumanInterventionRequiredException($"Text secret rotation required. Human intervention required.");
        }
        var newValue = await _console.PromptAsync($"Input value for {context.SecretName} (empty to keep existing), {parameters.Description}: ");
        if (string.IsNullOrEmpty(newValue))
        {
            newValue = existing;
        }
        return new SecretData(newValue, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue);
    }
}
