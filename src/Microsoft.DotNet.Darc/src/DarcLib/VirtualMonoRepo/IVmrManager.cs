// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrManager
{
    IReadOnlyCollection<SourceMapping> Mappings { get; }

    /// <summary>
    /// Updates repo in the VMR to given revision.
    /// </summary>
    /// <param name="mapping">Repository mapping</param>
    /// <param name="targetRevision">Revision (commit SHA, branch, tag..) onto which to synchronize, leave empty for HEAD</param>
    /// <param name="noSquash">Whether to pull changes commit by commit instead of squashing all updates into one</param>
    /// <param name="ignoreWorkingTree">Does not keep working tree clean after commits for faster synchronization (changes are applied into the index directly)</param>
    Task UpdateVmr(
        SourceMapping mapping,
        string? targetRevision,
        bool noSquash,
        bool ignoreWorkingTree,
        CancellationToken cancellationToken);

    /// <summary>
    /// Initializes new repo that hasn't been synchronized into the VMR yet.
    /// </summary>
    /// <param name="mapping">Repository mapping</param>
    /// <param name="targetRevision">Revision (commit SHA, branch, tag..) onto which to synchronize, leave empty for HEAD</param>
    /// <param name="ignoreWorkingTree">Does not keep working tree clean after commits for faster synchronization (changes are applied into the index directly)</param>
    Task InitializeVmr(
        SourceMapping mapping,
        string? targetRevision,
        bool ignoreWorkingTree,
        CancellationToken cancellationToken);
}
