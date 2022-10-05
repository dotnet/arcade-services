using System;

namespace Microsoft.DncEng.SecretManager;

public class SecretData
{
    public SecretData(string value, DateTimeOffset expiresOn, DateTimeOffset nextRotationOn)
    {
        Value = value;
        ExpiresOn = expiresOn;
        NextRotationOn = nextRotationOn;
    }

    public DateTimeOffset NextRotationOn { get; }
    public DateTimeOffset ExpiresOn { get; }
    public string Value { get; }
}
