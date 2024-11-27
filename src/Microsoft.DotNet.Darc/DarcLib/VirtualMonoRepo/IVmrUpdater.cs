// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Maestro.Client.Models;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrUpdater
{
    /// <summary>
    /// Updates repo in the VMR to given revision.
    /// </summary>
    /// <param name="mappingName">Name of a repository mapping</param>
    /// <param name="build">Build from which the code is being flown</param>
    /// <param name="updateDependencies">When true, updates dependencies (from Version.Details.xml) recursively</param>
    /// <param name="additionalRemotes">Additional git remotes to use when fetching</param>
    /// <param name="componentTemplatePath">Path to VMR's Component.md template</param>
    /// <param name="tpnTemplatePath">Path to VMR's THIRD-PARTY-NOTICES.md template</param>
    /// <param name="generateCodeowners">Whether to generate a CODEOWNERS file</param>
    /// <param name="generateCredScanSuppressions">Whether to generate a .config/CredScanSuppressions.json file</param>
    /// <param name="discardPatches">Whether to clean up genreated .patch files after their used</param>
    /// <param name="reapplyVmrPatches">Whether to reapply patches stored in the VMR</param>
    /// <returns>True if the repository was updated, false if it was already up to date</returns>
    Task<bool> UpdateRepository(
        string mappingName,
        Build build,
        bool updateDependencies,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        string? componentTemplatePath,
        string? tpnTemplatePath,
        bool generateCodeowners,
        bool generateCredScanSuppressions,
        bool discardPatches,
        bool reapplyVmrPatches,
        CancellationToken cancellationToken);
}
