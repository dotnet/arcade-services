using System;
using System.Collections.Immutable;

namespace Microsoft.DncEng.SecretManager
{
    public class SecretProperties
    {
        public SecretProperties(string name, DateTimeOffset expiresOn, DateTimeOffset nextRotationOn, IImmutableDictionary<string, string> tags)
        {
            Name = name;
            ExpiresOn = expiresOn;
            NextRotationOn = nextRotationOn;
            Tags = tags;
        }

        public DateTimeOffset NextRotationOn { get; }
        public DateTimeOffset ExpiresOn { get; }
        public IImmutableDictionary<string, string> Tags { get; }
        public string Name { get; }
    }
}