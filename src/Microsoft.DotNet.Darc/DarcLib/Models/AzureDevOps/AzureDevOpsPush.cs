// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib;

public class AzureDevOpsPush
{
    public AzureDevOpsPush(AzureDevOpsRefUpdate refUpdate, AzureDevOpsCommit vstsCommit)
    {
        RefUpdates = new List<AzureDevOpsRefUpdate> {refUpdate};
        Commits = new List<AzureDevOpsCommit> {vstsCommit};
    }

    public List<AzureDevOpsRefUpdate> RefUpdates { get; set; }

    public List<AzureDevOpsCommit> Commits { get; set; }
}
