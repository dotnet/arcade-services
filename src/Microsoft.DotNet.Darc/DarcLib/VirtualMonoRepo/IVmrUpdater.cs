// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrUpdater
{
    /// <summary>
    /// Updates repo in the VMR to given revision.
    /// </summary>
    /// <param name="mappingName">Name of a repository mapping</param>
    /// <param name="targetRevision">Revision (commit SHA, branch, tag..) onto which to synchronize, leave empty for HEAD</param>
    /// <param name="targetVersion">Version of packages, that the SHA we're updating to, produced</param>
    /// <param name="noSquash">Whether to pull changes commit by commit instead of squashing all updates into one</param>
    /// <param name="updateDependencies">When true, updates dependencies (from Version.Details.xml) recursively</param>
    /// <param name="additionalRemotes">Additional git remotes to use when fetching</param>
    /// <param name="readmeTemplatePath">Path to VMR's README.md template</param>
    /// <param name="tpnTemplatePath">Path to VMR's THIRD-PARTY-NOTICES.md template</param>
    /// <param name="generateCodeowners">Whether to generate a CODEOWNERS file</param>
    /// <param name="discardPatches">Whether to clean up genreated .patch files after their used</param>
    Task UpdateRepository(
        string mappingName,
        string? targetRevision,
        string? targetVersion,
        bool updateDependencies,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        string? readmeTemplatePath,
        string? tpnTemplatePath,
        bool generateCodeowners,
        bool discardPatches,
        CancellationToken cancellationToken);
}
