using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager
{
    public abstract class StorageLocationType : ParameterizedObject, IDisposable
    {
        protected StorageLocationType(IReadOnlyDictionary<string, string> parameters) : base(parameters)
        {
        }

        public abstract Task<List<SecretProperties>> ListSecretsAsync();
        public abstract Task<SecretValue> GetSecretValueAsync(string name);
        public abstract Task SetSecretValueAsync(string name, SecretValue value);

        protected virtual void Dispose(bool disposing)
        {
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
