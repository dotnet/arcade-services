// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Microsoft.DotNet.Maestro.Tasks
{
    /// <summary>
    ///     Compares CertificatesSignInfo objects based on Include and
    ///     DualSigningAllowed
    /// </summary>
    public class CertificatesSignInfoComparer : IEqualityComparer<CertificatesSignInfo>
    {
        public bool Equals(CertificatesSignInfo x, CertificatesSignInfo y)
        {
            return x.Include.Equals(y.Include, StringComparison.OrdinalIgnoreCase) &&
                x.DualSigningAllowed == y.DualSigningAllowed;
        }

        public int GetHashCode(CertificatesSignInfo obj)
        {
            return (obj.Include).GetHashCode();
        }
    }
}
