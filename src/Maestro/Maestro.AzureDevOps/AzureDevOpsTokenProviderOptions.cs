// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace Maestro.AzureDevOps
{
    public class AzureDevOpsTokenProviderOptions
    {
        public Dictionary<string, string> Tokens { get; } = new Dictionary<string, string>();
    }
}
