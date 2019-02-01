// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.DarcLib
{
    public class AzureDevOpsArtifact
    {
        public string Type { get; set; }

        public string Alias { get; set; }

        public AzureDevOpsArtifactSourceReference DefinitionReference { get; set; }
    }
}
