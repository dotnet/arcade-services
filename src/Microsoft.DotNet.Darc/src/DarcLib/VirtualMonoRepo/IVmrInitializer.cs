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

public interface IVmrInitializer : IVmrManager
{
    /// <summary>
    /// Initializes new repo that hasn't been synchronized into the VMR yet.
    /// </summary>
    /// <param name="mappingName">Name of a repository mapping</param>
    /// <param name="targetRevision">Revision (commit SHA, branch, tag..) onto which to synchronize, leave empty for HEAD</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task InitializeVmr(string mappingName, string? targetRevision, CancellationToken cancellationToken)
    {
        var mapping = Mappings.FirstOrDefault(m => m.Name == mappingName)
            ?? throw new Exception($"No repository mapping named `{mappingName}` found!");

        return InitializeVmr(mapping, targetRevision, cancellationToken);
    }

    /// <summary>
    /// Initializes new repo that hasn't been synchronized into the VMR yet.
    /// </summary>
    /// <param name="mapping">Repository mapping</param>
    /// <param name="targetRevision">Revision (commit SHA, branch, tag..) onto which to synchronize, leave empty for HEAD</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task InitializeVmr(SourceMapping mapping, string? targetRevision, CancellationToken cancellationToken);
}
