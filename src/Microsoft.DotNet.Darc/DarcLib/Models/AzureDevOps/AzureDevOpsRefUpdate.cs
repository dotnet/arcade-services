// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.DarcLib;

public class AzureDevOpsRefUpdate
{
    public AzureDevOpsRefUpdate(string branch, string currentSha)
    {
        Name = branch;
        OldObjectId = currentSha;
    }

    public string Name { get; set; }

    public string OldObjectId { get; set; }
}
