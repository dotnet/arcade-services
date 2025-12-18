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
    Task<IReadOnlyCollection<UnixPath>> ApplyPatch(
        VmrIngestionPatch patch,
        NativePath targetDirectory,
        bool removePatchAfter,
        bool keepConflicts,
        bool reverseApply = false,
        bool applyToIndex = true,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<UnixPath>> ApplyPatches(
        IEnumerable<VmrIngestionPatch> patches,
        NativePath targetDirectory,
        bool removePatchAfter,
        bool keepConflicts,
        bool reverseApply = false,
        bool applyToIndex = true,
        CancellationToken cancellationToken = default);

    Task<List<VmrIngestionPatch>> CreatePatches(
        SourceMapping mapping,
        ILocalGitRepo clone,
        string sha1,
        string sha2,
        NativePath destDir,
        NativePath tmpPath,
        string[]? patchFileExclusionFilters = null,
        CancellationToken cancellationToken = default);

    Task<List<VmrIngestionPatch>> CreatePatches(
        string patchPath,
        string sha1,
        string sha2,
        UnixPath? path,
        IReadOnlyCollection<string>? filters,
        bool relativePaths,
        NativePath workingDir,
        UnixPath? applicationPath,
        bool ignoreLineEndings = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<UnixPath>> GetPatchedFiles(
        string patchPath,
        CancellationToken cancellationToken);
}
