// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrUpdater
{
    /// <summary>
    /// Updates repo in the VMR to given revision.
    /// </summary>
    /// <param name="mappingName">Name of a repository mapping</param>
    /// <param name="targetRevision">Revision (commit SHA, branch, tag..) onto which to synchronize, leave empty for HEAD</param>
    /// <param name="updateDependencies">When true, updates dependencies (from Version.Details.xml) recursively</param>
    /// <param name="codeFlowParameters">Record containing parameters for VMR updates</param>
    /// <param name="lookUpBuilds">Whether to look up package versions and build number from BAR when populating version files</param>
    /// <returns>True if the repository was updated, false if it was already up to date</returns>
    Task<bool> UpdateRepository(
        string mappingName,
        string? targetRevision,
        bool updateDependencies,
        CodeFlowParameters codeFlowParameters,
        bool lookUpBuilds,
        bool resetToRemoteWhenCloningRepo = false,
        CancellationToken cancellationToken = default);
}
