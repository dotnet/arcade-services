// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.DarcLib
{
    public class AzureDevOpsBuild
    {
        public long Id { get; set; }

        public string BuildNumber { get; set; }

        public AzureDevOpsBuildDefinition Definition { get; set; }

        public AzureDevOpsProject Project { get; set; }
    }
}
