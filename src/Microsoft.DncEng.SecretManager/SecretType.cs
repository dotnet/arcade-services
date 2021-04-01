using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager
{
    public static class ParameterConverter
    {
        public static TParameters ConvertParameters<TParameters>(IReadOnlyDictionary<string, string> parameters)
            where TParameters : new()
        {
            var ciParams = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase);
            var result = new TParameters();
            foreach (PropertyInfo property in typeof(TParameters).GetProperties())
            {
                if (ciParams.TryGetValue(property.Name, out string value))
                {
                    property.SetValue(result, ConvertPropertyValue(value, property.PropertyType));
                }
            }

            return result;
        }

        private static object ConvertPropertyValue(string value, Type type)
        {
            if (type == typeof(Guid))
            {
                return Guid.Parse(value);
            }
            return Convert.ChangeType(value, type);
        }

    }

    public abstract class SecretType : IDisposable
    {
        private readonly List<string> _defaultSuffixes = new List<string>{""};
        public virtual List<string> GetCompositeSecretSuffixes()
        {
            return _defaultSuffixes;
        }

        public abstract Task<List<SecretData>> RotateValues(IReadOnlyDictionary<string, string> parameters, RotationContext context, CancellationToken cancellationToken);

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
            private readonly SecretType _that;
            private readonly IReadOnlyDictionary<string, string> _parameters;

            public Bound(SecretType that, IReadOnlyDictionary<string, string> parameters)
            {
                _that = that;
                _parameters = parameters;
            }

            public List<string> GetCompositeSecretSuffixes()
            {
                return _that.GetCompositeSecretSuffixes();
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
        public sealed override Task<List<SecretData>> RotateValues(IReadOnlyDictionary<string, string> parameters, RotationContext context, CancellationToken cancellationToken)
        {
            var p = ParameterConverter.ConvertParameters<TParameters>(parameters);
            return RotateValues(p, context, cancellationToken);
        }

        public virtual async Task<List<SecretData>> RotateValues(TParameters parameters, RotationContext context, CancellationToken cancellationToken)
        {
            return new List<SecretData> {await RotateValue(parameters, context, cancellationToken)};
        }

        protected virtual Task<SecretData> RotateValue(TParameters parameters, RotationContext context, CancellationToken cancellationToken)
        {
            throw new NotImplementedException("Must implement either RotateValue or RotateValues");
        }
    }
}
