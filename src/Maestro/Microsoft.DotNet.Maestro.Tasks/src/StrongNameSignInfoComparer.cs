using System;
using System.Collections.Generic;

namespace Microsoft.DotNet.Maestro.Tasks
{
    /// <summary>
    ///     Compares StrongNameSignInfo objects based on certificate name, file and public key token
    /// </summary>
    public class StrongNameSignInfoComparer : IEqualityComparer<StrongNameSignInfo>
    {
        public bool Equals(StrongNameSignInfo x, StrongNameSignInfo y)
        {
            return x.CertificateName.Equals(y.CertificateName, StringComparison.OrdinalIgnoreCase) &&
                x.File.Equals(y.File, StringComparison.OrdinalIgnoreCase) &&
                x.PublicKeyToken.Equals(y.PublicKeyToken, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(StrongNameSignInfo obj)
        {
            return (obj.CertificateName, obj.File, obj.PublicKeyToken).GetHashCode();
        }
    }
}
