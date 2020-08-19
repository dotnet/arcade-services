// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.DarcLib.Models.AzureDevOps
{
    public class AzureDevOpsRepositoryResourceParameter
    {
        public string RefName { get; set; }
        public string Version { get; set; }

        public AzureDevOpsRepositoryResourceParameter(string refName, string version)
        {
            RefName = refName;
            Version = version;
        }
    }
}
