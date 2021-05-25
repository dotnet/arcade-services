using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager
{
    public abstract class SecretType : IDisposable
    {
        private readonly List<string> _defaultSuffixes = new List<string>{""};
        public virtual List<string> GetCompositeSecretSuffixes()
        {
            return _defaultSuffixes;
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
        public sealed override Task<List<SecretData>> RotateValues(IDictionary<string, object> parameters, RotationContext context, CancellationToken cancellationToken)
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
