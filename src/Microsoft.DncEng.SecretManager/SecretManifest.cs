using System.Collections.Generic;
using System.Collections.Immutable;
using System.Dynamic;
using System.IO;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Microsoft.DncEng.SecretManager;

public class SecretManifest
{
    public static SecretManifest Read(string filePath)
    {
        using var reader = new StreamReader(filePath, Encoding.UTF8);
        var parsed = Parse<Format>(reader);
        var importFile = parsed.importSecretsFrom;
        if (!string.IsNullOrEmpty(importFile))
        {
            if (!Path.IsPathRooted(importFile))
            {
                importFile = Path.Join(Path.GetDirectoryName(filePath), importFile);
            }
            var importReader = new StreamReader(importFile, Encoding.UTF8);
            var importedSecrets = Parse<Dictionary<string, Format.Secret>>(importReader);
            foreach (var (name, secret) in importedSecrets)
            {
                parsed.secrets.Add(name, secret);
            }
        }
        return new SecretManifest(parsed);
    }

    private static T Parse<T>(TextReader reader)
    {
        IDeserializer deserializer = new DeserializerBuilder()
            .Build();

        var parser = new Parser(reader);
        return deserializer.Deserialize<T>(parser);
    }

    public static SecretManifest ParseWithoutImports(TextReader reader)
    {
        return new SecretManifest(Parse<Format>(reader));
    }

#pragma warning disable IDE1006
    // ReSharper disable All
    private class Format
    {
        public Storage storageLocation { get; set; }
        public string importSecretsFrom { get; set; }
        public Dictionary<string, Storage> references { get; set; }
        public Dictionary<string, Key> keys { get; set; }
        public Dictionary<string, Secret> secrets { get; set; }

        public class Storage
        {
            public string type { get; set; }
            public ExpandoObject parameters { get; set; }
        }

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
            public ExpandoObject parameters { get; set; }
        }
    }
#pragma warning restore IDE1006
    // ReSharper restore All

    private SecretManifest(Format data)
    {
        StorageLocation = CreateStorage(data.storageLocation);
        References = data.references?.ToImmutableDictionary(p => p.Key, p => CreateStorage(p.Value)) ?? ImmutableDictionary<string, Storage>.Empty;
        Keys = data.keys?.ToImmutableDictionary(p => p.Key, p => CreateKey(p.Value)) ?? ImmutableDictionary<string, Key>.Empty;
        Secrets = data.secrets?.ToImmutableDictionary(p => p.Key, p => CreateSecret(p.Value)) ?? ImmutableDictionary<string, Secret>.Empty;
    }

    public Storage StorageLocation { get; }
    public IImmutableDictionary<string, Storage> References { get; }
    public IImmutableDictionary<string, Key> Keys { get; }
    public IImmutableDictionary<string, Secret> Secrets { get; }

    private static Storage CreateStorage(Format.Storage data)
    {
        return new Storage(data.type, data.parameters);
    }

    public class Storage
    {
        public Storage(string type, IDictionary<string, object> parameters)
        {
            Type = type;
            Parameters = parameters;
        }

        public string Type { get; }
        public IDictionary<string, object> Parameters { get; }
    }

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
        public Secret(string type, IDictionary<string, object> parameters, string owner, string description)
        {
            Type = type;
            Owner = owner;
            Description = description;
            Parameters = parameters;
        }

        public string Type { get; }
        public string Owner { get; }
        public string Description { get; }
        public IDictionary<string, object> Parameters { get; }
    }

}
