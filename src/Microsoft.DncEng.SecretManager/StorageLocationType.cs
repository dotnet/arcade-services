using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Microsoft.DncEng.SecretManager;

public abstract class StorageLocationType : IDisposable
{
    public abstract Task<List<SecretProperties>> ListSecretsAsync(IDictionary<string, object> parameters);
    [ItemCanBeNull]
    public abstract Task<SecretValue> GetSecretValueAsync(IDictionary<string, object> parameters, string name);
    public abstract Task SetSecretValueAsync(IDictionary<string, object> parameters, string name, SecretValue value);
    public abstract Task EnsureKeyAsync(IDictionary<string, object> parameters, string name, SecretManifest.Key config);

    protected virtual void Dispose(bool disposing)
    {
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public Bound BindParameters(IDictionary<string, object> parameters)
    {
        return new Bound(this, parameters);
    }

    public class Bound : IDisposable
    {
        private readonly StorageLocationType _that;
        private readonly IDictionary<string, object> _parameters;

        public Bound(StorageLocationType that, IDictionary<string, object> parameters)
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

        public Task EnsureKeyAsync(string name, SecretManifest.Key config)
        {
            return _that.EnsureKeyAsync(_parameters, name, config);
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
    public sealed override Task<List<SecretProperties>> ListSecretsAsync(IDictionary<string, object> parameters)
    {
        var p = ParameterConverter.ConvertParameters<TParameters>(parameters);
        return ListSecretsAsync(p);
    }

    public sealed override Task<SecretValue> GetSecretValueAsync(IDictionary<string, object> parameters, string name)
    {
        var p = ParameterConverter.ConvertParameters<TParameters>(parameters);
        return GetSecretValueAsync(p, name);
    }

    public sealed override Task SetSecretValueAsync(IDictionary<string, object> parameters, string name, SecretValue value)
    {
        var p = ParameterConverter.ConvertParameters<TParameters>(parameters);
        return SetSecretValueAsync(p, name, value);
    }

    public sealed override Task EnsureKeyAsync(IDictionary<string, object> parameters, string name, SecretManifest.Key key)
    {
        var p = ParameterConverter.ConvertParameters<TParameters>(parameters);
        return EnsureKeyAsync(p, name, key);
    }

    public abstract Task<List<SecretProperties>> ListSecretsAsync(TParameters parameters);
    public abstract Task<SecretValue> GetSecretValueAsync(TParameters parameters, string name);
    public abstract Task SetSecretValueAsync(TParameters parameters, string name, SecretValue value);
    public abstract Task EnsureKeyAsync(TParameters parameters, string name, SecretManifest.Key config);
}
