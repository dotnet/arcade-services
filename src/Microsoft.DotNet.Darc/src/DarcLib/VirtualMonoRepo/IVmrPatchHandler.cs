// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrPatchHandler
{
    Task ApplyPatch(
        SourceMapping mapping,
        VmrIngestionPatch patch,
        CancellationToken cancellationToken);
    
    Task<List<VmrIngestionPatch>> CreatePatches(
        SourceMapping mapping,
        string repoPath,
        string sha1,
        string sha2,
        string destDir,
        string tmpPath,
        CancellationToken cancellationToken);

    Task RestorePatchedFilesFromRepo(
        SourceMapping mapping,
        string clonePath,
        string originalRevision,
        CancellationToken cancellationToken);

    Task ApplyVmrPatches(
        SourceMapping mapping,
        CancellationToken cancellationToken);
}

/// <summary>
/// Patch that is created/applied during sync of the VMR.
/// </summary>
/// <param name="Path">Path where the patch is located</param>
/// <param name="ApplicationPath">Relative path within the VMR to which the patch is applied onto</param>
public record VmrIngestionPatch(string Path, string ApplicationPath);
