using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Microsoft.DncEng.SecretManager
{
    public class RotationContext
    {
        private readonly Dictionary<string, string> _values;

        public RotationContext(IReadOnlyDictionary<string, string> values)
        {
            _values = values.ToDictionary(p => p.Key, p => p.Value);
        }

        public string GetValue(string key, string defaultValue)
        {
            return _values.TryGetValue(key, out string value) ? value : defaultValue;
        }

        public void SetValue(string key, string value)
        {
            _values[key] = value;
        }

        public IImmutableDictionary<string, string> GetValues()
        {
            return _values.ToImmutableDictionary();
        }
    }
}