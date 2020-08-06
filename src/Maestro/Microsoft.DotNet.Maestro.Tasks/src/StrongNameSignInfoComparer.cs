// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
                x.Include.Equals(y.Include, StringComparison.OrdinalIgnoreCase) &&
                x.PublicKeyToken.Equals(y.PublicKeyToken, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(StrongNameSignInfo obj)
        {
            return (obj.CertificateName, obj.Include, obj.PublicKeyToken).GetHashCode();
        }
    }
}
