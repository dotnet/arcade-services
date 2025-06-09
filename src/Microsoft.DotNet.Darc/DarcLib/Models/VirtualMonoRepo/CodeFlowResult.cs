// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

public record CodeFlowResult(
    bool HadUpdates,
    IReadOnlyCollection<UnixPath> ConflictedFiles,
    NativePath RepoPath,
    List<DependencyUpdate> DependencyUpdates,
    IReadOnlyCollection<UpstreamRepoDiff>? UpstreamRepoDiffs);

// <summary>
// Contains the old and new SHAs of an upstream repo (repo that the product repo depends on)
// </summary>
public record UpstreamRepoDiff(
    string RepoUri,
    string? OldCommitSha,
    string? NewCommitSha);
