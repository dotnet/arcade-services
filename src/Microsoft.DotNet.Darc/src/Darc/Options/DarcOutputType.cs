// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.Darc.Options
{
    /// <summary>
    /// Output type that darc should use. Commandline enum parsing is case sensitive
    /// so we put these as lower case.
    /// </summary>
    public enum DarcOutputType
    {
        yaml,
        json
    }
}
