// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.DarcLib.Models.AzureDevOps;

public enum AzureDevOpsCheckState
{
    None,
    Queued,
    Broken,
    Rejected,
    Approved,
    Running
}
