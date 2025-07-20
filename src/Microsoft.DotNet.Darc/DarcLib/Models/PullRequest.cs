// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.DotNet.DarcLib.Models;
public class PullRequest : ICachableGitResource
{
    public string Title { get; set; }
    public string Description { get; set; }
    public string BaseBranch { get; set; }
    public string HeadBranch { get; set; }
    public PrStatus Status { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string HeadCommitSha { get; set; }
    public string ETag { get; set; }

}
