// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
        VmrIngestionPatch patch,
        NativePath targetDirectory,
        CancellationToken cancellationToken);

    Task<List<VmrIngestionPatch>> CreatePatches(
        SourceMapping mapping,
        NativePath repoPath,
        string sha1,
        string sha2,
        NativePath destDir,
        NativePath tmpPath,
        CancellationToken cancellationToken);

    Task<List<VmrIngestionPatch>> CreatePatches(
        string patchName,
        string sha1,
        string sha2,
        string? path,
        IReadOnlyCollection<string>? filters,
        bool relativePaths,
        NativePath workingDir,
        UnixPath? applicationPath,
        CancellationToken cancellationToken);

    IReadOnlyCollection<string> GetVmrPatches(SourceMapping mapping) => GetVmrPatches(mapping.Name);

    IReadOnlyCollection<string> GetVmrPatches(string mappingName);

    Task<IReadOnlyCollection<UnixPath>> GetPatchedFiles(string patchPath, CancellationToken cancellationToken);
}
