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
    Task ApplyPatch(VmrIngestionPatch patch, CancellationToken cancellationToken);

    Task ApplyPatch(
        SourceMapping mapping,
        string patchPath,
        CancellationToken cancellationToken)
        => ApplyPatch(new VmrIngestionPatch(patchPath, VmrInfo.RelativeSourcesDir / mapping.Name), cancellationToken);

    Task<List<VmrIngestionPatch>> CreatePatches(
        SourceMapping mapping,
        LocalPath repoPath,
        string sha1,
        string sha2,
        LocalPath destDir,
        LocalPath tmpPath,
        CancellationToken cancellationToken);

    IReadOnlyCollection<string> GetVmrPatches(SourceMapping mapping);

    Task<IReadOnlyCollection<UnixPath>> GetPatchedFiles(string patchPath, CancellationToken cancellationToken);
}
