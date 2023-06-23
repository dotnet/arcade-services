using System;
using System.Collections.Immutable;

namespace Microsoft.DncEng.SecretManager;

public class SecretProperties
{
    public SecretProperties(string name, DateTimeOffset expiresOn, IImmutableDictionary<string, string> tags)
    {
        Name = name;
        ExpiresOn = expiresOn;
        Tags = tags;
    }

    public DateTimeOffset ExpiresOn { get; }
    public IImmutableDictionary<string, string> Tags { get; }
    public string Name { get; }
}
