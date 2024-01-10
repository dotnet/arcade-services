// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib;

public interface IAssetLocationResolver
{
    /// <summary>
    ///     Update a list of dependencies with asset locations.
    /// </summary>
    /// <param name="dependencies">Dependencies to load locations for</param>
    /// <returns>Async task</returns>
    Task AddAssetLocationToDependenciesAsync(IReadOnlyCollection<DependencyDetail> dependencies);
}
