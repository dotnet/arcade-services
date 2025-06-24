// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable
using System.Diagnostics;

namespace Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

[DebuggerDisplay("{Name}: {SourceSha} -> {TargetSha}")]
public abstract record Codeflow(string SourceSha, string TargetSha)
{
    public abstract string RepoSha { get; init; }

    public abstract string VmrSha { get; init; }

    public string GetBranchName() => $"darc/{Name}/{Commit.GetShortSha(SourceSha)}-{Commit.GetShortSha(TargetSha)}";

    public abstract string Name { get; }
}

public record ForwardFlow(string RepoSha, string VmrSha) : Codeflow(RepoSha, VmrSha)
{
    public override string Name { get; } = "forward";
}

public record Backflow(string VmrSha, string RepoSha) : Codeflow(VmrSha, RepoSha)
{
    public override string Name { get; } = "back";
}
