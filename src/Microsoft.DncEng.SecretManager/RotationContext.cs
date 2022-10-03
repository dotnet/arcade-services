using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Microsoft.DncEng.SecretManager;

public class RotationContext
{
    private readonly StorageLocationType.Bound _storage;
    private readonly IReadOnlyDictionary<string, StorageLocationType.Bound> _references;
    private readonly Dictionary<string, string> _values;

    public RotationContext(string name, IReadOnlyDictionary<string, string> values, StorageLocationType.Bound storage, IReadOnlyDictionary<string, StorageLocationType.Bound> references)
    {
        _storage = storage;
        _references = references;
        SecretName = name;
        _values = values.ToDictionary(p => p.Key, p => p.Value);
    }

    public string SecretName { get; }

    public string GetValue(string key, string defaultValue)
    {
        return _values.TryGetValue(key, out string value) ? value : defaultValue;
    }

    public void SetValue(string key, string value)
    {
        _values[key] = value;
    }

    [ItemCanBeNull]
    public async Task<SecretValue> GetSecret(SecretReference reference)
    {
        SecretValue value;
        if (string.IsNullOrEmpty(reference.Location))
        {
            value = await _storage.GetSecretValueAsync(reference.Name);
        }
        else
        {
            StorageLocationType.Bound storage = _references.GetValueOrDefault(reference.Location);
            if (storage == null)
            {
                throw new InvalidOperationException($"The storage reference {reference.Location} could not be found.");
            }

            value = await storage.GetSecretValueAsync(reference.Name);
        }
        return value;
    }

    public async Task<string> GetSecretValue(SecretReference reference)
    {
        SecretValue value = await GetSecret(reference);
        return value?.Value;
    }


    public IImmutableDictionary<string, string> GetValues()
    {
        return _values.ToImmutableDictionary();
    }
}
