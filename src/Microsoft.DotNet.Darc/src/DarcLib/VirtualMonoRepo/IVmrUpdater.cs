// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrUpdater : IVmrManager
{
    /// <summary>
    /// Updates repo in the VMR to given revision.
    /// </summary>
    /// <param name="mappingName">Name of a repository mapping</param>
    /// <param name="targetRevision">Revision (commit SHA, branch, tag..) onto which to synchronize, leave empty for HEAD</param>
    /// <param name="targetVersion">Version of packages, that the SHA we're updating to, produced</param>
    /// <param name="noSquash">Whether to pull changes commit by commit instead of squashing all updates into one</param>
    /// <param name="updateDependencies">When true, updates dependencies (from Version.Details.xml) recursively</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateRepository(
        string mappingName,
        string? targetRevision,
        string? targetVersion,
        bool noSquash,
        bool updateDependencies,
        CancellationToken cancellationToken)
    {
        var mapping = Mappings.FirstOrDefault(m => m.Name == mappingName)
            ?? throw new Exception($"No repository mapping named `{mappingName}` found!");

        return UpdateRepository(mapping, targetRevision, null, noSquash, updateDependencies, cancellationToken);
    }

    /// <summary>
    /// Updates repo in the VMR to given revision.
    /// </summary>
    /// <param name="mapping">Repository mapping</param>
    /// <param name="targetRevision">Revision (commit SHA, branch, tag..) onto which to synchronize, leave empty for HEAD</param>
    /// <param name="targetVersion">Version of packages, that the SHA we're updating to, produced</param>
    /// <param name="noSquash">Whether to pull changes commit by commit instead of squashing all updates into one</param>
    /// <param name="updateDependencies">When true, updates dependencies (from Version.Details.xml) recursively</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateRepository(
        SourceMapping mapping,
        string? targetRevision,
        string? targetVersion,
        bool noSquash,
        bool updateDependencies,
        CancellationToken cancellationToken);
}
