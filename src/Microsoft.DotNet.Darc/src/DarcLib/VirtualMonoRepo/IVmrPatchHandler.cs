// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrPatchHandler
{
    Task ApplyPatch(
        SourceMapping mapping,
        string patchPath,
        CancellationToken cancellationToken);
    
    Task CreatePatch(
        SourceMapping mapping,
        string repoPath,
        string sha1,
        string sha2,
        string destPath,
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
