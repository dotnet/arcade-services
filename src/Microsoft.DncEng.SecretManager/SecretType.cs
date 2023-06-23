using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager;

public abstract class SecretType : IDisposable
{
    private readonly List<string> _defaultSuffixes = new List<string> { "" };
    private readonly List<string> _noReferences = new List<string>();

    public virtual List<string> GetCompositeSecretSuffixes()
    {
        return _defaultSuffixes;
    }

    public virtual List<string> GetSecretReferences(IDictionary<string, object> parameters)
    {
        return _noReferences;
    }

    public abstract Task<List<SecretData>> RotateValues(IDictionary<string, object> parameters, RotationContext context, CancellationToken cancellationToken);

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
        private readonly SecretType _that;
        private readonly IDictionary<string, object> _parameters;

        public Bound(SecretType that, IDictionary<string, object> parameters)
        {
            _that = that;
            _parameters = parameters;
        }

        public List<string> GetCompositeSecretSuffixes()
        {
            return _that.GetCompositeSecretSuffixes();
        }

        public List<string> GetSecretReferences()
        {
            return _that.GetSecretReferences(_parameters);
        }

        public Task<List<SecretData>> RotateValues(RotationContext context, CancellationToken cancellationToken)
        {
            return _that.RotateValues(_parameters, context, cancellationToken);
        }

        public void Dispose()
        {
            _that.Dispose();
        }
    }
}

public abstract class SecretType<TParameters> : SecretType
    where TParameters : new()
{
    public sealed override List<string> GetSecretReferences(IDictionary<string, object> parameters)
    {
        if (parameters == null)
        {
            return new List<string>();
        }
        var ciParameters = new Dictionary<string, object>(parameters, StringComparer.OrdinalIgnoreCase);
        var secretReferences = new List<string>();
        foreach (PropertyInfo property in typeof(TParameters).GetProperties())
        {
            if (property.PropertyType == typeof(SecretReference))
            {
                if (ciParameters.TryGetValue(property.Name, out object propertyValue) && propertyValue != null)
                {
                    var reference = (SecretReference)ParameterConverter.ConvertValue(propertyValue, typeof(SecretReference));
                    if (string.IsNullOrEmpty(reference.Location))
                    {
                        secretReferences.Add(propertyValue.ToString());
                    }
                }
            }
        }
        return secretReferences;
    }

    public sealed override Task<List<SecretData>> RotateValues(IDictionary<string, object> parameters, RotationContext context, CancellationToken cancellationToken)
    {
        var p = ParameterConverter.ConvertParameters<TParameters>(parameters);
        return RotateValues(p, context, cancellationToken);
    }

    public virtual async Task<List<SecretData>> RotateValues(TParameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        return new List<SecretData> { await RotateValue(parameters, context, cancellationToken) };
    }

    protected virtual Task<SecretData> RotateValue(TParameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Must implement either RotateValue or RotateValues");
    }
}
