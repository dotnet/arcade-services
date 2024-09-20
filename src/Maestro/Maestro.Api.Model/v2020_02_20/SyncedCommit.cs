// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.Api.Model.v2020_02_20;
public class SyncedCommit
{
    public string RepoPath { get; set; }

    public string CommitUrl { get; set; }

    public string DateCommitted { get; set; }
}
