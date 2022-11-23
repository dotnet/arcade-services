// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrPatchHandler
{
    Task ApplyPatch(
        SourceMapping mapping,
        VmrIngestionPatch patch,
        CancellationToken cancellationToken);

    Task ApplyPatch(
        SourceMapping mapping,
        string patchPath,
        CancellationToken cancellationToken)
        => ApplyPatch(mapping, new VmrIngestionPatch(patchPath, VmrInfo.RelativeSourcesDir / mapping.Name), cancellationToken);

    Task<List<VmrIngestionPatch>> CreatePatches(
        SourceMapping mapping,
        LocalPath repoPath,
        string sha1,
        string sha2,
        LocalPath destDir,
        LocalPath tmpPath,
        CancellationToken cancellationToken);

    Task RestoreFilesFromPatch(
        SourceMapping mapping,
        LocalPath clonePath,
        string patch,
        CancellationToken cancellationToken);

    IReadOnlyCollection<string> GetVmrPatches(SourceMapping mapping);

    Task<IReadOnlyCollection<string>> GetPatchedFiles(
        string repoPath,
        string patchPath,
        CancellationToken cancellationToken);
}

/// <summary>
/// Patch that is created/applied during sync of the VMR.
/// </summary>
/// <param name="Path">Path where the patch is located</param>
/// <param name="ApplicationPath">Relative path within the VMR to which the patch is applied onto</param>
public record VmrIngestionPatch(string Path, string? ApplicationPath);
