using System;
using System.Collections.Immutable;

namespace Microsoft.DncEng.SecretManager;

public class SecretValue
{
    public SecretValue(string value, IImmutableDictionary<string, string> tags, DateTimeOffset nextRotationOn, DateTimeOffset expiresOn)
    {
        Value = value;
        Tags = tags;
        NextRotationOn = nextRotationOn;
        ExpiresOn = expiresOn;
    }

    public string Value { get; }
    public IImmutableDictionary<string, string> Tags { get; }
    public DateTimeOffset NextRotationOn { get; }
    public DateTimeOffset ExpiresOn { get; }
}
