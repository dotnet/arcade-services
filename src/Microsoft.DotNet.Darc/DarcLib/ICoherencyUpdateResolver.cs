﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DotNet.Maestro.Client.Models;

namespace Microsoft.DotNet.DarcLib;

public interface ICoherencyUpdateResolver
{
    /// <summary>
    ///     Get updates required by coherency constraints.
    /// </summary>
    /// <param name="dependencies">Current set of dependencies.</param>
    /// <param name="remoteFactory">Remote factory for remote queries.</param>
    /// <returns>List of dependency updates.</returns>
    Task<List<DependencyUpdate>> GetRequiredCoherencyUpdatesAsync(
        IEnumerable<DependencyDetail> dependencies,
        IRemoteFactory remoteFactory);

    /// <summary>
    ///     Given a current set of dependencies, determine what non-coherency updates
    ///     are required.
    /// </summary>
    /// <param name="sourceRepoUri">Repository that <paramref name="assets"/> came from.</param>
    /// <param name="sourceCommit">Commit that <paramref name="assets"/> came from.</param>
    /// <param name="assets">Assets to apply</param>
    /// <param name="dependencies">Current set of dependencies.</param>
    /// <returns>List of dependency updates.</returns>
    List<DependencyUpdate> GetRequiredNonCoherencyUpdates(
        string sourceRepoUri,
        string sourceCommit,
        IEnumerable<AssetData> assets,
        IEnumerable<DependencyDetail> dependencies);
}
