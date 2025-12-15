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
    List<DependencyUpdate> DependencyUpdates)
{
    public bool HadConflicts => ConflictedFiles.Count > 0;

    public bool RecreatedPreviousFlows { get; init; }
}
