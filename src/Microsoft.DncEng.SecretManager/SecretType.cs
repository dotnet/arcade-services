using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager
{
    public abstract class SecretType : ParameterizedObject, IDisposable
    {
        protected SecretType() : base(ImmutableDictionary.Create<string, string>())
        {
        }

        protected SecretType(IReadOnlyDictionary<string, string> parameters) : base(parameters)
        {
        }

        private readonly List<string> _defaultSuffixes = new List<string> {""};
        public virtual List<string> GetCompositeSecretSuffixes()
        {
            return _defaultSuffixes;
        }

        public virtual async Task<List<SecretData>> RotateValues(RotationContext context, CancellationToken cancellationToken)
        {
            return new List<SecretData> {await RotateValue(context, cancellationToken)};
        }

        protected virtual Task<SecretData> RotateValue(RotationContext context, CancellationToken cancellationToken)
        {
            throw new NotImplementedException("Must implement either RotateValue or RotateValues");
        }

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
