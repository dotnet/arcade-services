// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Microsoft.DotNet.Maestro.Tasks
{
    /// <summary>
    ///     Compares FileExtensionSignInfo objects based on extension and certificate name
    /// </summary>
    public class FileExtensionSignInfoComparer : IEqualityComparer<FileExtensionSignInfo>
    {
        public bool Equals(FileExtensionSignInfo x, FileExtensionSignInfo y)
        {
            return x.Extension.Equals(y.Extension, StringComparison.OrdinalIgnoreCase) &&
                x.CertificateName.Equals(y.CertificateName, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(FileExtensionSignInfo obj)
        {
            return (obj.Extension, obj.CertificateName).GetHashCode();
        }
    }
}
