// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Microsoft.DotNet.Maestro.Tasks
{
    /// <summary>
    ///     Compares FileSignInfo objects based on file and certificate name
    /// </summary>
    public class FileSignInfoComparer : IEqualityComparer<FileSignInfo>
    {
        public bool Equals(FileSignInfo x, FileSignInfo y)
        {
            return x.CertificateName.Equals(y.CertificateName, StringComparison.OrdinalIgnoreCase) &&
                x.File.Equals(y.File, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(FileSignInfo obj)
        {
            return (obj.CertificateName, obj.File).GetHashCode();
        }
    }
}
