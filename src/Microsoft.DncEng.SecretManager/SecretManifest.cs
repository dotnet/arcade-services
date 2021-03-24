using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Microsoft.DncEng.SecretManager
{
    public class SecretManifest
    {
        public static SecretManifest Read(string filePath)
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8);
            return Parse(reader);
        }

        public static SecretManifest Parse(TextReader reader)
        {
            IDeserializer deserializer = new DeserializerBuilder()
                .Build();

            var parser = new Parser(reader);
            return new SecretManifest(deserializer.Deserialize<Format>(parser));
        }

        #pragma warning disable IDE1006
        // ReSharper disable All
        private class Format
        {
            public Guid subscription { get; set; }
            public string name { get; set; }
            public bool missingSecretsAllowed { get; set; }
            public Dictionary<string, Key> keys { get; set; }
            public Dictionary<string, Secret> secrets { get; set; }

            public class Key
            {
                public string type { get; set; }
                public int size { get; set; }
            }

            public class Secret
            {
                public string type { get; set; }
                public string owner { get; set; }
                public string description { get; set; }
                public Dictionary<string, string> parameters { get; set; }
            }
        }
        #pragma warning restore IDE1006
        // ReSharper restore All

        private SecretManifest(Format data)
        {
            Subscription = data.subscription;
            Name = data.name;
            MissingSecretsAllowed = data.missingSecretsAllowed;
            Keys = data.keys.ToImmutableDictionary(p => p.Key, p => CreateKey(p.Value));
            Secrets = data.secrets.ToImmutableDictionary(p => p.Key, p => CreateSecret(p.Value));
        }

        public Guid Subscription { get; }
        public string Name { get; }
        public bool MissingSecretsAllowed { get; }
        public IImmutableDictionary<string, Key> Keys { get; }
        public IImmutableDictionary<string, Secret> Secrets { get; }

        private static Key CreateKey(Format.Key data)
        {
            return new Key(data.type, data.size);
        }

        public class Key
        {
            public Key(string type, int size)
            {
                Type = type;
                Size = size;
            }

            public string Type { get; }
            public int Size { get; }
        }

        private static Secret CreateSecret(Format.Secret data)
        {
            return new Secret(data.type, data.parameters, data.owner, data.description);
        }

        public class Secret
        {
            public Secret(string type, Dictionary<string, string> parameters, string owner, string description)
            {
                Type = type;
                Owner = owner;
                Description = description;
                Parameters = parameters.ToImmutableDictionary(p => p.Key, p => p.Value);
            }

            public string Type { get; }
            public string Owner { get; }
            public string Description { get; }
            public IImmutableDictionary<string, string> Parameters { get; }
        }

    }
}
