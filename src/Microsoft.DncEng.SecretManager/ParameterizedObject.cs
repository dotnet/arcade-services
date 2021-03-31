using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.DncEng.SecretManager
{
    public class ParameterizedObject
    {
        protected IReadOnlyDictionary<string, string> Parameters { get; }

        protected ParameterizedObject(IReadOnlyDictionary<string, string> parameters)
        {
            Parameters = parameters;
        }

        public void ReadRequiredParameter<T>(string name, ref T storage)
        {
            if (!Parameters.TryGetValue(name, out var stringValue))
            {
                throw new ArgumentException($"Parameter '{name}' is missing.", "parameters");
            }

            if (typeof(T) == typeof(Guid))
            {
                if (!Guid.TryParse(stringValue, out var guid))
                {
                    throw new ArgumentException($"Parameter '{name}' is not a valid guid.", "parameters");
                }

                storage = (T)(object)guid;
                return;
            }

            storage = (T)Convert.ChangeType(stringValue, typeof(T), CultureInfo.InvariantCulture);
        }
    }
}