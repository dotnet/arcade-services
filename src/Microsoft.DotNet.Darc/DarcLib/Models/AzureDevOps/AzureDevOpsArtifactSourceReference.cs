// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.DarcLib;

public class AzureDevOpsArtifactSourceReference
{
    public AzureDevOpsIdNamePair DefaultVersionSpecific { get; set; }

    public AzureDevOpsIdNamePair DefaultVersionType { get; set; }

    public AzureDevOpsIdNamePair Definition { get; set; }

    public AzureDevOpsIdNamePair Project { get; set; }
}
