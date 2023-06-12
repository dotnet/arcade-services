// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib;

public class AzureDevOpsCommit
{
    public AzureDevOpsCommit(List<AzureDevOpsChange> changes, string commitComment)
    {
        Changes = changes;
        Comment = commitComment;
    }

    public List<AzureDevOpsChange> Changes { get; set; }

    public string Comment { get; set; }
}
