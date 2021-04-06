using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Microsoft.DncEng.SecretManager
{
    public abstract class StorageLocationType : IDisposable
    {
        public abstract Task<List<SecretProperties>> ListSecretsAsync(IReadOnlyDictionary<string, string> parameters);
        [ItemCanBeNull]
        public abstract Task<SecretValue> GetSecretValueAsync(IReadOnlyDictionary<string, string> parameters, string name);
        public abstract Task SetSecretValueAsync(IReadOnlyDictionary<string, string> parameters, string name, SecretValue value);

        protected virtual void Dispose(bool disposing)
        {
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public Bound BindParameters(IReadOnlyDictionary<string, string> parameters)
        {
            return new Bound(this, parameters);
        }

        public class Bound : IDisposable
        {
            private readonly StorageLocationType _that;
            private readonly IReadOnlyDictionary<string, string> _parameters;

            public Bound(StorageLocationType that, IReadOnlyDictionary<string, string> parameters)
            {
                _that = that;
                _parameters = parameters;
            }

            public Task<List<SecretProperties>> ListSecretsAsync()
            {
                return _that.ListSecretsAsync(_parameters);
            }

            [ItemCanBeNull]
            public Task<SecretValue> GetSecretValueAsync(string name)
            {
                return _that.GetSecretValueAsync(_parameters, name);
            }

            public Task SetSecretValueAsync(string name, SecretValue value)
            {
                return _that.SetSecretValueAsync(_parameters, name, value);
            }

            public void Dispose()
            {
                _that.Dispose();
            }
        }
    }

    public abstract class StorageLocationType<TParameters> : StorageLocationType
        where TParameters : new()
    {
        public sealed override Task<List<SecretProperties>> ListSecretsAsync(IReadOnlyDictionary<string, string> parameters)
        {
            var p = ParameterConverter.ConvertParameters<TParameters>(parameters);
            return ListSecretsAsync(p);
        }

        public sealed override Task<SecretValue> GetSecretValueAsync(IReadOnlyDictionary<string, string> parameters, string name)
        {
            var p = ParameterConverter.ConvertParameters<TParameters>(parameters);
            return GetSecretValueAsync(p, name);
        }

        public sealed override Task SetSecretValueAsync(IReadOnlyDictionary<string, string> parameters, string name, SecretValue value)
        {
            var p = ParameterConverter.ConvertParameters<TParameters>(parameters);
            return SetSecretValueAsync(p, name, value);
        }

        public abstract Task<List<SecretProperties>> ListSecretsAsync(TParameters parameters);
        public abstract Task<SecretValue> GetSecretValueAsync(TParameters parameters, string name);
        public abstract Task SetSecretValueAsync(TParameters parameters, string name, SecretValue value);
    }
}
