using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("random-base64")]
public class RandomBase64 : SecretType<RandomBase64.Parameters>
{
    private const string PrimarySuffix = "-primary";
    private readonly ISystemClock _clock;

    public class Parameters
    {
        public int Bytes { get; set; }
    }

    public RandomBase64(ISystemClock clock)
    {
        _clock = clock;
    }

    public override List<string> GetCompositeSecretSuffixes()
    {
        return new List<string>
        {
            PrimarySuffix,
            "-secondary",
        };
    }

    public override async Task<List<SecretData>> RotateValues(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        string currentPrimary = await context.GetSecretValue(new SecretReference(context.SecretName + PrimarySuffix));
        if (currentPrimary == null)
        {
            currentPrimary = "";
        }
        var newExpiration = DateTimeOffset.MaxValue;
        DateTimeOffset newRotateOn = _clock.UtcNow.AddMonths(1);
        var newSecondary = new SecretData(currentPrimary, newExpiration, newRotateOn);
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[parameters.Bytes];
        rng.GetNonZeroBytes(bytes);
        string newPrimaryValue = Convert.ToBase64String(bytes);
        var newPrimary = new SecretData(newPrimaryValue, newExpiration, newRotateOn);
        return new List<SecretData>
        {
            newPrimary,
            newSecondary,
        };
    }
}
