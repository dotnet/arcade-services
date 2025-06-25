// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrPatchHandler
{
    Task ApplyPatch(
        VmrIngestionPatch patch,
        NativePath targetDirectory,
        bool removePatchAfter,
        bool reverseApply = false,
        CancellationToken cancellationToken = default);

    Task<List<VmrIngestionPatch>> CreatePatches(
        SourceMapping mapping,
        ILocalGitRepo clone,
        string sha1,
        string sha2,
        NativePath destDir,
        NativePath tmpPath,
        bool includeAdditionalMappings,
        CancellationToken cancellationToken);

    Task<List<VmrIngestionPatch>> CreatePatches(
        string patchName,
        string sha1,
        string sha2,
        UnixPath? path,
        IReadOnlyCollection<string>? filters,
        bool relativePaths,
        NativePath workingDir,
        UnixPath? applicationPath,
        bool ignoreLineEndings = false,
        CancellationToken cancellationToken = default);

    IReadOnlyCollection<VmrIngestionPatch> GetVmrPatches();

    Task<IReadOnlyCollection<UnixPath>> GetPatchedFiles(string patchPath, CancellationToken cancellationToken);
}
