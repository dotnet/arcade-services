// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public class CodeflowHistory
{
    public string SorceRepoUrl { get; set; }
    public string TargetRepoUrl { get; set; }
    public List<Commit> RepoCommits { get; set; }
    public List<Commit> VmrCommits { get; set; }
    public List<Codeflow> ForwardFlows {get; set; }
    public List<Codeflow> Backflows { get; set; }
}

public class Codeflow
{
    public string SourceCommitSha { get; set; }
    public string TargetCommitSha { get; set; }
}
