using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager
{
    public class RotationContext
    {
        private readonly StorageLocationType.Bound _storage;
        private readonly Dictionary<string, string> _values;

        public RotationContext(string name, IReadOnlyDictionary<string, string> values, StorageLocationType.Bound storage)
        {
            _storage = storage;
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

        public async Task<string> GetSecretValue(string name)
        {
            var value = await _storage.GetSecretValueAsync(name);
            return value.Value;
        }

        public IImmutableDictionary<string, string> GetValues()
        {
            return _values.ToImmutableDictionary();
        }
    }
}
